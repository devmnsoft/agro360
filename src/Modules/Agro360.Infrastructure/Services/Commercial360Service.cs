using Agro360.Application.Contracts;
using Agro360.Domain.Commercial;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Agro360.Infrastructure.Services;

public sealed class Commercial360Service(DatabaseExecutor db, ITenantContext tenant) : ICommercial360Service
{
    private static readonly Dictionary<string, string> Resources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["customers"] = "agro360.crm_customers",
        ["prospects"] = "agro360.crm_customers",
        ["segments"] = "agro360.crm_customer_segments",
        ["contacts"] = "agro360.crm_contacts",
        ["representatives"] = "agro360.sales_representatives",
        ["opportunities"] = "agro360.sales_opportunities",
        ["activities"] = "agro360.sales_activities",
        ["price-tables"] = "agro360.sales_price_tables",
        ["orders"] = "agro360.sales_orders",
        ["proposals"] = "agro360.sales_proposals",
        ["contracts"] = "agro360.sales_contracts",
        ["commissions"] = "agro360.sales_commissions",
        ["splits"] = "agro360.sales_split_agreements",
        ["targets"] = "agro360.sales_targets",
        ["products"] = "agro360.inventory_products"
    };

    public Task<CommercialPage<CommercialRecord>> ListAsync(string resource, string? search, string? status, int page, int pageSize, CancellationToken ct) => db.InTenantTransactionAsync<CommercialPage<CommercialRecord>>(async (c, t) =>
    {
        var table = Table(resource);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        if (resource.Equals("products", StringComparison.OrdinalIgnoreCase))
        {
            var pSearch = string.IsNullOrWhiteSpace(search) ? null : search;
            var sqlProducts = """
                select id, name, 'ACTIVE' as status, (sku || ' · ' || base_unit) as detail, 0::numeric as amount,
                       coalesce(updated_at, created_at) as updatedat, null as customername, null as currency
                from agro360.inventory_products
                where tenant_id = @TenantId and deleted_at is null
                  and (@Search is null or name ilike '%'||@Search||'%' or sku ilike '%'||@Search||'%' or code::text ilike '%'||@Search||'%')
                  and (@Status is null or @Status = 'ACTIVE')
                order by name, id
                limit @PageSize offset @Offset;

                select count(*)
                from agro360.inventory_products
                where tenant_id = @TenantId and deleted_at is null
                  and (@Search is null or name ilike '%'||@Search||'%' or sku ilike '%'||@Search||'%' or code::text ilike '%'||@Search||'%')
                  and (@Status is null or @Status = 'ACTIVE');
                """;
            using var multiP = await c.QueryMultipleAsync(sqlProducts, new { tenant.TenantId, Search = pSearch, Status = string.IsNullOrWhiteSpace(status) ? null : status, PageSize = pageSize, Offset = offset }, t);
            var rowsP = (await multiP.ReadAsync<CommercialRecord>()).ToArray();
            var totalP = await multiP.ReadSingleAsync<int>();
            return new(rowsP, page, pageSize, totalP);
        }

        if (resource.Equals("opportunities", StringComparison.OrdinalIgnoreCase))
        {
            var oppSearch = string.IsNullOrWhiteSpace(search) ? null : search;
            var sqlOpp = """
                select o.id, o.name, o.stage as status,
                       coalesce(c.name, '') as detail,
                       o.estimated_value as amount,
                       o.updated_at as updatedat,
                       c.name as customername,
                       'BRL' as currency
                from agro360.sales_opportunities o
                left join agro360.crm_customers c on c.tenant_id = o.tenant_id and c.id = o.customer_id
                where o.tenant_id = @TenantId and o.deleted_at is null
                  and (@Search is null or o.name ilike '%'||@Search||'%')
                  and (@Status is null or o.stage = @Status)
                order by o.updated_at desc, o.id desc
                limit @PageSize offset @Offset;

                select count(*)
                from agro360.sales_opportunities o
                where o.tenant_id = @TenantId and o.deleted_at is null
                  and (@Search is null or o.name ilike '%'||@Search||'%')
                  and (@Status is null or o.stage = @Status);
                """;
            using var multiOpp = await c.QueryMultipleAsync(sqlOpp, new { tenant.TenantId, Search = oppSearch, Status = string.IsNullOrWhiteSpace(status) ? null : status, PageSize = pageSize, Offset = offset }, t);
            var rowsOpp = (await multiOpp.ReadAsync<CommercialRecord>()).ToArray();
            var totalOpp = await multiOpp.ReadSingleAsync<int>();
            return new(rowsOpp, page, pageSize, totalOpp);
        }

        var typeClause = resource switch
        {
            "customers" => " and type = 'CUSTOMER'",
            "prospects" => " and type = 'PROSPECT'",
            _ => ""
        };
        var name = resource switch { "orders" => "order_number", "proposals" => "proposal_number", "commissions" => "coalesce(status,'EXPECTED')", "splits" => "name", _ => "name" };
        var detail = resource switch
        {
            "proposals" => "coalesce((select c.name from agro360.crm_customers c where c.tenant_id=agro360.sales_proposals.tenant_id and c.id=agro360.sales_proposals.customer_id), '') || ' · ' || coalesce((select currency from agro360.sales_proposal_versions v where v.tenant_id=agro360.sales_proposals.tenant_id and v.proposal_id=agro360.sales_proposals.id and v.version_number=agro360.sales_proposals.current_version), 'BRL')",
            "orders" => "coalesce((select c.name from agro360.crm_customers c where c.tenant_id=agro360.sales_orders.tenant_id and c.id=agro360.sales_orders.customer_id), '') || ' · ' || coalesce(currency, 'BRL')",
            "customers" => "coalesce(tax_document, email)",
            "prospects" => "coalesce(tax_document, email)",
            _ => "cast(null as text)"
        };
        var amount = resource switch
        {
            "orders" => "total_amount",
            "proposals" => "coalesce((select total_amount from agro360.sales_proposal_versions v where v.tenant_id=agro360.sales_proposals.tenant_id and v.proposal_id=agro360.sales_proposals.id and v.version_number=agro360.sales_proposals.current_version),0)",
            "contracts" => "contracted_value",
            "commissions" => "amount",
            "splits" => "0",
            _ => "0"
        };
        var customerNameCol = resource switch
        {
            "orders" => "(select c.name from agro360.crm_customers c where c.tenant_id=agro360.sales_orders.tenant_id and c.id=agro360.sales_orders.customer_id) as customername",
            "proposals" => "(select c.name from agro360.crm_customers c where c.tenant_id=agro360.sales_proposals.tenant_id and c.id=agro360.sales_proposals.customer_id) as customername",
            _ => "cast(null as text) as customername"
        };
        var currencyCol = resource switch
        {
            "orders" => "coalesce(currency, 'BRL') as currency",
            "proposals" => "coalesce((select currency from agro360.sales_proposal_versions v where v.tenant_id=agro360.sales_proposals.tenant_id and v.proposal_id=agro360.sales_proposals.id and v.version_number=agro360.sales_proposals.current_version), 'BRL') as currency",
            "contracts" => "coalesce(currency, 'BRL') as currency",
            _ => "cast(null as text) as currency"
        };

        var sql = $"select id,{name} name,status,{detail} detail,{amount} amount,updated_at updatedat,{customerNameCol},{currencyCol} from {table} where tenant_id=@TenantId and deleted_at is null{typeClause} and (@Search is null or {name} ilike '%'||@Search||'%') and (@Status is null or status=@Status) order by updated_at desc, id desc limit @PageSize offset @Offset; select count(*) from {table} where tenant_id=@TenantId and deleted_at is null{typeClause} and (@Search is null or {name} ilike '%'||@Search||'%') and (@Status is null or status=@Status)";
        using var multi = await c.QueryMultipleAsync(sql, new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(search) ? null : search, Status = string.IsNullOrWhiteSpace(status) ? null : status, PageSize = pageSize, Offset = offset }, t);
        var rows = (await multi.ReadAsync<CommercialRecord>()).ToArray();
        var total = await multi.ReadSingleAsync<int>();
        return new(rows, page, pageSize, total);
    }, ct);

    public Task<IReadOnlyList<CommercialLookup>> LookupAsync(string resource, string? search, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var table = Table(resource);
        var searchGuid = Guid.TryParse(search, out var g) ? g : (Guid?)null;
        var (sql, param) = resource switch
        {
            "products" => (
                """
                select id, (name || ' (' || sku || ')') as label, 'ACTIVE' as status, base_unit as unit, sku
                from agro360.inventory_products
                where tenant_id = @TenantId and deleted_at is null
                  and (@Search is null or name ilike '%'||@Search||'%' or sku ilike '%'||@Search||'%' or code::text ilike '%'||@Search||'%' or id = @SearchGuid)
                order by name, id
                limit 50
                """,
                (object)new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(search) ? null : search, SearchGuid = searchGuid }
            ),
            "opportunities" => (
                """
                select id, name as label, stage as status
                from agro360.sales_opportunities
                where tenant_id = @TenantId and deleted_at is null
                  and (@Search is null or name ilike '%'||@Search||'%' or id = @SearchGuid)
                order by name, id
                limit 50
                """,
                new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(search) ? null : search, SearchGuid = searchGuid }
            ),
            "customers" => (
                """
                select id, name as label, status
                from agro360.crm_customers
                where tenant_id = @TenantId and deleted_at is null and type = 'CUSTOMER'
                  and (@Search is null or name ilike '%'||@Search||'%' or tax_document ilike '%'||@Search||'%' or id = @SearchGuid)
                order by name, id
                limit 50
                """,
                new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(search) ? null : search, SearchGuid = searchGuid }
            ),
            "prospects" => (
                """
                select id, name as label, status
                from agro360.crm_customers
                where tenant_id = @TenantId and deleted_at is null and type = 'PROSPECT'
                  and (@Search is null or name ilike '%'||@Search||'%' or tax_document ilike '%'||@Search||'%' or id = @SearchGuid)
                order by name, id
                limit 50
                """,
                new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(search) ? null : search, SearchGuid = searchGuid }
            ),
            "orders" => (
                """
                select id, order_number as label, status
                from agro360.sales_orders
                where tenant_id = @TenantId and deleted_at is null
                  and (@Search is null or order_number ilike '%'||@Search||'%' or id = @SearchGuid)
                order by order_number, id
                limit 50
                """,
                new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(search) ? null : search, SearchGuid = searchGuid }
            ),
            "proposals" => (
                """
                select id, proposal_number as label, status
                from agro360.sales_proposals
                where tenant_id = @TenantId and deleted_at is null
                  and (@Search is null or proposal_number ilike '%'||@Search||'%' or id = @SearchGuid)
                order by proposal_number, id
                limit 50
                """,
                new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(search) ? null : search, SearchGuid = searchGuid }
            ),
            "contracts" => (
                """
                select id, coalesce(contract_number || ' - ' || name, name) as label, status
                from agro360.sales_contracts
                where tenant_id = @TenantId and deleted_at is null
                  and (@Search is null or name ilike '%'||@Search||'%' or contract_number ilike '%'||@Search||'%' or id = @SearchGuid)
                order by name, id
                limit 50
                """,
                new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(search) ? null : search, SearchGuid = searchGuid }
            ),
            _ => (
                $"""
                select id, name as label, status
                from {table}
                where tenant_id = @TenantId and deleted_at is null
                  and (@Search is null or name ilike '%'||@Search||'%' or id = @SearchGuid)
                order by name, id
                limit 50
                """,
                new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(search) ? null : search, SearchGuid = searchGuid }
            )
        };
        return (IReadOnlyList<CommercialLookup>)(await c.QueryAsync<CommercialLookup>(sql, param, t)).ToArray();
    }, ct);

    public Task<Guid> SaveCustomerAsync(Guid? id, CustomerCommand command, CancellationToken ct) { CommercialRules.ValidateTaxDocument(command.TaxDocument); return db.InTenantTransactionAsync(async (c, t) => { var entityId = id ?? Guid.CreateVersion7(); var n = await c.ExecuteAsync(id is null ? "insert into agro360.crm_customers(id,tenant_id,segment_id,representative_id,name,type,tax_document,email,phone,status,notes,created_by,updated_by) values(@Id,@TenantId,@SegmentId,@RepresentativeId,@Name,@Type,@TaxDocument,@Email,@Phone,'ACTIVE',@Notes,@UserId,@UserId)" : "update agro360.crm_customers set segment_id=@SegmentId,representative_id=@RepresentativeId,name=@Name,tax_document=@TaxDocument,email=@Email,phone=@Phone,notes=@Notes,updated_by=@UserId,updated_at=now() where id=@Id and tenant_id=@TenantId and deleted_at is null", new { Id = entityId, tenant.TenantId, command.SegmentId, command.RepresentativeId, command.Name, Type = command.Type.ToUpperInvariant(), command.TaxDocument, command.Email, command.Phone, command.Notes, tenant.UserId }, t); if (n == 0) throw new KeyNotFoundException("Cliente não encontrado."); return entityId; }, ct); }

    public Task<Guid> SaveOpportunityAsync(Guid? id, OpportunityCommand command, CancellationToken ct) { CommercialRules.ValidateOpportunity(command.Stage, command.EstimatedValue, command.LossReason); if (command.ExpectedClose is not null && command.ExpectedClose < DateOnly.FromDateTime(DateTime.UtcNow)) throw new ArgumentException("A data prevista não pode estar no passado."); return db.InTenantTransactionAsync(async (c, t) => { var entityId = id ?? Guid.CreateVersion7(); string? old = null; if (id is not null) old = await c.ExecuteScalarAsync<string?>("select stage from agro360.sales_opportunities where id=@Id and tenant_id=@TenantId", new { Id = entityId, tenant.TenantId }, t); var n = await c.ExecuteAsync(id is null ? "insert into agro360.sales_opportunities(id,tenant_id,customer_id,product_id,representative_id,name,estimated_value,stage,probability,expected_close,source,next_action,loss_reason,created_by,updated_by) values(@Id,@TenantId,@CustomerId,@ProductId,@RepresentativeId,@Name,@EstimatedValue,@Stage,@Probability,@ExpectedClose,@Source,@NextAction,@LossReason,@UserId,@UserId)" : "update agro360.sales_opportunities set customer_id=@CustomerId,product_id=@ProductId,representative_id=@RepresentativeId,name=@Name,estimated_value=@EstimatedValue,stage=@Stage,probability=@Probability,expected_close=@ExpectedClose,source=@Source,next_action=@NextAction,loss_reason=@LossReason,updated_by=@UserId,updated_at=now() where id=@Id and tenant_id=@TenantId and deleted_at is null", new { Id = entityId, tenant.TenantId, command.CustomerId, command.ProductId, command.RepresentativeId, command.Name, command.EstimatedValue, Stage = command.Stage.ToUpperInvariant(), command.Probability, command.ExpectedClose, command.Source, command.NextAction, command.LossReason, tenant.UserId }, t); if (n == 0) throw new KeyNotFoundException("Oportunidade não encontrada."); if (id is null || old != command.Stage) await c.ExecuteAsync("insert into agro360.sales_opportunity_history(id,tenant_id,opportunity_id,from_stage,to_stage,changed_by) values(gen_random_uuid(),@TenantId,@Id,@Old,@Stage,@UserId)", new { tenant.TenantId, Id = entityId, Old = old, Stage = command.Stage.ToUpperInvariant(), tenant.UserId }, t); return entityId; }, ct); }

    public Task<Guid> SaveActivityAsync(Guid? id, ActivityCommand command, CancellationToken ct) { if (command.Status == "COMPLETED" && string.IsNullOrWhiteSpace(command.Result)) throw new ArgumentException("Atividade concluída exige resultado."); if (command.Status == "CANCELLED" && string.IsNullOrWhiteSpace(command.CancellationReason)) throw new ArgumentException("Cancelamento exige motivo."); return db.InTenantTransactionAsync(async (c, t) => { var entityId = id ?? Guid.CreateVersion7(); var n = await c.ExecuteAsync(id is null ? "insert into agro360.sales_activities(id,tenant_id,customer_id,representative_id,name,type,scheduled_at,status,channel,result,next_action,notes,cancellation_reason,created_by,updated_by) values(@Id,@TenantId,@CustomerId,@RepresentativeId,@Type,@Type,@ScheduledAt,@Status,@Channel,@Result,@NextAction,@Notes,@CancellationReason,@UserId,@UserId)" : "update agro360.sales_activities set customer_id=@CustomerId,representative_id=@RepresentativeId,type=@Type,name=@Type,scheduled_at=@ScheduledAt,status=@Status,channel=@Channel,result=@Result,next_action=@NextAction,notes=@Notes,cancellation_reason=@CancellationReason,updated_by=@UserId,updated_at=now() where id=@Id and tenant_id=@TenantId", new { Id = entityId, tenant.TenantId, command.CustomerId, command.RepresentativeId, Type = command.Type.ToUpperInvariant(), command.ScheduledAt, Status = command.Status.ToUpperInvariant(), command.Channel, command.Result, command.NextAction, command.Notes, command.CancellationReason, tenant.UserId }, t); if (n == 0) throw new KeyNotFoundException(); return entityId; }, ct); }

    public Task<Guid> CreateContractAsync(CommercialContractCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var type = CommercialRules.NormalizeContractType(command.Type);
        CommercialRules.ValidateContract(type, command.Quantity, command.UnitPrice, command.ValidFrom, command.ValidTo, command.CommercialTerms, command.Currency, command.Incoterm);
        var existing = await c.ExecuteScalarAsync<Guid?>("select id from agro360.sales_contracts where x.tenant_id=@TenantId and x.idempotency_key=@IdempotencyKey", new { tenant.TenantId, command.IdempotencyKey }, t);
        if (existing.HasValue) return existing.Value;
        var customerStatus = await c.ExecuteScalarAsync<string?>("select status from agro360.crm_customers where tenant_id=@TenantId and id=@CustomerId and deleted_at is null", new { tenant.TenantId, command.CustomerId }, t);
        if (customerStatus is null) throw new KeyNotFoundException("Cliente não encontrado.");
        var id = Guid.CreateVersion7();
        var value = decimal.Round(command.Quantity * command.UnitPrice, 2, MidpointRounding.AwayFromZero);
        await c.ExecuteAsync("insert into agro360.sales_contracts(id,tenant_id,customer_id,product_id,representative_id,name,contract_number,type,contracted_quantity,unit,unit_price,currency,contracted_value,valid_from,valid_to,payment_terms,commercial_terms,incoterm,status,notes,idempotency_key,version,created_by,updated_by) values(@Id,@TenantId,@CustomerId,@ProductId,@RepresentativeId,'Contrato '||@Type,'CTR-'||nextval('agro360.sales_contract_number_seq'),@Type,@Quantity,@Unit,@UnitPrice,@Currency,@Value,@ValidFrom,@ValidTo,@CommercialTerms,@CommercialTerms,@Incoterm,'DRAFT',@Notes,@IdempotencyKey,1,@UserId,@UserId)", new { Id = id, tenant.TenantId, command.CustomerId, command.ProductId, command.RepresentativeId, Type = type, command.Quantity, Unit = command.Unit.Trim().ToUpperInvariant(), command.UnitPrice, Currency = command.Currency.Trim().ToUpperInvariant(), Value = value, command.ValidFrom, command.ValidTo, command.CommercialTerms, Incoterm = command.Incoterm?.Trim().ToUpperInvariant(), command.Notes, command.IdempotencyKey, tenant.UserId }, t);
        await c.ExecuteAsync("insert into agro360.sales_contract_versions(id,tenant_id,contract_id,version_number,snapshot,reason,created_by) values(gen_random_uuid(),@TenantId,@Id,1,jsonb_build_object('type',@Type,'quantity',@Quantity,'unitPrice',@UnitPrice,'currency',@Currency),'Criação',@UserId); insert into agro360.sales_commercial_events(id,tenant_id,event_type,aggregate_id,payload,created_by) values(gen_random_uuid(),@TenantId,'CONTRACT_CREATED',@Id,jsonb_build_object('status','DRAFT'),@UserId)", new { tenant.TenantId, Id = id, Type = type, command.Quantity, command.UnitPrice, Currency = command.Currency.Trim().ToUpperInvariant(), tenant.UserId }, t);
        return id;
    }, ct);

    public Task ChangeContractStatusAsync(Guid id, StatusCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var next = command.Status.Trim().ToUpperInvariant();
        var contract = await c.QuerySingleOrDefaultAsync<(string Status, string CustomerStatus, long Version)>("select s.status,c.status customerstatus,s.version from agro360.sales_contracts s join agro360.crm_customers c on c.tenant_id=s.tenant_id and c.id=s.customer_id where s.tenant_id=@TenantId and s.id=@Id and s.deleted_at is null for update of s", new { tenant.TenantId, Id = id }, t);
        if (contract == default) throw new KeyNotFoundException("Contrato não encontrado.");
        CommercialRules.ValidateContractTransition(contract.Status, next, command.Reason);
        if (next is "APPROVED" or "ACTIVE") CommercialRules.CustomerCanOrder(contract.CustomerStatus, false);
        await c.ExecuteAsync("update agro360.sales_contracts set status=@Next,cancellation_reason=case when @Next='CANCELLED' then @Reason else cancellation_reason end,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and version=@Version; insert into agro360.sales_commercial_events(id,tenant_id,event_type,aggregate_id,payload,created_by) values(gen_random_uuid(),@TenantId,'CONTRACT_'||@Next,@Id,jsonb_build_object('from',@Current,'status',@Next,'reason',@Reason),@UserId)", new { tenant.TenantId, Id = id, Next = next, Current = contract.Status, command.Reason, tenant.UserId, contract.Version }, t);
    }, ct);

    public Task<Guid> CreateOrderAsync(SalesOrderCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        if (command.ExpectedDelivery is not null && command.ExpectedDelivery < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("A data de entrega não pode estar no passado.");

        var customer = await c.QuerySingleOrDefaultAsync<(string Status, Guid SegmentId)>(
            "select status,segment_id SegmentId from agro360.crm_customers where id=@CustomerId and tenant_id=@TenantId and deleted_at is null",
            new { tenant.TenantId, command.CustomerId }, t);
        if (customer == default) throw new KeyNotFoundException("Cliente não encontrado.");
        CommercialRules.CustomerCanOrder(customer.Status, false);

        if (command.ContractId is { } contractId)
        {
            var contract = await c.QuerySingleOrDefaultAsync<(string Status, decimal ContractedQuantity, decimal FulfilledQuantity)>(
                "select s.status,s.contracted_quantity ContractedQuantity,greatest(s.delivered_quantity,coalesce((select sum(i.quantity) from agro360.sales_orders o join agro360.sales_order_items i on i.tenant_id=o.tenant_id and i.order_id=o.id where o.tenant_id=s.tenant_id and o.contract_id=s.id and o.status not in('CANCELLED','RETURNED') and o.deleted_at is null),0)) FulfilledQuantity from agro360.sales_contracts s where s.id=@ContractId and s.customer_id=@CustomerId and s.tenant_id=@TenantId and s.deleted_at is null for update",
                new { ContractId = contractId, command.CustomerId, tenant.TenantId }, t);
            if (contract == default) throw new KeyNotFoundException("Contrato não encontrado para o cliente informado.");
            CommercialRules.EnsureContractAcceptsOrders(contract.Status);
            CommercialRules.ValidateContractBalance(contract.ContractedQuantity, contract.FulfilledQuantity, command.Items.Sum(x => x.Quantity));
        }

        var policyRows = (await c.QueryAsync<SalesPricePolicyLookup>(
            "select p.id PriceTableId,i.product_id ProductId,i.unit,i.base_price BasePrice,i.maximum_discount MaximumDiscount,p.is_default IsDefault,p.valid_from ValidFrom,p.updated_at UpdatedAt from agro360.sales_price_tables p join agro360.sales_price_table_items i on i.tenant_id=p.tenant_id and i.price_table_id=p.id where p.tenant_id=@TenantId and p.segment_id=@SegmentId and p.status='ACTIVE' and p.deleted_at is null and current_date between p.valid_from and p.valid_to and i.product_id=any(@ProductIds) order by p.is_default desc,p.valid_from desc,p.updated_at desc,p.id",
            new { tenant.TenantId, customer.SegmentId, ProductIds = command.Items.Select(x => x.ProductId).Distinct().ToArray() }, t)).ToArray();
        var policies = policyRows
            .GroupBy(x => (x.ProductId, Unit: x.Unit.ToUpperInvariant()))
            .ToDictionary(x => x.Key, x => x.First());
        var pricedItems = command.Items.Select(item =>
        {
            if (!policies.TryGetValue((item.ProductId, item.Unit.Trim().ToUpperInvariant()), out var policy))
                throw new DomainException("Produto e unidade não possuem política comercial vigente para o segmento do cliente.", "sales.price_policy_missing");
            return (Item: item, Policy: policy);
        }).ToArray();
        var calculations = CommercialRules.CalculateOrder(pricedItems.Select(x => (x.Item.Quantity, x.Item.UnitPrice, x.Item.DiscountPercentage, x.Policy.BasePrice, x.Policy.MaximumDiscount)));
        var total = CommercialRules.OrderTotal(calculations, command.Freight);
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync("insert into agro360.sales_orders(id,tenant_id,customer_id,representative_id,property_id,contract_id,order_number,status,total_amount,freight,payment_terms,expected_delivery,notes,created_by,updated_by) values(@Id,@TenantId,@CustomerId,@RepresentativeId,@PropertyId,@ContractId,'PED-'||nextval('agro360.sales_order_number_seq'),'DRAFT',@Total,@Freight,@PaymentTerms,@ExpectedDelivery,@Notes,@UserId,@UserId)", new { Id = id, tenant.TenantId, command.CustomerId, command.RepresentativeId, command.PropertyId, command.ContractId, Total = total, command.Freight, command.PaymentTerms, command.ExpectedDelivery, command.Notes, tenant.UserId }, t);
        for (var index = 0; index < pricedItems.Length; index++)
        {
            var item = pricedItems[index].Item;
            var policy = pricedItems[index].Policy;
            var calculation = calculations[index];
            await c.ExecuteAsync("insert into agro360.sales_order_items(id,tenant_id,order_id,product_id,lot_id,quantity,unit,unit_price,discount_percentage,total_amount,price_table_id,base_unit_price,pricing_snapshot) values(gen_random_uuid(),@TenantId,@OrderId,@ProductId,@LotId,@Quantity,@Unit,@UnitPrice,@DiscountPercentage,@LineTotal,@PriceTableId,@BasePrice,jsonb_build_object('basePrice',@BasePrice,'maximumDiscount',@MaximumDiscount,'effectiveDiscount',@EffectiveDiscount,'rounding','line-half-away-from-zero'))", new { tenant.TenantId, OrderId = id, item.ProductId, item.LotId, item.Quantity, Unit = item.Unit.Trim().ToUpperInvariant(), item.UnitPrice, item.DiscountPercentage, LineTotal = calculation.LineTotal, policy.PriceTableId, calculation.BasePrice, calculation.MaximumDiscount, calculation.EffectiveDiscount }, t);
        }
        return id;
    }, ct);

    public Task ChangeOrderStatusAsync(Guid id, StatusCommand command, bool mayOverrideBlock, CancellationToken ct)
    {
        var next = CommercialRules.NormalizeOrderStatus(command.Status);
        if (next is "RESERVED" or "FULFILLMENT" or "INVOICED" or "DELIVERED" or "RETURNED")
            throw new DomainException($"O status '{next}' decorre de fatos operacionais e deve ser registrado pelo módulo correspondente (Logística ou Faturamento).", "sales.order_operational_status_disallowed");

        return db.InTenantTransactionAsync(async (c, t) =>
        {
            var order = await c.QuerySingleOrDefaultAsync<(string Status, string CustomerStatus)>(
            "select o.status,c.status CustomerStatus from agro360.sales_orders o join agro360.crm_customers c on c.id=o.customer_id and c.tenant_id=o.tenant_id where o.id=@Id and o.tenant_id=@TenantId and o.deleted_at is null for update of o",
            new { Id = id, tenant.TenantId }, t);
        if (order == default) throw new KeyNotFoundException("Pedido não encontrado.");
        CommercialRules.ValidateOrderTransition(order.Status, next, command.Reason);

        if (next == "APPROVED")
        {
            CommercialRules.CustomerCanOrder(order.CustomerStatus, mayOverrideBlock);
            if (!await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.sales_order_items where order_id=@Id and tenant_id=@TenantId)", new { Id = id, tenant.TenantId }, t))
                throw new ArgumentException("Pedido sem itens.");
        }

        await c.ExecuteAsync("update agro360.sales_orders set status=@Status,cancellation_reason=case when @Status='CANCELLED' then @Reason end,updated_by=@UserId,updated_at=now() where id=@Id and tenant_id=@TenantId and status=@Current", new { Id = id, tenant.TenantId, Status = next, Current = order.Status, command.Reason, tenant.UserId }, t);
        await c.ExecuteAsync("insert into agro360.sales_commercial_events(id,tenant_id,event_type,aggregate_id,payload,created_by) values(gen_random_uuid(),@TenantId,@Type,@Id,jsonb_build_object('from',@Current,'status',@Status,'reason',@Reason),@UserId)", new { tenant.TenantId, Type = $"ORDER_{next}", Id = id, Current = order.Status, Status = next, command.Reason, tenant.UserId }, t);
        if (next == "CANCELLED")
        {
            await c.ExecuteAsync("update agro360.sales_commissions set status='CANCELLED',updated_at=now() where tenant_id=@TenantId and order_id=@Id and status not in('PAID','REVERSED'); update agro360.sales_split_entries set status='CANCELLED',updated_at=now() where tenant_id=@TenantId and order_id=@Id and status='EXPECTED'", new { tenant.TenantId, Id = id }, t);

            // Release any active fulfillment reservations to restore stock balances without leaving orphan reservations
            var activeReservations = (await c.QueryAsync<(Guid Id, Guid LotId, decimal Active)>(
                "select r.id, r.stock_lot_id LotId, (r.quantity - r.consumed_quantity - r.released_quantity) Active " +
                "from agro360.fulfillment_reservations r " +
                "join agro360.sales_order_items oi on oi.tenant_id = r.tenant_id and oi.id = r.order_item_id " +
                "where r.tenant_id = @TenantId and oi.order_id = @Id and r.status = 'ACTIVE' " +
                "order by r.id for update",
                new { tenant.TenantId, Id = id }, t)).AsList();

            foreach (var res in activeReservations)
            {
                if (res.Active > 0)
                {
                    await c.ExecuteAsync(
                        "update agro360.fulfillment_reservations set released_quantity = released_quantity + @Amount, status = 'CANCELLED', version = version + 1, updated_at = now(), updated_by = @UserId where tenant_id = @TenantId and id = @ReservationId and status = 'ACTIVE'; " +
                        "update agro360.inventory_stock_balances b set reserved = reserved - @Amount, version = version + 1, updated_at = now() from agro360.inventory_stock_lots l where l.tenant_id = b.tenant_id and l.id = @LotId and b.tenant_id = @TenantId and b.warehouse_id = l.warehouse_id and b.product_id = l.product_id and b.reserved >= @Amount",
                        new { tenant.TenantId, ReservationId = res.Id, res.LotId, Amount = res.Active, tenant.UserId }, t);
                }
            }
        }
    }, ct);
    }

    public Task<Guid> CalculateCommissionAsync(CommissionCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var status = await c.ExecuteScalarAsync<string?>("select status from agro360.sales_orders where id=@OrderId and tenant_id=@TenantId and deleted_at is null", new { tenant.TenantId, command.OrderId }, t);
        if (status is null) throw new KeyNotFoundException("Pedido não encontrado.");
        if (!CommercialRules.IsCommissionEligible(status))
            throw new DomainException("O status do pedido não é elegível para comissão.", "sales.commission_status_ineligible");
        var amount = CommercialRules.Commission(command.Basis, command.Percentage, command.FixedValue);
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync("insert into agro360.sales_commissions(id,tenant_id,order_id,rule_id,representative_id,basis,percentage,fixed_value,amount,status,created_by,updated_by) values(@Id,@TenantId,@OrderId,@RuleId,@RepresentativeId,@Basis,@Percentage,@FixedValue,@Amount,'EXPECTED',@UserId,@UserId)", new { Id = id, tenant.TenantId, command.OrderId, command.RuleId, command.RepresentativeId, command.Basis, command.Percentage, command.FixedValue, Amount = amount, tenant.UserId }, t);
        return id;
    }, ct);
    public Task ChangeCommissionStatusAsync(Guid id, StatusCommand command, CancellationToken ct) => ChangeStatus("agro360.sales_commissions", id, command, true, ct);
    public Task<Guid> SaveSplitAsync(SplitAgreementCommand command, CancellationToken ct) { CommercialRules.ValidateSplit(command.Participants.Select(p => (p.ParticipantId, p.Percentage, p.FixedValue))); return db.InTenantTransactionAsync(async (c, t) => { var id = Guid.CreateVersion7(); await c.ExecuteAsync("insert into agro360.sales_split_agreements(id,tenant_id,order_id,contract_id,name,status,release_rule,created_by,updated_by) values(@Id,@TenantId,@OrderId,@ContractId,@Name,'DRAFT',@ReleaseRule,@UserId,@UserId)", new { Id = id, tenant.TenantId, command.OrderId, command.ContractId, command.Name, command.ReleaseRule, tenant.UserId }, t); foreach (var p in command.Participants) await c.ExecuteAsync("insert into agro360.sales_split_participants(id,tenant_id,agreement_id,participant_id,participant_type,percentage,fixed_value,priority) values(gen_random_uuid(),@TenantId,@AgreementId,@ParticipantId,@ParticipantType,@Percentage,@FixedValue,@Priority)", new { tenant.TenantId, AgreementId = id, p.ParticipantId, p.ParticipantType, p.Percentage, p.FixedValue, p.Priority }, t); return id; }, ct); }
    public Task ChangeSplitStatusAsync(Guid id, StatusCommand command, CancellationToken ct) => ChangeStatus("agro360.sales_split_agreements", id, command, false, ct);
    private Task ChangeStatus(string table, Guid id, StatusCommand command, bool reasonRequired, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) => { var status = command.Status.ToUpperInvariant(); if (reasonRequired && status is ("BLOCKED" or "REVERSED") && string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("Informe a justificativa."); var n = await c.ExecuteAsync($"update {table} set status=@Status,status_reason=@Reason,updated_by=@UserId,updated_at=now() where id=@Id and tenant_id=@TenantId and deleted_at is null", new { Id = id, tenant.TenantId, Status = status, command.Reason, tenant.UserId }, t); if (n == 0) throw new KeyNotFoundException(); }, ct);

    public Task<Guid> CreateProposalAsync(SalesProposalCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var id = Guid.CreateVersion7();
        await EnsureProposalReferences(c, t, command);
        await c.ExecuteAsync("insert into agro360.sales_proposals(id,tenant_id,customer_id,opportunity_id,representative_id,proposal_number,status,current_version,created_by,updated_by) values(@Id,@TenantId,@CustomerId,@OpportunityId,@RepresentativeId,'PROP-'||nextval('agro360.sales_proposal_number_seq'),'DRAFT',1,@UserId,@UserId)", new { Id = id, tenant.TenantId, command.CustomerId, command.OpportunityId, command.RepresentativeId, tenant.UserId }, t);
        await InsertProposalVersion(c, t, id, 1, command, null);
        return id;
    }, ct);

    public Task<long> ReviseProposalAsync(Guid id, SalesProposalCommand command, CancellationToken ct)
    {
        if (!command.ExpectedVersion.HasValue || command.ExpectedVersion.Value <= 0)
            throw new DomainException("A versão esperada (ExpectedVersion) deve ser positiva e informada na revisão.", "sales.proposal_expected_version_required");

        return db.InTenantTransactionAsync(async (c, t) =>
        {
            // Lock proposal row first under transaction so concurrent revisions serialize before any snapshot
            var row = await c.QuerySingleOrDefaultAsync<(string Status, long CurrentVersion)>("select status,current_version CurrentVersion from agro360.sales_proposals where tenant_id=@TenantId and id=@Id and deleted_at is null for update", new { tenant.TenantId, Id = id }, t);
            if (row == default) throw new KeyNotFoundException("Proposta não encontrada.");
            if (row.Status is "ACCEPTED" or "CANCELLED" or "EXPIRED") throw new DomainException("Proposta encerrada não pode ser revisada.", "sales.proposal_revision_forbidden");
            if (command.ExpectedVersion.Value != row.CurrentVersion)
                throw new ConflictException($"A proposta foi alterada concorrentemente (versão atual: {row.CurrentVersion}, esperada: {command.ExpectedVersion.Value}). Recarregue os dados antes de revisar.");

            await EnsureProposalReferences(c, t, command);
            if (row.Status != "DRAFT" && string.IsNullOrWhiteSpace(command.ChangeReason)) throw new DomainException("Alteração material exige motivo e nova versão.", "sales.proposal_change_reason_required");
            var next = row.CurrentVersion + 1;
            await InsertProposalVersion(c, t, id, next, command, row.CurrentVersion);
            await c.ExecuteAsync("update agro360.sales_proposals set customer_id=@CustomerId,opportunity_id=@OpportunityId,representative_id=@RepresentativeId,current_version=@Next,status='DRAFT',accepted_version=null,accepted_at=null,accepted_by=null,acceptance_evidence_type=null,acceptance_evidence_reference=null,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and current_version=@Current", new { tenant.TenantId, Id = id, command.CustomerId, command.OpportunityId, command.RepresentativeId, Next = next, Current = row.CurrentVersion, tenant.UserId }, t);
            return next;
        }, ct);
    }

    public Task<ProposalVersionView> GetProposalAsync(Guid id, long? version, CancellationToken ct) => db.InTenantTransactionAsync<ProposalVersionView>(async (c, t) =>
    {
        var header = await c.QuerySingleOrDefaultAsync<ProposalHeader>(
            "select p.id ProposalId,p.proposal_number Number,p.status,p.current_version CurrentVersion,p.accepted_version AcceptedVersion,p.customer_id CustomerId,coalesce(c.name, 'Cliente') CustomerName,p.opportunity_id OpportunityId,p.representative_id RepresentativeId,v.version_number Version,v.currency,v.valid_until ValidUntil,v.freight,v.items_total ItemsTotal,v.total_amount Total,v.payment_terms PaymentTerms,v.change_reason ChangeReason " +
            "from agro360.sales_proposals p " +
            "left join agro360.crm_customers c on c.tenant_id=p.tenant_id and c.id=p.customer_id " +
            "join agro360.sales_proposal_versions v on v.tenant_id=p.tenant_id and v.proposal_id=p.id and v.version_number=coalesce(@Version,p.current_version) " +
            "where p.tenant_id=@TenantId and p.id=@Id and p.deleted_at is null",
            new { tenant.TenantId, Id = id, Version = version }, t);
        if (header is null) throw new KeyNotFoundException("Proposta ou versão não encontrada.");
        var items = (await c.QueryAsync<ProposalItemView>(
            "select i.id,i.product_id ProductId,i.unit,i.quantity,i.unit_price UnitPrice,i.discount_percentage DiscountPercentage,i.total_amount Total,coalesce((select sum(ci.quantity) from agro360.sales_proposal_conversion_items ci where ci.tenant_id=i.tenant_id and ci.proposal_item_id=i.id),0) ConvertedQuantity,i.pricing_snapshot::text PricingSnapshot,coalesce(pr.name, 'Produto') ProductName " +
            "from agro360.sales_proposal_items i " +
            "left join agro360.inventory_products pr on pr.tenant_id=i.tenant_id and pr.id=i.product_id " +
            "where i.tenant_id=@TenantId and i.proposal_id=@Id and i.version_number=@Version order by i.created_at,i.id",
            new { tenant.TenantId, Id = id, header.Version }, t)).ToArray();
        return new(header.ProposalId, header.Number, header.Status, header.Version, header.CustomerId, header.Currency, header.ValidUntil, header.Freight, header.ItemsTotal, header.Total, header.PaymentTerms, items, header.CustomerName, header.CurrentVersion, header.AcceptedVersion, header.ChangeReason, header.OpportunityId, header.RepresentativeId);
    }, ct);

    public Task<SalesOrderDetailView> GetOrderAsync(Guid id, CancellationToken ct) => db.InTenantTransactionAsync<SalesOrderDetailView>(async (c, t) =>
    {
        var order = await c.QuerySingleOrDefaultAsync<OrderHeaderDto>(
            """
            select o.id, o.order_number OrderNumber, o.customer_id CustomerId, coalesce(c.name, 'Cliente') CustomerName,
                   o.status, coalesce(o.currency, 'BRL') Currency, o.total_amount TotalAmount, o.freight,
                   o.payment_terms PaymentTerms, o.expected_delivery ExpectedDelivery, o.notes,
                   o.created_at CreatedAt, o.updated_at UpdatedAt,
                   pc.proposal_id ProposalId, p.proposal_number ProposalNumber, pc.version_number ProposalVersion
            from agro360.sales_orders o
            left join agro360.crm_customers c on c.tenant_id = o.tenant_id and c.id = o.customer_id
            left join agro360.sales_proposal_conversions pc on pc.tenant_id = o.tenant_id and pc.order_id = o.id
            left join agro360.sales_proposals p on p.tenant_id = o.tenant_id and p.id = pc.proposal_id
            where o.tenant_id = @TenantId and o.id = @Id and o.deleted_at is null
            """,
            new { tenant.TenantId, Id = id }, t);

        if (order is null) throw new KeyNotFoundException("Pedido não encontrado.");

        var items = (await c.QueryAsync<SalesOrderItemDetailView>(
            """
            select i.id, i.product_id ProductId, coalesce(pr.name, 'Produto') ProductName,
                   i.unit, i.quantity, i.unit_price UnitPrice, i.discount_percentage DiscountPercentage,
                   i.total_amount TotalAmount, i.price_table_id PriceTableId, i.base_unit_price BaseUnitPrice
            from agro360.sales_order_items i
            left join agro360.inventory_products pr on pr.tenant_id = i.tenant_id and pr.id = i.product_id
            where i.tenant_id = @TenantId and i.order_id = @Id
            order by i.created_at, i.id
            """,
            new { tenant.TenantId, Id = id }, t)).ToArray();

        var fulfillments = (await c.QueryAsync<SalesOrderFulfillmentView>(
            """
            select distinct s.id ShipmentId, s.number ShipmentNumber, s.status Status, s.created_at CreatedAt
            from agro360.fulfillment_shipments s
            join agro360.fulfillment_shipment_items si on si.tenant_id = s.tenant_id and si.shipment_id = s.id
            join agro360.sales_order_items oi on oi.tenant_id = si.tenant_id and oi.id = si.order_item_id
            where s.tenant_id = @TenantId and oi.order_id = @Id and s.deleted_at is null
            order by s.created_at desc
            """,
            new { tenant.TenantId, Id = id }, t)).ToArray();

        var events = (await c.QueryAsync<SalesOrderEventView>(
            """
            select event_type EventType, payload::text Details, created_at OccurredAt
            from agro360.sales_commercial_events
            where tenant_id = @TenantId and aggregate_id = @Id
            order by created_at desc
            """,
            new { tenant.TenantId, Id = id }, t)).ToArray();

        var itemsTotal = items.Sum(x => x.TotalAmount);
        var nextAction = order.Status switch
        {
            "DRAFT" => "Submeter para aprovação ou cancelar",
            "UNDER_REVIEW" => "Aprovar ou rejeitar pedido",
            "APPROVED" => "Atender expedição na Logística ou cancelar",
            "FULFILLMENT" => "Acompanhar separação e conferência na Logística",
            "INVOICED" => "Aguardando entrega ao cliente",
            "DELIVERED" => "Pedido entregue e finalizado",
            "CANCELLED" => "Pedido cancelado",
            "RETURNED" => "Pedido devolvido",
            _ => "Consultar status"
        };

        return new SalesOrderDetailView(
            order.Id,
            order.OrderNumber,
            order.CustomerId,
            order.CustomerName,
            order.Status,
            order.Currency,
            itemsTotal,
            order.Freight,
            order.TotalAmount,
            order.PaymentTerms,
            order.ExpectedDelivery,
            order.Notes,
            order.ProposalId,
            order.ProposalNumber,
            order.ProposalVersion,
            order.CreatedAt,
            order.UpdatedAt,
            items,
            fulfillments,
            events,
            nextAction);
    }, ct);

    public Task DecideProposalAsync(Guid id, ProposalDecisionCommand command, string decision, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var next = decision.Trim().ToUpperInvariant();
        var row = await c.QuerySingleOrDefaultAsync<(string Status, long CurrentVersion)>("select status,current_version CurrentVersion from agro360.sales_proposals where tenant_id=@TenantId and id=@Id and deleted_at is null for update", new { tenant.TenantId, Id = id }, t);
        if (row == default) throw new KeyNotFoundException("Proposta não encontrada.");
        if (command.Version != row.CurrentVersion) throw new ConflictException("A proposta possui uma versão mais recente.");
        CommercialRules.ValidateProposalTransition(row.Status, next, command.Reason);
        await c.ExecuteAsync("insert into agro360.sales_proposal_decisions(id,tenant_id,proposal_id,version_number,decision,reason,decided_by) values(gen_random_uuid(),@TenantId,@Id,@Version,@Decision,@Reason,@UserId); update agro360.sales_proposals set status=@Decision,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and current_version=@Version", new { tenant.TenantId, Id = id, command.Version, Decision = next, command.Reason, tenant.UserId }, t);
    }, ct);

    public Task AcceptProposalAsync(Guid id, ProposalAcceptanceCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var row = await c.QuerySingleOrDefaultAsync<(string Status, long CurrentVersion, DateOnly ValidUntil)>("select p.status,p.current_version CurrentVersion,v.valid_until ValidUntil from agro360.sales_proposals p join agro360.sales_proposal_versions v on v.tenant_id=p.tenant_id and v.proposal_id=p.id and v.version_number=p.current_version where p.tenant_id=@TenantId and p.id=@Id and p.deleted_at is null for update of p", new { tenant.TenantId, Id = id }, t);
        if (row == default) throw new KeyNotFoundException("Proposta não encontrada.");
        if (command.Version != row.CurrentVersion) throw new ConflictException("Versão substituída não pode ser aceita.");
        CommercialRules.EnsureProposalAcceptable(row.Status, row.ValidUntil, DateOnly.FromDateTime(DateTime.UtcNow));
        var acceptedAt = command.AcceptedAt ?? DateTimeOffset.UtcNow;
        await c.ExecuteAsync("insert into agro360.sales_proposal_decisions(id,tenant_id,proposal_id,version_number,decision,reason,decided_at,decided_by) values(gen_random_uuid(),@TenantId,@Id,@Version,'ACCEPTED',@Evidence,@AcceptedAt,@UserId); update agro360.sales_proposals set status='ACCEPTED',accepted_version=@Version,accepted_at=@AcceptedAt,accepted_by=@UserId,acceptance_evidence_type=@EvidenceType,acceptance_evidence_reference=@Evidence,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id", new { tenant.TenantId, Id = id, command.Version, command.EvidenceType, Evidence = command.EvidenceReference, AcceptedAt = acceptedAt, tenant.UserId }, t);
    }, ct);

    public Task<ProposalConversionResult> ConvertProposalAsync(Guid id, ProposalConversionCommand command, CancellationToken ct) => db.InTenantTransactionAsync<ProposalConversionResult>(async (c, t) =>
    {
        var normalizedRequest = new { ProposalId = id, command.Version, Items = command.Items.OrderBy(x => x.ProposalItemId).Select(x => new { x.ProposalItemId, x.Quantity }).ToArray() };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalizedRequest)))).ToLowerInvariant();
        var previous = await c.QuerySingleOrDefaultAsync<(Guid OrderId, string RequestHash, string Currency, decimal Total, string OrderNumber)>("select x.order_id OrderId,x.request_hash RequestHash,o.currency,o.total_amount Total,o.order_number OrderNumber from agro360.sales_proposal_conversions x join agro360.sales_orders o on o.tenant_id=x.tenant_id and o.id=x.order_id where x.tenant_id=@TenantId and x.idempotency_key=@IdempotencyKey", new { tenant.TenantId, command.IdempotencyKey }, t);
        if (previous != default) return previous.RequestHash == hash ? new(previous.OrderId, true, previous.Currency, previous.Total, previous.OrderNumber) : throw new ConflictException("Chave idempotente já utilizada com conteúdo diferente.");
        var proposal = await c.QuerySingleOrDefaultAsync<(string Status, long CurrentVersion, Guid CustomerId, Guid? RepresentativeId, string PaymentTerms, decimal Freight, string Currency, string CustomerStatus, string CustomerType)>("select p.status,p.current_version CurrentVersion,p.customer_id CustomerId,p.representative_id RepresentativeId,v.payment_terms PaymentTerms,v.freight,v.currency,coalesce(c.status, 'ACTIVE') CustomerStatus,coalesce(c.type, 'CUSTOMER') CustomerType from agro360.sales_proposals p left join agro360.crm_customers c on c.tenant_id=p.tenant_id and c.id=p.customer_id join agro360.sales_proposal_versions v on v.tenant_id=p.tenant_id and v.proposal_id=p.id and v.version_number=p.current_version where p.tenant_id=@TenantId and p.id=@Id and p.deleted_at is null for update of p", new { tenant.TenantId, Id = id }, t);
        if (proposal == default) throw new KeyNotFoundException("Proposta não encontrada.");
        // The proposal lock serializes both competing balances and same-key retries. Re-read after it:
        // the first request may have committed while this transaction waited for the lock.
        previous = await c.QuerySingleOrDefaultAsync<(Guid OrderId, string RequestHash, string Currency, decimal Total, string OrderNumber)>("select x.order_id OrderId,x.request_hash RequestHash,o.currency,o.total_amount Total,o.order_number OrderNumber from agro360.sales_proposal_conversions x join agro360.sales_orders o on o.tenant_id=x.tenant_id and o.id=x.order_id where x.tenant_id=@TenantId and x.idempotency_key=@IdempotencyKey", new { tenant.TenantId, command.IdempotencyKey }, t);
        if (previous != default) return previous.RequestHash == hash ? new(previous.OrderId, true, previous.Currency, previous.Total, previous.OrderNumber) : throw new ConflictException("Chave idempotente já utilizada com conteúdo diferente.");
        if (proposal.Status != "ACCEPTED" || proposal.CurrentVersion != command.Version) throw new DomainException("Somente a versão aceita atual pode ser convertida.", "sales.proposal_not_convertible");
        if (string.Equals(proposal.CustomerType, "PROSPECT", StringComparison.OrdinalIgnoreCase)) throw new DomainException("Prospect deve ser qualificado como cliente antes de gerar pedido.", "sales.customer_is_prospect");
        CommercialRules.CustomerCanOrder(proposal.CustomerStatus, false);
        var requested = command.Items.GroupBy(x => x.ProposalItemId).ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (requested.Count != command.Items.Count) throw new DomainException("Item repetido na conversão.", "sales.proposal_conversion_duplicate_item");
        var items = (await c.QueryAsync<ConversionItem>("select i.id,i.product_id ProductId,i.unit,i.quantity,i.unit_price UnitPrice,i.discount_percentage DiscountPercentage,i.total_amount TotalAmount,i.price_table_id PriceTableId,i.pricing_snapshot::text PricingSnapshot,coalesce((select sum(ci.quantity) from agro360.sales_proposal_conversion_items ci where ci.tenant_id=i.tenant_id and ci.proposal_item_id=i.id),0) Converted,coalesce((select sum(oi.total_amount) from agro360.sales_proposal_conversion_items ci join agro360.sales_order_items oi on oi.tenant_id=ci.tenant_id and oi.id=ci.order_item_id where ci.tenant_id=i.tenant_id and ci.proposal_item_id=i.id),0) ConvertedAmount from agro360.sales_proposal_items i where i.tenant_id=@TenantId and i.proposal_id=@Id and i.version_number=@Version for update", new { tenant.TenantId, Id = id, Version = command.Version }, t)).ToDictionary(x => x.Id);
        foreach (var pair in requested) if (!items.TryGetValue(pair.Key, out var item) || pair.Value <= 0 || item.Converted + pair.Value > item.Quantity) throw new DomainException("Quantidade excede o saldo aceito.", "sales.proposal_conversion_balance");
        var hasPriorConversion = await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.sales_proposal_conversions where tenant_id=@TenantId and proposal_id=@Id and version_number=@Version)", new { tenant.TenantId, Id = id, Version = command.Version }, t);
        var appliedFreight = hasPriorConversion ? 0m : proposal.Freight;
        var allocations = requested.ToDictionary(x => x.Key, x => CommercialRules.ProposalConversionAmount(items[x.Key].Quantity, items[x.Key].TotalAmount, items[x.Key].Converted, items[x.Key].ConvertedAmount, x.Value));
        var orderId = Guid.CreateVersion7(); var total = allocations.Values.Sum() + appliedFreight;
        var orderNumber = await c.ExecuteScalarAsync<string>("select 'PED-'||nextval('agro360.sales_order_number_seq')", null, t);
        await c.ExecuteAsync("insert into agro360.sales_orders(id,tenant_id,customer_id,representative_id,order_number,status,total_amount,freight,payment_terms,currency,created_by,updated_by) values(@OrderId,@TenantId,@CustomerId,@RepresentativeId,@OrderNumber,'DRAFT',@Total,@Freight,@PaymentTerms,@Currency,@UserId,@UserId)", new { OrderId = orderId, tenant.TenantId, proposal.CustomerId, proposal.RepresentativeId, OrderNumber = orderNumber, Total = total, Freight = appliedFreight, proposal.PaymentTerms, proposal.Currency, tenant.UserId }, t);
        var conversionId = Guid.CreateVersion7(); await c.ExecuteAsync("insert into agro360.sales_proposal_conversions(id,tenant_id,proposal_id,version_number,order_id,idempotency_key,request_hash,created_by) values(@ConversionId,@TenantId,@Id,@Version,@OrderId,@IdempotencyKey,@Hash,@UserId)", new { ConversionId = conversionId, tenant.TenantId, Id = id, Version = command.Version, OrderId = orderId, command.IdempotencyKey, Hash = hash, tenant.UserId }, t);
        foreach (var pair in requested) { var item = items[pair.Key]; var orderItemId = Guid.CreateVersion7(); var lineTotal = allocations[pair.Key]; await c.ExecuteAsync("insert into agro360.sales_order_items(id,tenant_id,order_id,product_id,quantity,unit,unit_price,discount_percentage,total_amount,price_table_id,base_unit_price,pricing_snapshot) values(@OrderItemId,@TenantId,@OrderId,@ProductId,@Quantity,@Unit,@UnitPrice,@DiscountPercentage,@Total,@PriceTableId,@UnitPrice,@PricingSnapshot::jsonb); insert into agro360.sales_proposal_conversion_items(tenant_id,conversion_id,proposal_item_id,order_item_id,quantity) values(@TenantId,@ConversionId,@ProposalItemId,@OrderItemId,@Quantity)", new { OrderItemId = orderItemId, tenant.TenantId, OrderId = orderId, item.ProductId, Quantity = pair.Value, item.Unit, item.UnitPrice, item.DiscountPercentage, Total = lineTotal, item.PriceTableId, item.PricingSnapshot, ConversionId = conversionId, ProposalItemId = pair.Key }, t); }
        return new(orderId, false, proposal.Currency, total, orderNumber);
    }, ct);

    private async Task EnsureProposalReferences(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction t, SalesProposalCommand command)
    {
        if (command.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow)) throw new DomainException("A validade não pode iniciar expirada.", "sales.proposal_validity_invalid");
        var valid = await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.crm_customers where tenant_id=@TenantId and id=@CustomerId and deleted_at is null) and (@OpportunityId is null or exists(select 1 from agro360.sales_opportunities where tenant_id=@TenantId and id=@OpportunityId and customer_id=@CustomerId and deleted_at is null))", new { tenant.TenantId, command.CustomerId, command.OpportunityId }, t);
        if (!valid) throw new KeyNotFoundException("Cliente ou negociação incompatível.");
    }

    private async Task InsertProposalVersion(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction t, Guid id, long version, SalesProposalCommand command, long? supersedes)
    {
        var currency = command.Currency.Trim().ToUpperInvariant(); var lines = command.Items.Select(x => CommercialRules.ProposalLineTotal(x.Quantity, x.UnitPrice, x.DiscountPercentage)).ToArray(); var itemsTotal = lines.Sum(); var total = decimal.Round(itemsTotal + command.Freight, 2, MidpointRounding.AwayFromZero);
        await c.ExecuteAsync("insert into agro360.sales_proposal_versions(id,tenant_id,proposal_id,version_number,currency,valid_until,freight,payment_terms,items_total,total_amount,change_reason,supersedes_version,created_by) values(gen_random_uuid(),@TenantId,@Id,@Version,@Currency,@ValidUntil,@Freight,@PaymentTerms,@ItemsTotal,@Total,@ChangeReason,@Supersedes,@UserId)", new { tenant.TenantId, Id = id, Version = version, Currency = currency, command.ValidUntil, command.Freight, command.PaymentTerms, ItemsTotal = itemsTotal, Total = total, command.ChangeReason, Supersedes = supersedes, tenant.UserId }, t);
        for (var i = 0; i < command.Items.Count; i++) { var item = command.Items[i]; var snapshot = JsonSerializer.Serialize(new { item.PriceTableId, item.UnitPrice, item.DiscountPercentage, currency, rounding = "line-half-away-from-zero" }); await c.ExecuteAsync("insert into agro360.sales_proposal_items(id,tenant_id,proposal_id,version_number,product_id,unit,quantity,unit_price,discount_percentage,total_amount,price_table_id,pricing_snapshot) values(gen_random_uuid(),@TenantId,@Id,@Version,@ProductId,@Unit,@Quantity,@UnitPrice,@DiscountPercentage,@Total,@PriceTableId,@Snapshot::jsonb)", new { tenant.TenantId, Id = id, Version = version, item.ProductId, Unit = item.Unit.Trim().ToUpperInvariant(), item.Quantity, item.UnitPrice, item.DiscountPercentage, Total = lines[i], item.PriceTableId, Snapshot = snapshot }, t); }
    }

    public Task<CommercialDashboard> DashboardAsync(CancellationToken ct) => db.InTenantTransactionAsync<CommercialDashboard>(async (c, t) =>
    {
        var sql = """
            select count(*) filter(where status='ACTIVE') activecustomers, count(*) filter(where status='BLOCKED') blockedcustomers from agro360.crm_customers where tenant_id=@TenantId and deleted_at is null;
            select count(*) from agro360.sales_contracts where tenant_id=@TenantId and status='ACTIVE' and deleted_at is null;
            select coalesce(sum(estimated_value),0) from agro360.sales_opportunities where tenant_id=@TenantId and stage not in('WON','LOST') and deleted_at is null;
            select coalesce(sum(total_amount),0) from agro360.sales_orders where tenant_id=@TenantId and status not in('CANCELLED','DELIVERED') and deleted_at is null;
            select coalesce(sum(amount) filter(where status='EXPECTED'),0), coalesce(sum(amount) filter(where status='PAID'),0) from agro360.sales_commissions where tenant_id=@TenantId and deleted_at is null;
            select coalesce(sum(amount),0) from agro360.sales_split_entries where tenant_id=@TenantId and status='EXPECTED';
            select coalesce(currency, 'BRL') Currency, coalesce(sum(total_amount), 0) OrdersTotal from agro360.sales_orders where tenant_id=@TenantId and status not in('CANCELLED','DELIVERED') and deleted_at is null group by coalesce(currency, 'BRL');
            """;
        using var m = await c.QueryMultipleAsync(sql, new { tenant.TenantId }, t);
        var customers = await m.ReadSingleAsync<(int ActiveCustomers, int BlockedCustomers)>();
        var contracts = await m.ReadSingleAsync<int>();
        var pipeline = await m.ReadSingleAsync<decimal>();
        var forecast = await m.ReadSingleAsync<decimal>();
        var commissions = await m.ReadSingleAsync<(decimal Expected, decimal Paid)>();
        var splits = await m.ReadSingleAsync<decimal>();
        var currencyRows = (await m.ReadAsync<(string Currency, decimal OrdersTotal)>()).ToArray();

        var opp = (await ListAsync("opportunities", null, null, 1, 6, ct)).Items;
        var orders = (await ListAsync("orders", null, null, 1, 6, ct)).Items;

        var currencyTotals = currencyRows.Select(cr => new SalesCurrencyTotal(
            cr.Currency,
            cr.Currency == "BRL" ? pipeline : 0m,
            cr.Currency == "BRL" ? forecast : 0m,
            cr.OrdersTotal)).ToArray();
        if (currencyTotals.Length == 0)
        {
            currencyTotals = [new SalesCurrencyTotal("BRL", pipeline, forecast, 0m)];
        }

        return new(customers.ActiveCustomers, customers.BlockedCustomers, contracts, pipeline, forecast, commissions.Expected, commissions.Paid, splits, opp, orders, currencyTotals);
    }, ct);

    private static string Table(string resource) => Resources.TryGetValue(resource, out var table) ? table : throw new KeyNotFoundException("Recurso comercial não encontrado.");
    private sealed class ProposalHeader { public Guid ProposalId { get; init; } public string Number { get; init; } = ""; public string Status { get; init; } = ""; public long CurrentVersion { get; init; } public long? AcceptedVersion { get; init; } public long Version { get; init; } public Guid CustomerId { get; init; } public string CustomerName { get; init; } = ""; public Guid? OpportunityId { get; init; } public Guid? RepresentativeId { get; init; } public string Currency { get; init; } = "BRL"; public DateOnly ValidUntil { get; init; } public decimal Freight { get; init; } public decimal ItemsTotal { get; init; } public decimal Total { get; init; } public string PaymentTerms { get; init; } = ""; public string? ChangeReason { get; init; } }
    private sealed class ConversionItem { public Guid Id { get; init; } public Guid ProductId { get; init; } public string Unit { get; init; } = ""; public decimal Quantity { get; init; } public decimal UnitPrice { get; init; } public decimal DiscountPercentage { get; init; } public decimal TotalAmount { get; init; } public Guid? PriceTableId { get; init; } public string PricingSnapshot { get; init; } = "{}"; public decimal Converted { get; init; } public decimal ConvertedAmount { get; init; } }
    private sealed class SalesPricePolicyLookup { public Guid PriceTableId { get; init; } public Guid ProductId { get; init; } public string Unit { get; init; } = string.Empty; public decimal BasePrice { get; init; } public decimal MaximumDiscount { get; init; } public bool IsDefault { get; init; } public DateOnly ValidFrom { get; init; } public DateTimeOffset UpdatedAt { get; init; } }
    private sealed class OrderHeaderDto
    {
        public Guid Id { get; init; }
        public string OrderNumber { get; init; } = "";
        public Guid CustomerId { get; init; }
        public string CustomerName { get; init; } = "";
        public string Status { get; init; } = "";
        public string Currency { get; init; } = "BRL";
        public decimal TotalAmount { get; init; }
        public decimal Freight { get; init; }
        public string? PaymentTerms { get; init; }
        public DateOnly? ExpectedDelivery { get; init; }
        public string? Notes { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? UpdatedAt { get; init; }
        public Guid? ProposalId { get; init; }
        public string? ProposalNumber { get; init; }
        public long? ProposalVersion { get; init; }
    }
}
