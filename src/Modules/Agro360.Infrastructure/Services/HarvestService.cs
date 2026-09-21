using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.Agriculture;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed class HarvestService(
    DatabaseExecutor database,
    ITenantContext tenant,
    ILogger<HarvestService> logger,
    IOperationalInspectionTrigger inspectionTrigger) : IHarvestService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly string[] AllowedAllocationDestinations =
        ["AVAILABLE", "QUARANTINE", "RECLASSIFICATION", "REPROCESSING", "RETURN_TO_ORIGIN", "LOSS", "DISPOSAL"];
    private static readonly string[] AllowedOperationKinds = ["PLAN", "HARVEST", "RECEIPT"];
    private static readonly Action<ILogger, string, Exception?> LogInvalidClosingSnapshot =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(36021, "InvalidHarvestClosingSnapshot"),
            "Não foi possível ler a coleção {SnapshotKind} do fechamento da safra; o conteúdo persistido foi omitido do log.");

    private static string Hash<T>(T command) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command)))).ToLowerInvariant();
    private static string Required(string value, string name, int max = 160) => Guard.Required(value, name, max);

    public Task<HarvestOperationDto> CreatePlanAsync(CreateHarvestPlanCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (db, tx) =>
        {
            var key = Required(command.IdempotencyKey, nameof(command.IdempotencyKey)); var hash = Hash(command);
            var replay = await Replay(db, tx, "harvest_plans", key, hash, "PLAN", cancellationToken); if (replay is not null) return replay;
            if (command.PlannedEnd < command.PlannedStart) throw new DomainException("O fim previsto deve ser igual ou posterior ao início.", "harvest.invalid_period");
            var area = Guard.Positive(command.PlannedAreaHa, nameof(command.PlannedAreaHa)); var qty = Guard.Positive(command.EstimatedQuantity, nameof(command.EstimatedQuantity));
            var unit = Required(command.Unit, nameof(command.Unit), 16).ToLowerInvariant();
            var refs = await db.QuerySingleOrDefaultAsync<PlanReferences>(new CommandDefinition("""
                select s.farm_id FarmId,s.status SeasonStatus,f.farm_id FieldFarmId,f.area_ha FieldArea,
                       p.base_unit ProductUnit,w.farm_id WarehouseFarmId
                from agro360.agriculture_seasons s
                join agro360.geo_fields f on f.tenant_id=s.tenant_id and f.id=@FieldId and f.deleted_at is null
                join agro360.inventory_products p on p.tenant_id=s.tenant_id and p.id=@ProductId and p.deleted_at is null
                join agro360.inventory_warehouses w on w.tenant_id=s.tenant_id and w.id=@WarehouseId and w.deleted_at is null
                where s.tenant_id=@TenantId and s.id=@SeasonId and s.deleted_at is null;
                """, new { tenant.TenantId, command.SeasonId, command.FieldId, command.ProductId, WarehouseId=command.DestinationWarehouseId }, tx, cancellationToken:cancellationToken));
            if (refs is null || refs.FarmId != command.FarmId || refs.FieldFarmId != command.FarmId || refs.WarehouseFarmId != command.FarmId)
                throw new DomainException("Safra, talhão, propriedade e destino devem pertencer à mesma propriedade do cliente.", "harvest.incompatible_references");
            if (refs.SeasonStatus is 3 or 4 or 5) throw new ConflictException("A safra está encerrada para novos planejamentos.", "season.not_open");
            if (area > refs.FieldArea) throw new DomainException("A área planejada excede a área física do talhão.", "harvest.area_exceeds_field");
            if (!string.Equals(unit, refs.ProductUnit, StringComparison.OrdinalIgnoreCase)) throw new DomainException("Use a unidade base configurada para o produto.", "harvest.unit_mismatch");
            var id=Guid.CreateVersion7();
            await db.ExecuteAsync(new CommandDefinition("""
                insert into agro360.harvest_plans(id,tenant_id,farm_id,season_id,field_id,product_id,destination_warehouse_id,cost_center_id,responsible_id,
                planned_start,planned_end,planned_area_ha,estimated_quantity,unit,notes,idempotency_key,request_hash,created_by)
                values(@Id,@TenantId,@FarmId,@SeasonId,@FieldId,@ProductId,@WarehouseId,@CostCenterId,@ResponsibleId,@PlannedStart,@PlannedEnd,@Area,@Quantity,@Unit,@Notes,@Key,@Hash,@UserId);
                """, new { Id=id,tenant.TenantId,command.FarmId,command.SeasonId,command.FieldId,command.ProductId,WarehouseId=command.DestinationWarehouseId,command.CostCenterId,command.ResponsibleId,command.PlannedStart,command.PlannedEnd,Area=area,Quantity=qty,Unit=unit,command.Notes,Key=key,Hash=hash,UserId=tenant.UserId },tx,cancellationToken:cancellationToken));
            await Audit(db,tx,"create","HarvestPlan",id,cancellationToken);
            return new HarvestOperationDto(id,"PLAN","PLANNED",qty,unit,qty,command.PlannedStart.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc),"Planejamento",1);
        },cancellationToken);

    public Task<HarvestOperationDto> RegisterAsync(RegisterHarvestRecordCommand command, CancellationToken cancellationToken) => database.InTenantTransactionAsync(async(db,tx)=>
    {
        var key=Required(command.IdempotencyKey,nameof(command.IdempotencyKey)); var hash=Hash(command); var replay=await Replay(db,tx,"harvest_records",key,hash,"HARVEST",cancellationToken); if(replay is not null)return replay;
        var qty=Guard.Positive(command.HarvestedQuantity,nameof(command.HarvestedQuantity)); var unit=Required(command.Unit,nameof(command.Unit),16).ToLowerInvariant();
        var plan=await db.QuerySingleOrDefaultAsync<PlanRow>(new CommandDefinition("select estimated_quantity Quantity,unit,status,planned_area_ha Area from agro360.harvest_plans where tenant_id=@TenantId and id=@PlanId for update",new{tenant.TenantId,command.PlanId},tx,cancellationToken:cancellationToken)) ?? throw new NotFoundException("Planejamento de colheita",command.PlanId);
        if(plan.Status=="CANCELLED")throw new ConflictException("O planejamento foi cancelado.","harvest.plan_cancelled");
        if(!string.Equals(unit,plan.Unit,StringComparison.OrdinalIgnoreCase))throw new DomainException("A unidade difere do planejamento.","harvest.unit_mismatch");
        if(command.HarvestedAreaHa.HasValue && (command.HarvestedAreaHa.Value<=0 || command.HarvestedAreaHa.Value>plan.Area))throw new DomainException("A área efetivamente colhida deve ser positiva e não exceder o talhão planejado.","harvest.invalid_area");
        var id=Guid.CreateVersion7(); var reference=Required(command.CommercialReference,nameof(command.CommercialReference),100);
        await db.ExecuteAsync(new CommandDefinition("""
          insert into agro360.harvest_records(id,tenant_id,plan_id,operational_at,harvested_quantity,unit,harvested_area_ha,commercial_reference,notes,idempotency_key,request_hash,created_by)
          values(@Id,@TenantId,@PlanId,@At,@Quantity,@Unit,@Area,@Reference,@Notes,@Key,@Hash,@UserId);
          update agro360.harvest_plans set status='IN_PROGRESS',updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and id=@PlanId;
          """,new{Id=id,tenant.TenantId,command.PlanId,At=command.OperationalAt,Quantity=qty,Unit=unit,Area=command.HarvestedAreaHa,Reference=reference,command.Notes,Key=key,Hash=hash,UserId=tenant.UserId},tx,cancellationToken:cancellationToken));
        await Audit(db,tx,"record","HarvestRecord",id,cancellationToken);
        return new HarvestOperationDto(id,"HARVEST","AWAITING_RECEIPT",qty,unit,qty,command.OperationalAt,reference,1);
    },cancellationToken);

    public async Task<HarvestOperationDto> ReceiveAsync(ReceiveHarvestCommand command, CancellationToken cancellationToken)
    {
        Guid? createdReceiptId = null;
        Guid? receivedProductId = null;
        var result = await database.InTenantTransactionAsync(async (db, tx) =>
        {
            var key = Required(command.IdempotencyKey, nameof(command.IdempotencyKey));
            var hash = Hash(command);
            var replay = await Replay(db, tx, "production_receipts", key, hash, "RECEIPT", cancellationToken);
            if (replay is not null) return replay;
            var qty = Guard.Positive(command.ReceivedQuantity, nameof(command.ReceivedQuantity));
            var unit = Required(command.Unit, nameof(command.Unit), 16).ToLowerInvariant();
            var source = await db.QuerySingleOrDefaultAsync<ReceiptSource>(new CommandDefinition("""
                select r.harvested_quantity Quantity, r.unit, p.product_id ProductId, p.destination_warehouse_id WarehouseId, r.operational_at OperationalAt,
                coalesce((select sum(x.received_quantity) from agro360.production_receipts x where x.tenant_id=r.tenant_id and x.harvest_record_id=r.id),0) Received
                from agro360.harvest_records r join agro360.harvest_plans p on p.tenant_id=r.tenant_id and p.id=r.plan_id
                where r.tenant_id=@TenantId and r.id=@Id and r.status<>'CANCELLED' for update of r;
                """, new { tenant.TenantId, Id = command.HarvestRecordId }, tx, cancellationToken: cancellationToken))
                ?? throw new NotFoundException("Apontamento de colheita", command.HarvestRecordId);
            if (qty > source.Quantity - source.Received) throw new ConflictException("A quantidade excede o saldo ainda não recebido.", "harvest.receipt_exceeds_balance");
            if (command.WarehouseId != source.WarehouseId) throw new DomainException("O local difere do destino conferido no planejamento.", "harvest.warehouse_mismatch");
            if (!string.Equals(unit, source.Unit, StringComparison.OrdinalIgnoreCase)) throw new DomainException("A unidade difere da origem.", "harvest.unit_mismatch");
            decimal? net = null;
            if (command.GrossWeight.HasValue || command.TareWeight.HasValue)
            {
                if (unit is not ("kg" or "t" or "g" or "arroba")) throw new DomainException("Tara só é aceita para unidade de massa.", "harvest.tare_incompatible");
                if (!command.GrossWeight.HasValue || !command.TareWeight.HasValue || command.GrossWeight.Value < command.TareWeight.Value) throw new DomainException("O peso bruto deve ser maior ou igual à tara.", "harvest.invalid_weight");
                net = command.GrossWeight.Value - command.TareWeight.Value;
            }
            var id = Guid.CreateVersion7();
            var remaining = source.Quantity - source.Received - qty;
            var status = remaining == 0 ? "RECEIVED" : "PARTIALLY_RECEIVED";
            await db.ExecuteAsync(new CommandDefinition("""
                insert into agro360.production_receipts(id,tenant_id,harvest_record_id,warehouse_id,product_id,received_at,received_quantity,unit,gross_weight,tare_weight,net_weight,lot_number,entry_mode,divergence_reason,notes,idempotency_key,request_hash,created_by)
                values(@Id,@TenantId,@HarvestId,@WarehouseId,@ProductId,@At,@Quantity,@Unit,@Gross,@Tare,@Net,@Lot,@Mode,@Divergence,@Notes,@Key,@Hash,@UserId);
                update agro360.harvest_records set status=@Status,updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and id=@HarvestId;
                """, new { Id = id, tenant.TenantId, HarvestId = command.HarvestRecordId, command.WarehouseId, source.ProductId, At = command.ReceivedAt, Quantity = qty, Unit = unit, Gross = command.GrossWeight, Tare = command.TareWeight, Net = net, Lot = Required(command.LotNumber, nameof(command.LotNumber), 100), Mode = Required(command.EntryMode, nameof(command.EntryMode), 16).ToUpperInvariant(), Divergence = command.DivergenceReason, command.Notes, Key = key, Hash = hash, UserId = tenant.UserId, Status = status }, tx, cancellationToken: cancellationToken));
            await Audit(db, tx, "receive", "ProductionReceipt", id, cancellationToken);
            createdReceiptId = id;
            receivedProductId = source.ProductId;
            return new HarvestOperationDto(id, "RECEIPT", "AWAITING_INSPECTION", qty, unit, qty, command.ReceivedAt, command.LotNumber, 1);
        }, cancellationToken);

        if (createdReceiptId.HasValue)
        {
            await inspectionTrigger.TryStartFromOriginAsync(new OperationalInspectionEventRequest(
                ProcessCode: "HARVEST_RECEIPT",
                OriginType: "production_receipts",
                OriginId: createdReceiptId.Value,
                ProductId: receivedProductId,
                UnitId: command.WarehouseId,
                Notes: $"Recebimento de colheita lote {command.LotNumber}"), cancellationToken);
        }

        return result;
    }

    public Task<HarvestOperationDto> InspectAsync(CompleteHarvestInspectionCommand command,CancellationToken cancellationToken)=>database.InTenantTransactionAsync(async(db,tx)=>
    {
        var key=Required(command.IdempotencyKey,nameof(command.IdempotencyKey));var hash=Hash(command);
        var prior=await db.QuerySingleOrDefaultAsync<InspectionReplay>(new CommandDefinition("select inspection_id Id,request_hash Hash,created_at At from agro360.harvest_inspection_requests where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=key},tx,cancellationToken:cancellationToken));
        if(prior is not null){if(prior.Hash!=hash)throw new ConflictException("A chave idempotente já foi usada com outro conteúdo.","harvest.idempotency_conflict");return new HarvestOperationDto(prior.Id,"INSPECTION","REPLAYED",0,"-",0,prior.At,key,1);}
        var receipt=await db.QuerySingleOrDefaultAsync<ReceiptRow>(new CommandDefinition("select received_quantity Quantity,unit,quality_status Status,product_id ProductId,lot_number Reference from agro360.production_receipts where tenant_id=@TenantId and id=@Id for update",new{tenant.TenantId,Id=command.ReceiptId},tx,cancellationToken:cancellationToken))??throw new NotFoundException("Recebimento",command.ReceiptId);
        if(receipt.Status!="AWAITING_INSPECTION")throw new ConflictException("Este recebimento já possui decisão de qualidade.","harvest.inspection_completed");
        var spec=await db.QuerySingleOrDefaultAsync<SpecRow>(new CommandDefinition("select version,product_id ProductId,status from agro360.quality_specifications where tenant_id=@TenantId and id=@Id and deleted_at is null",new{tenant.TenantId,Id=command.SpecificationId},tx,cancellationToken:cancellationToken))??throw new NotFoundException("Especificação de qualidade",command.SpecificationId);
        if(spec.Status!="ACTIVE"||spec.ProductId!=receipt.ProductId)throw new DomainException("A especificação ativa deve pertencer ao produto recebido.","harvest.specification_incompatible");
        var parameters=(await db.QueryAsync<ParameterRow>(new CommandDefinition("select id,required,evidence_required EvidenceRequired,minimum_value Minimum,maximum_value Maximum,name from agro360.quality_specification_parameters where tenant_id=@TenantId and specification_id=@Id and deleted_at is null",new{tenant.TenantId,Id=command.SpecificationId},tx,cancellationToken:cancellationToken))).ToArray();
        foreach(var p in parameters.Where(x=>x.Required)){var result=command.Results.SingleOrDefault(x=>x.ParameterId==p.Id);if(result is null||(result.NumericValue is null&&string.IsNullOrWhiteSpace(result.TextValue))||(p.EvidenceRequired&&result.EvidenceDocumentId is null))throw new DomainException($"O resultado obrigatório '{p.Name}' e sua evidência configurada devem ser informados.","harvest.required_quality_result");}
        var outside=parameters.Any(p=>{var r=command.Results.SingleOrDefault(x=>x.ParameterId==p.Id);return r?.NumericValue is decimal v&&((p.Minimum.HasValue&&v<p.Minimum.Value)||(p.Maximum.HasValue&&v>p.Maximum.Value));});
        var decision=Required(command.Decision,nameof(command.Decision),30).ToUpperInvariant();if(decision is not("APPROVED" or "BLOCKED" or "QUARANTINE" or "REJECTED"))throw new DomainException("Decisão de qualidade inválida.","harvest.invalid_decision");
        if((outside||decision!="APPROVED")&&string.IsNullOrWhiteSpace(command.Reason))throw new DomainException("Reprovação, bloqueio, quarentena ou resultado fora da faixa exige motivo.","harvest.inspection_reason_required");
        if(outside&&decision=="APPROVED")throw new DomainException("Há resultado fora da faixa configurada; aplique o tratamento configurado.","harvest.quality_outside_range");
        var id=Guid.CreateVersion7();
        await db.ExecuteAsync(new CommandDefinition("""
          insert into agro360.quality_inspections(id,tenant_id,inspection_type,product_id,responsible_id,inspected_at,status,overall_result,decision,decision_reason,completed_at,completed_by,created_by,production_receipt_id,specification_id,specification_version)
          values(@Id,@TenantId,'HARVEST_RECEIPT',@ProductId,@UserId,now(),'COMPLETED',@Decision,@Decision,@Reason,now(),@UserId,@UserId,@ReceiptId,@SpecId,@Version);
          update agro360.production_receipts set quality_status=@Decision,accepted_quantity=case when @Decision='APPROVED' then received_quantity else 0 end,updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and id=@ReceiptId;
          insert into agro360.harvest_inspection_requests(tenant_id,idempotency_key,request_hash,inspection_id,created_by) values(@TenantId,@Key,@Hash,@Id,@UserId);
          """,new{Id=id,tenant.TenantId,receipt.ProductId,UserId=tenant.UserId,Decision=decision,Reason=command.Reason,ReceiptId=command.ReceiptId,SpecId=command.SpecificationId,Version=spec.Version,Key=key,Hash=hash},tx,cancellationToken:cancellationToken));
        foreach(var p in parameters){var r=command.Results.SingleOrDefault(x=>x.ParameterId==p.Id);var ip=Guid.CreateVersion7();await db.ExecuteAsync(new CommandDefinition("insert into agro360.quality_inspection_parameters(id,tenant_id,inspection_id,specification_parameter_id,name,required,critical,created_by) values(@Id,@TenantId,@InspectionId,@ParameterId,@Name,@Required,false,@UserId);",new{Id=ip,tenant.TenantId,InspectionId=id,ParameterId=p.Id,p.Name,p.Required,UserId=tenant.UserId},tx,cancellationToken:cancellationToken));if(r is not null)await db.ExecuteAsync(new CommandDefinition("insert into agro360.quality_inspection_results(id,tenant_id,inspection_parameter_id,numeric_value,text_value,conforming,evidence_document_id,created_by) values(@Id,@TenantId,@ParameterId,@Numeric,@Text,@Conforming,@Evidence,@UserId);",new{Id=Guid.CreateVersion7(),tenant.TenantId,ParameterId=ip,Numeric=r.NumericValue,Text=r.TextValue,Conforming=!(r.NumericValue is decimal v&&((p.Minimum.HasValue&&v<p.Minimum.Value)||(p.Maximum.HasValue&&v>p.Maximum.Value))),Evidence=r.EvidenceDocumentId,UserId=tenant.UserId},tx,cancellationToken:cancellationToken));}
        await Audit(db,tx,"inspect","HarvestInspection",id,cancellationToken);
        return new HarvestOperationDto(id,"INSPECTION",decision,receipt.Quantity,receipt.Unit,receipt.Quantity,DateTimeOffset.UtcNow,receipt.Reference,1);
    },cancellationToken);

    public Task<HarvestOperationDto> AllocateAsync(AllocateHarvestMaterialCommand command,CancellationToken cancellationToken)=>database.InTenantTransactionAsync(async(db,tx)=>
    {
        var key=Required(command.IdempotencyKey,nameof(command.IdempotencyKey));var hash=Hash(command);var replay=await Replay(db,tx,"harvest_material_allocations",key,hash,"ALLOCATION",cancellationToken);if(replay is not null)return replay;
        var qty=Guard.Positive(command.Quantity,nameof(command.Quantity));var destination=Required(command.Destination,nameof(command.Destination),24).ToUpperInvariant();var reason=Required(command.Reason,nameof(command.Reason),1000);
        var receipt=await db.QuerySingleOrDefaultAsync<AllocationSource>(new CommandDefinition("""
          select r.received_quantity Quantity,r.accepted_quantity Accepted,r.unit,r.quality_status Status,r.product_id ProductId,r.warehouse_id WarehouseId,r.lot_number Reference,
          coalesce((select sum(a.quantity) from agro360.harvest_material_allocations a where a.tenant_id=r.tenant_id and a.receipt_id=r.id),0) Allocated,
          coalesce((select sum(a.quantity) from agro360.harvest_material_allocations a where a.tenant_id=r.tenant_id and a.receipt_id=r.id and a.destination='AVAILABLE'),0) AvailableAllocated
          from agro360.production_receipts r where r.tenant_id=@TenantId and r.id=@Id for update;
          """,new{tenant.TenantId,Id=command.ReceiptId},tx,cancellationToken:cancellationToken))??throw new NotFoundException("Recebimento",command.ReceiptId);
        if(qty>receipt.Quantity-receipt.Allocated)throw new ConflictException("A destinação excede o saldo do recebimento.","harvest.allocation_exceeds_balance");
        if(destination=="AVAILABLE"&&(receipt.Accepted<=0||qty>receipt.Accepted-receipt.AvailableAllocated))throw new ConflictException("Somente o saldo efetivamente aprovado pode entrar no estoque disponível.","harvest.quality_blocks_stock");
        if (!AllowedAllocationDestinations.Contains(destination, StringComparer.Ordinal)) throw new DomainException("Destinação inválida.","harvest.invalid_destination");
        Guid? movement=null;if(destination=="AVAILABLE"){movement=Guid.CreateVersion7();var balance=await db.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition("select id,available,average_cost AverageCost,version from agro360.inventory_stock_balances where tenant_id=@TenantId and warehouse_id=@WarehouseId and product_id=@ProductId for update",new{tenant.TenantId,receipt.WarehouseId,receipt.ProductId},tx,cancellationToken:cancellationToken));var after=(balance?.Available??0)+qty;var version=(balance?.Version??0)+1;await db.ExecuteAsync(new CommandDefinition("""
          insert into agro360.inventory_stock_balances(id,tenant_id,warehouse_id,product_id,unit,available,reserved,minimum,average_cost,version)
          values(@BalanceId,@TenantId,@WarehouseId,@ProductId,@Unit,@After,0,0,0,@Version)
          on conflict(tenant_id,warehouse_id,product_id) do update set available=@After,updated_at=now(),version=@Version;
          insert into agro360.inventory_stock_movements(id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,lot_number,reference_type,reference_id,notes,idempotency_key,balance_after,average_cost_after,balance_version,occurred_at,created_by)
          values(@MovementId,@TenantId,@WarehouseId,@ProductId,'PRODUCTION',@Quantity,@Unit,@Lot,'HARVEST_RECEIPT',@ReceiptId,@Reason,@MovementKey,@After,@AverageCost,@Version,now(),@UserId);
          """,new{BalanceId=balance?.Id??Guid.CreateVersion7(),tenant.TenantId,receipt.WarehouseId,receipt.ProductId,receipt.Unit,After=after,Version=version,MovementId=movement,Quantity=qty,Lot=receipt.Reference,ReceiptId=command.ReceiptId,Reason=reason,MovementKey=$"harvest:{key}",AverageCost=balance?.AverageCost??0,UserId=tenant.UserId},tx,cancellationToken:cancellationToken));}
        var id=Guid.CreateVersion7();var remaining=receipt.Quantity-receipt.Allocated-qty;
        await db.ExecuteAsync(new CommandDefinition("insert into agro360.harvest_material_allocations(id,tenant_id,receipt_id,quantity,destination,reason,stock_movement_id,idempotency_key,request_hash,created_by) values(@Id,@TenantId,@ReceiptId,@Quantity,@Destination,@Reason,@Movement,@Key,@Hash,@UserId); update agro360.production_receipts set quality_status=case when @Remaining=0 then 'ALLOCATED' else 'PARTIALLY_ALLOCATED' end,updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and id=@ReceiptId;",new{Id=id,tenant.TenantId,ReceiptId=command.ReceiptId,Quantity=qty,Destination=destination,Reason=reason,Movement=movement,Key=key,Hash=hash,UserId=tenant.UserId,Remaining=remaining},tx,cancellationToken:cancellationToken));
        await Audit(db,tx,"allocate","HarvestMaterialAllocation",id,cancellationToken);
        return new HarvestOperationDto(id,"ALLOCATION",destination,qty,receipt.Unit,remaining,DateTimeOffset.UtcNow,receipt.Reference,1);
    },cancellationToken);

    public Task<HarvestDashboardDto> DashboardAsync(Guid? seasonId,Guid? fieldId,CancellationToken cancellationToken)=>database.InTenantTransactionAsync(async(db,tx)=>
    {
        var totals=await db.QuerySingleAsync<DashboardRow>(new CommandDefinition("""
          select coalesce(sum(p.estimated_quantity),0) Planned,
           coalesce((select sum(r.harvested_quantity) from agro360.harvest_records r join agro360.harvest_plans x on x.tenant_id=r.tenant_id and x.id=r.plan_id where r.tenant_id=@TenantId and r.status<>'CANCELLED' and (@SeasonId is null or x.season_id=@SeasonId) and (@FieldId is null or x.field_id=@FieldId)),0) Harvested,
           coalesce((select sum(q.received_quantity) from agro360.production_receipts q join agro360.harvest_records r on r.tenant_id=q.tenant_id and r.id=q.harvest_record_id join agro360.harvest_plans x on x.tenant_id=r.tenant_id and x.id=r.plan_id where q.tenant_id=@TenantId and (@SeasonId is null or x.season_id=@SeasonId) and (@FieldId is null or x.field_id=@FieldId)),0) Received,
           coalesce((select sum(q.received_quantity) from agro360.production_receipts q join agro360.harvest_records r on r.tenant_id=q.tenant_id and r.id=q.harvest_record_id join agro360.harvest_plans x on x.tenant_id=r.tenant_id and x.id=r.plan_id where q.tenant_id=@TenantId and q.quality_status='AWAITING_INSPECTION' and (@SeasonId is null or x.season_id=@SeasonId) and (@FieldId is null or x.field_id=@FieldId)),0) AwaitingQuality,
           coalesce((select sum(q.accepted_quantity) from agro360.production_receipts q join agro360.harvest_records r on r.tenant_id=q.tenant_id and r.id=q.harvest_record_id join agro360.harvest_plans x on x.tenant_id=r.tenant_id and x.id=r.plan_id where q.tenant_id=@TenantId and (@SeasonId is null or x.season_id=@SeasonId) and (@FieldId is null or x.field_id=@FieldId)),0) Approved,
           coalesce((select sum(a.quantity) from agro360.harvest_material_allocations a join agro360.production_receipts q on q.tenant_id=a.tenant_id and q.id=a.receipt_id join agro360.harvest_records r on r.tenant_id=q.tenant_id and r.id=q.harvest_record_id join agro360.harvest_plans x on x.tenant_id=r.tenant_id and x.id=r.plan_id where a.tenant_id=@TenantId and a.destination in('LOSS','DISPOSAL') and (@SeasonId is null or x.season_id=@SeasonId) and (@FieldId is null or x.field_id=@FieldId)),0) Loss,
           coalesce((select sum(c.amount) from agro360.cost_entries c where c.tenant_id=@TenantId and (@SeasonId is null or c.season_id=@SeasonId) and (@FieldId is null or c.field_id=@FieldId)),0) Costs,
           coalesce(sum(p.planned_area_ha),0) Area,max(p.unit) Unit,count(distinct lower(p.unit)) UnitCount
          from agro360.harvest_plans p where p.tenant_id=@TenantId and p.status<>'CANCELLED' and (@SeasonId is null or p.season_id=@SeasonId) and (@FieldId is null or p.field_id=@FieldId);
          """,new{tenant.TenantId,SeasonId=seasonId,FieldId=fieldId},tx,cancellationToken:cancellationToken));
        if (totals.UnitCount > 1) throw new DomainException("Selecione uma safra ou um talhão com uma única unidade para consolidar quantidades.", "harvest.incompatible_dashboard_units");
        var recent=await ListInternal(db,tx,null,seasonId,cancellationToken);return new HarvestDashboardDto(totals.Planned,totals.Harvested,totals.Received,Math.Max(0,totals.Harvested-totals.Received),totals.AwaitingQuality,totals.Approved,totals.Loss,totals.Costs,totals.Area>0?totals.Costs/totals.Area:null,totals.Approved>0?totals.Costs/totals.Approved:null,true,totals.Unit??"-",recent.Take(20).ToArray());
    },cancellationToken);

    public Task<HarvestTraceDto> TraceAsync(Guid receiptId,CancellationToken cancellationToken)=>database.InTenantTransactionAsync(async(db,tx)=>
    {var row=await db.QuerySingleOrDefaultAsync<TraceRow>(new CommandDefinition("select q.id ReceiptId,q.lot_number LotNumber,q.quality_status QualityStatus,p.farm_id FarmId,p.season_id SeasonId,p.field_id FieldId,r.id HarvestRecordId,q.received_quantity ReceivedQuantity,q.unit,coalesce(sum(a.quantity),0) AllocatedQuantity from agro360.production_receipts q join agro360.harvest_records r on r.tenant_id=q.tenant_id and r.id=q.harvest_record_id join agro360.harvest_plans p on p.tenant_id=r.tenant_id and p.id=r.plan_id left join agro360.harvest_material_allocations a on a.tenant_id=q.tenant_id and a.receipt_id=q.id where q.tenant_id=@TenantId and q.id=@Id group by q.id,p.id,r.id",new{tenant.TenantId,Id=receiptId},tx,cancellationToken:cancellationToken))??throw new NotFoundException("Recebimento",receiptId);var timeline=await ListInternal(db,tx,null,row.SeasonId,cancellationToken);return new HarvestTraceDto(row.ReceiptId,row.LotNumber,row.QualityStatus,row.FarmId,row.SeasonId,row.FieldId,row.HarvestRecordId,row.ReceivedQuantity,row.AllocatedQuantity,row.Unit,timeline.Where(x=>x.Id==receiptId||x.Reference==row.LotNumber).ToArray());},cancellationToken);
    public Task<SeasonClosingDto> GetClosingAsync(Guid seasonId, DateOnly cutoffDate, CancellationToken cancellationToken) => database.InTenantTransactionAsync(async (db, tx) =>
    {
        var scope = await ClosingScope(db, tx, seasonId, cutoffDate, cancellationToken);
        var indicators = await ClosingIndicators(db, tx, seasonId, cutoffDate, scope.Unit, cancellationToken);
        var runRow = await db.QuerySingleOrDefaultAsync<ClosingRunRow>(new CommandDefinition("select id,generated_at GeneratedAt,criteria_version CriteriaVersion,issues::text Issues from agro360.harvest_closing_runs where tenant_id=@TenantId and season_id=@SeasonId and cutoff_date=@Cutoff order by generated_at desc limit 1", new { tenant.TenantId, SeasonId=seasonId, Cutoff=cutoffDate }, tx, cancellationToken:cancellationToken));
        var versions = (await db.QueryAsync<ClosingVersionRow>(new CommandDefinition("select id,season_id SeasonId,version,state,cutoff_date CutoffDate,generated_at GeneratedAt,responsible_id ResponsibleId,supersedes_id SupersedesId,reason,notes,indicators::text Indicators,issues::text Issues from agro360.harvest_closing_versions where tenant_id=@TenantId and season_id=@SeasonId order by version desc",new{tenant.TenantId,SeasonId=seasonId},tx,cancellationToken:cancellationToken))).Select(MapVersion).ToArray();
        var latest=versions.FirstOrDefault();var lastClosed=versions.FirstOrDefault(x=>x.State=="CLOSED");
        var retroactive=lastClosed is not null && await db.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.harvest_records r join agro360.harvest_plans p on p.tenant_id=r.tenant_id and p.id=r.plan_id where r.tenant_id=@TenantId and p.season_id=@SeasonId and r.operational_at::date<=@Cutoff and r.created_at>@GeneratedAt)",new{tenant.TenantId,SeasonId=seasonId,Cutoff=lastClosed.CutoffDate,GeneratedAt=lastClosed.GeneratedAt},tx,cancellationToken:cancellationToken));
        return new SeasonClosingDto(scope,latest?.State??(runRow is null?"PREPARATION":"CHECKED"),indicators,runRow is null?null:new SeasonClosingRunDto(runRow.Id,runRow.GeneratedAt,runRow.CriteriaVersion,DeserializeIssues(runRow.Issues)),versions,retroactive);
    },cancellationToken);

    public Task<SeasonClosingRunDto> RunClosingChecksAsync(RunSeasonClosingCommand command,CancellationToken cancellationToken) => database.InTenantTransactionAsync<SeasonClosingRunDto>(async(db,tx)=>
    {
        var key=Required(command.IdempotencyKey,nameof(command.IdempotencyKey)); var prior=await db.QuerySingleOrDefaultAsync<ClosingRunRow>(new CommandDefinition("select id,generated_at GeneratedAt,criteria_version CriteriaVersion,issues::text Issues from agro360.harvest_closing_runs where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=key},tx,cancellationToken:cancellationToken));
        if(prior is not null)return new SeasonClosingRunDto(prior.Id,prior.GeneratedAt,prior.CriteriaVersion,DeserializeIssues(prior.Issues));
        await db.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))",new{LockKey=$"{tenant.TenantId}:harvest-closing-check:{command.SeasonId}"},tx,cancellationToken:cancellationToken));
        prior=await db.QuerySingleOrDefaultAsync<ClosingRunRow>(new CommandDefinition("select id,generated_at GeneratedAt,criteria_version CriteriaVersion,issues::text Issues from agro360.harvest_closing_runs where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=key},tx,cancellationToken:cancellationToken));if(prior is not null)return new SeasonClosingRunDto(prior.Id,prior.GeneratedAt,prior.CriteriaVersion,DeserializeIssues(prior.Issues));
        var scope=await ClosingScope(db,tx,command.SeasonId,command.CutoffDate,cancellationToken);var indicators=await ClosingIndicators(db,tx,command.SeasonId,command.CutoffDate,scope.Unit,cancellationToken);var issues=BuildIssues(indicators);var id=Guid.CreateVersion7();
        await db.ExecuteAsync(new CommandDefinition("insert into agro360.harvest_closing_runs(id,tenant_id,season_id,farm_id,cutoff_date,criteria_version,issues,idempotency_key,created_by) values(@Id,@TenantId,@SeasonId,@FarmId,@Cutoff,'1.0',cast(@Issues as jsonb),@Key,@UserId)",new{Id=id,tenant.TenantId,command.SeasonId,scope.FarmId,Cutoff=command.CutoffDate,Issues=JsonSerializer.Serialize(issues,SnapshotJsonOptions),Key=key,UserId=tenant.UserId},tx,cancellationToken:cancellationToken));await Audit(db,tx,"check","HarvestManagementClosing",id,cancellationToken);return new SeasonClosingRunDto(id,DateTimeOffset.UtcNow,"1.0",issues);
    },cancellationToken);

    public Task<SeasonClosingVersionDto> CreateClosingAsync(CreateSeasonClosingCommand command,CancellationToken cancellationToken)=>database.InTenantTransactionAsync<SeasonClosingVersionDto>(async(db,tx)=>
    {
        var key=Required(command.IdempotencyKey,nameof(command.IdempotencyKey));var prior=await db.QuerySingleOrDefaultAsync<ClosingVersionRow>(new CommandDefinition("select id,season_id SeasonId,version,state,cutoff_date CutoffDate,generated_at GeneratedAt,responsible_id ResponsibleId,supersedes_id SupersedesId,reason,notes,indicators::text Indicators,issues::text Issues from agro360.harvest_closing_versions where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=key},tx,cancellationToken:cancellationToken));if(prior is not null)return MapVersion(prior);
        await db.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))",new{LockKey=$"{tenant.TenantId}:harvest-closing:{command.SeasonId}"},tx,cancellationToken:cancellationToken));
        prior=await db.QuerySingleOrDefaultAsync<ClosingVersionRow>(new CommandDefinition("select id,season_id SeasonId,version,state,cutoff_date CutoffDate,generated_at GeneratedAt,responsible_id ResponsibleId,supersedes_id SupersedesId,reason,notes,indicators::text Indicators,issues::text Issues from agro360.harvest_closing_versions where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=key},tx,cancellationToken:cancellationToken));if(prior is not null)return MapVersion(prior);var scope=await ClosingScope(db,tx,command.SeasonId,command.CutoffDate,cancellationToken);var indicators=await ClosingIndicators(db,tx,command.SeasonId,command.CutoffDate,scope.Unit,cancellationToken);var issues=BuildIssues(indicators);
        var previous=await db.QuerySingleOrDefaultAsync<PreviousClosing>(new CommandDefinition("select id,version from agro360.harvest_closing_versions where tenant_id=@TenantId and season_id=@SeasonId order by version desc limit 1 for update",new{tenant.TenantId,command.SeasonId},tx,cancellationToken:cancellationToken));if(previous is not null&&string.IsNullOrWhiteSpace(command.RevisionReason))throw new DomainException("Informe o motivo da revisão para preservar a rastreabilidade.","closing.revision_reason_required");
        var id=Guid.CreateVersion7();var version=(previous?.Version??0)+1;var state=issues.Any(x=>x.Severity=="BLOCKER")?"BLOCKED":"CHECKED";await db.ExecuteAsync(new CommandDefinition("insert into agro360.harvest_closing_versions(id,tenant_id,season_id,farm_id,cutoff_date,version,state,responsible_id,supersedes_id,reason,notes,criteria,indicators,issues,idempotency_key,created_by) values(@Id,@TenantId,@SeasonId,@FarmId,@Cutoff,@Version,@State,@UserId,@Supersedes,@Reason,@Notes,cast(@Criteria as jsonb),cast(@Indicators as jsonb),cast(@Issues as jsonb),@Key,@UserId)",new{Id=id,tenant.TenantId,command.SeasonId,scope.FarmId,Cutoff=command.CutoffDate,Version=version,State=state,UserId=tenant.UserId,Supersedes=previous?.Id,Reason=command.RevisionReason,command.Notes,Criteria="{\"version\":\"1.0\",\"cutoffBasis\":\"operational_date\"}",Indicators=JsonSerializer.Serialize(indicators,SnapshotJsonOptions),Issues=JsonSerializer.Serialize(issues,SnapshotJsonOptions),Key=key},tx,cancellationToken:cancellationToken));await Audit(db,tx,"create","HarvestManagementClosing",id,cancellationToken);return new SeasonClosingVersionDto(id,version,state,command.CutoffDate,DateTimeOffset.UtcNow,tenant.UserId,previous?.Id,command.RevisionReason,command.Notes,indicators,issues);
    },cancellationToken);

    public Task<SeasonClosingVersionDto> CloseAsync(Guid closingId,ChangeSeasonClosingStateCommand command,CancellationToken cancellationToken)=>database.InTenantTransactionAsync<SeasonClosingVersionDto>(async(db,tx)=>
    {
        var row=await db.QuerySingleOrDefaultAsync<ClosingVersionRow>(new CommandDefinition("select id,season_id SeasonId,version,state,cutoff_date CutoffDate,generated_at GeneratedAt,responsible_id ResponsibleId,supersedes_id SupersedesId,reason,notes,indicators::text Indicators,issues::text Issues from agro360.harvest_closing_versions where tenant_id=@TenantId and id=@Id for update",new{tenant.TenantId,Id=closingId},tx,cancellationToken:cancellationToken))??throw new NotFoundException("Fechamento gerencial",closingId);if(row.Version!=command.Version)throw new ConflictException("A versão foi alterada; atualize a página.","closing.concurrent_change");if(row.State=="BLOCKED"||DeserializeIssues(row.Issues).Any(x=>x.Severity=="BLOCKER"))throw new ConflictException("Corrija os bloqueios na operação de origem e gere nova conferência.","closing.blocked");if(row.State=="CLOSED")return MapVersion(row);
        var scope=await ClosingScope(db,tx,row.SeasonId,row.CutoffDate,cancellationToken);var currentIndicators=await ClosingIndicators(db,tx,row.SeasonId,row.CutoffDate,scope.Unit,cancellationToken);var currentIssues=BuildIssues(currentIndicators);if(currentIssues.Any(x=>x.Severity=="BLOCKER"))throw new ConflictException("Novos bloqueios foram encontrados; corrija a origem e gere uma nova versão.","closing.blocked");if(!SnapshotMatches(DeserializeIndicators(row.Indicators),currentIndicators))throw new ConflictException("Os dados considerados mudaram desde a geração. Execute a conferência e gere uma nova versão.","closing.stale_snapshot");
        await db.ExecuteAsync(new CommandDefinition("update agro360.harvest_closing_versions set state='CLOSED',closed_at=now(),closed_by=@UserId,notes=coalesce(@Notes,notes),row_version=row_version+1 where tenant_id=@TenantId and id=@Id",new{tenant.TenantId,Id=closingId,UserId=tenant.UserId,command.Notes},tx,cancellationToken:cancellationToken));await Audit(db,tx,"close","HarvestManagementClosing",closingId,cancellationToken);return MapVersion(row with{State="CLOSED",Notes=command.Notes??row.Notes});
    },cancellationToken);

    public Task<SeasonClosingVersionDto> ReopenAsync(Guid closingId,ReopenSeasonClosingCommand command,CancellationToken cancellationToken)=>database.InTenantTransactionAsync<SeasonClosingVersionDto>(async(db,tx)=>
    {
        var reason=Required(command.Reason,nameof(command.Reason),1000);var key=Required(command.IdempotencyKey,nameof(command.IdempotencyKey));
        var replay=await db.QuerySingleOrDefaultAsync<ClosingVersionRow>(new CommandDefinition("select id,season_id SeasonId,version,state,cutoff_date CutoffDate,generated_at GeneratedAt,responsible_id ResponsibleId,supersedes_id SupersedesId,reason,notes,indicators::text Indicators,issues::text Issues from agro360.harvest_closing_versions where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=key},tx,cancellationToken:cancellationToken));if(replay is not null)return MapVersion(replay);
        var closed=await db.QuerySingleOrDefaultAsync<ClosingVersionRow>(new CommandDefinition("select id,season_id SeasonId,version,state,cutoff_date CutoffDate,generated_at GeneratedAt,responsible_id ResponsibleId,supersedes_id SupersedesId,reason,notes,indicators::text Indicators,issues::text Issues from agro360.harvest_closing_versions where tenant_id=@TenantId and id=@Id",new{tenant.TenantId,Id=closingId},tx,cancellationToken:cancellationToken))??throw new NotFoundException("Fechamento gerencial",closingId);
        if(closed.Version!=command.Version)throw new ConflictException("A versão foi alterada; atualize a página.","closing.concurrent_change");if(closed.State!="CLOSED")throw new ConflictException("Somente o fechamento vigente e concluído pode ser reaberto.","closing.not_closed");
        await db.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))",new{LockKey=$"{tenant.TenantId}:harvest-closing:{closed.SeasonId}"},tx,cancellationToken:cancellationToken));
        replay=await db.QuerySingleOrDefaultAsync<ClosingVersionRow>(new CommandDefinition("select id,season_id SeasonId,version,state,cutoff_date CutoffDate,generated_at GeneratedAt,responsible_id ResponsibleId,supersedes_id SupersedesId,reason,notes,indicators::text Indicators,issues::text Issues from agro360.harvest_closing_versions where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=key},tx,cancellationToken:cancellationToken));if(replay is not null)return MapVersion(replay);
        closed=await db.QuerySingleOrDefaultAsync<ClosingVersionRow>(new CommandDefinition("select id,season_id SeasonId,version,state,cutoff_date CutoffDate,generated_at GeneratedAt,responsible_id ResponsibleId,supersedes_id SupersedesId,reason,notes,indicators::text Indicators,issues::text Issues from agro360.harvest_closing_versions where tenant_id=@TenantId and id=@Id for update",new{tenant.TenantId,Id=closingId},tx,cancellationToken:cancellationToken))??throw new NotFoundException("Fechamento gerencial",closingId);if(closed.Version!=command.Version||closed.State!="CLOSED")throw new ConflictException("O fechamento mudou durante a reabertura. Atualize o histórico.","closing.concurrent_change");
        var latest=await db.QuerySingleAsync<PreviousClosing>(new CommandDefinition("select id,version from agro360.harvest_closing_versions where tenant_id=@TenantId and season_id=@SeasonId order by version desc limit 1 for update",new{tenant.TenantId,closed.SeasonId},tx,cancellationToken:cancellationToken));if(latest.Id!=closed.Id)throw new ConflictException("Há uma versão posterior. Atualize o histórico antes de reabrir.","closing.concurrent_change");
        var scope=await ClosingScope(db,tx,closed.SeasonId,closed.CutoffDate,cancellationToken);var indicators=await ClosingIndicators(db,tx,closed.SeasonId,closed.CutoffDate,scope.Unit,cancellationToken);var issues=BuildIssues(indicators);var id=Guid.CreateVersion7();var version=closed.Version+1;var state=issues.Any(x=>x.Severity=="BLOCKER")?"BLOCKED":"CHECKED";
        await db.ExecuteAsync(new CommandDefinition("insert into agro360.harvest_closing_versions(id,tenant_id,season_id,farm_id,cutoff_date,version,state,responsible_id,supersedes_id,reason,notes,criteria,indicators,issues,idempotency_key,created_by) values(@Id,@TenantId,@SeasonId,@FarmId,@Cutoff,@Version,@State,@UserId,@Supersedes,@Reason,@Notes,cast(@Criteria as jsonb),cast(@Indicators as jsonb),cast(@Issues as jsonb),@Key,@UserId)",new{Id=id,tenant.TenantId,SeasonId=closed.SeasonId,scope.FarmId,Cutoff=closed.CutoffDate,Version=version,State=state,UserId=tenant.UserId,Supersedes=closed.Id,Reason=reason,Notes=closed.Notes,Criteria="{\"version\":\"1.0\",\"cutoffBasis\":\"operational_date\",\"transition\":\"reopen\"}",Indicators=JsonSerializer.Serialize(indicators,SnapshotJsonOptions),Issues=JsonSerializer.Serialize(issues,SnapshotJsonOptions),Key=key},tx,cancellationToken:cancellationToken));await Audit(db,tx,"reopen","HarvestManagementClosing",id,cancellationToken);return new SeasonClosingVersionDto(id,version,state,closed.CutoffDate,DateTimeOffset.UtcNow,tenant.UserId,closed.Id,reason,closed.Notes,indicators,issues);
    },cancellationToken);

    public Task<IReadOnlyCollection<HarvestOperationDto>> ListAsync(string? kind,Guid? seasonId,CancellationToken cancellationToken)
    {
        var normalizedKind = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim().ToUpperInvariant();
        if (normalizedKind is not null && !AllowedOperationKinds.Contains(normalizedKind, StringComparer.Ordinal))
            throw new DomainException("Tipo de operação inválido.", "harvest.invalid_operation_kind");

        return database.InTenantTransactionAsync((db,tx)=>ListInternal(db,tx,normalizedKind,seasonId,cancellationToken),cancellationToken);
    }
    private async Task<IReadOnlyCollection<HarvestOperationDto>> ListInternal(System.Data.IDbConnection db,System.Data.IDbTransaction tx,string? kind,Guid? seasonId,CancellationToken ct)=>(await db.QueryAsync<HarvestOperationDto>(new CommandDefinition("""
      select id,'PLAN' Kind,status,estimated_quantity Quantity,unit,estimated_quantity RemainingQuantity,planned_start::timestamptz OccurredAt,'Planejamento' Reference,version from agro360.harvest_plans where tenant_id=@TenantId and (@SeasonId is null or season_id=@SeasonId) and (@Kind is null or @Kind='PLAN')
      union all select r.id,'HARVEST',r.status,r.harvested_quantity,r.unit,greatest(0,r.harvested_quantity-coalesce((select sum(q.received_quantity) from agro360.production_receipts q where q.tenant_id=r.tenant_id and q.harvest_record_id=r.id),0)),r.operational_at,r.commercial_reference,r.version from agro360.harvest_records r join agro360.harvest_plans p on p.tenant_id=r.tenant_id and p.id=r.plan_id where r.tenant_id=@TenantId and (@SeasonId is null or p.season_id=@SeasonId) and (@Kind is null or @Kind='HARVEST')
      union all select q.id,'RECEIPT',q.quality_status,q.received_quantity,q.unit,greatest(0,q.received_quantity-coalesce((select sum(a.quantity) from agro360.harvest_material_allocations a where a.tenant_id=q.tenant_id and a.receipt_id=q.id),0)),q.received_at,q.lot_number,q.version from agro360.production_receipts q join agro360.harvest_records r on r.tenant_id=q.tenant_id and r.id=q.harvest_record_id join agro360.harvest_plans p on p.tenant_id=r.tenant_id and p.id=r.plan_id where q.tenant_id=@TenantId and (@SeasonId is null or p.season_id=@SeasonId) and (@Kind is null or @Kind='RECEIPT') order by OccurredAt desc limit 200;
      """,new{tenant.TenantId,SeasonId=seasonId,Kind=kind?.ToUpperInvariant()},tx,cancellationToken:ct))).ToArray();
    private async Task<SeasonClosingScopeDto> ClosingScope(System.Data.IDbConnection db,System.Data.IDbTransaction tx,Guid seasonId,DateOnly cutoff,CancellationToken ct)
    {
        var row=await db.QuerySingleOrDefaultAsync<ClosingScopeRow>(new CommandDefinition("select s.id SeasonId,s.farm_id FarmId,s.name Season,f.name Farm,s.crop Crop,s.start_date StartsOn,s.end_date EndsOn,coalesce((select min(lower(p.unit)) from agro360.harvest_plans p where p.tenant_id=s.tenant_id and p.season_id=s.id and p.status<>'CANCELLED'),'-') Unit from agro360.agriculture_seasons s join agro360.geo_farms f on f.tenant_id=s.tenant_id and f.id=s.farm_id where s.tenant_id=@TenantId and s.id=@SeasonId and s.deleted_at is null",new{tenant.TenantId,SeasonId=seasonId},tx,cancellationToken:ct))??throw new NotFoundException("Safra",seasonId);if(cutoff<row.StartsOn)throw new DomainException("A data de corte não pode anteceder o início da safra.","closing.invalid_cutoff");return new(row.SeasonId,row.FarmId,row.Season,row.Farm,row.Crop,row.StartsOn,row.EndsOn,cutoff,row.Unit);
    }
    private async Task<IReadOnlyCollection<SeasonClosingIndicatorDto>> ClosingIndicators(System.Data.IDbConnection db,System.Data.IDbTransaction tx,Guid seasonId,DateOnly cutoff,string unit,CancellationToken ct)
    {
        var x=await db.QuerySingleAsync<ClosingTotals>(new CommandDefinition("""
        with plans as (select * from agro360.harvest_plans where tenant_id=@TenantId and season_id=@SeasonId and status<>'CANCELLED'),
        records as (select r.* from agro360.harvest_records r join plans p on p.id=r.plan_id where r.status<>'CANCELLED' and r.operational_at::date<=@Cutoff),
        receipts as (select q.* from agro360.production_receipts q join records r on r.id=q.harvest_record_id where q.received_at::date<=@Cutoff),
        allocations as (select a.* from agro360.harvest_material_allocations a join receipts q on q.id=a.receipt_id where a.created_at::date<=@Cutoff)
        select coalesce((select sum(planned_area_ha) from plans),0) RegisteredArea,
        coalesce((select sum(harvested_area_ha) from records),0) HarvestedArea,
        coalesce((select sum(harvested_quantity) from records),0) Harvested,
        coalesce((select sum(received_quantity) from receipts),0) Received,
        coalesce((select sum(received_quantity) from receipts where quality_status='AWAITING_INSPECTION'),0) AwaitingQuality,
        coalesce((select sum(accepted_quantity) from receipts),0) Approved,
        coalesce((select sum(quantity) from allocations),0) Allocated,
        coalesce((select sum(quantity) from allocations where destination='AVAILABLE'),0) MadeAvailable,
        coalesce((select sum(quantity) from allocations where destination in('REPROCESSING','RECLASSIFICATION')),0) InProcessing,
        coalesce((select sum(quantity) from allocations where destination in('LOSS','DISPOSAL')),0) Loss,
        coalesce((select sum(amount) from agro360.cost_entries where tenant_id=@TenantId and season_id=@SeasonId and occurred_on<=@Cutoff),0) Costs,
        coalesce((select count(distinct lower(unit)) from plans),0) UnitCount
        """,new{tenant.TenantId,SeasonId=seasonId,Cutoff=cutoff},tx,cancellationToken:ct));

        var stockLots = (await db.QueryAsync<StockLotClosingRow>(new CommandDefinition("""
            select coalesce(l.quantity, 0) Quantity, lower(l.unit) Unit
            from agro360.inventory_stock_lots l
            where l.tenant_id = @TenantId
              and l.lot_number in (
                select distinct q.lot_number
                from agro360.production_receipts q
                join agro360.harvest_records r on r.tenant_id = q.tenant_id and r.id = q.harvest_record_id
                join agro360.harvest_plans p on p.tenant_id = r.tenant_id and p.id = r.plan_id
                where p.tenant_id = @TenantId and p.season_id = @SeasonId and q.received_at::date <= @Cutoff
              )
            """, new { tenant.TenantId, SeasonId = seasonId, Cutoff = cutoff }, tx, cancellationToken: ct))).ToArray();

        decimal? currentStock = null;
        string stockAvailability = "UNAVAILABLE";
        string? stockExplanation = null;

        if (x.UnitCount > 1)
        {
            stockExplanation = "Há unidades físicas incompatíveis no escopo da safra; consolidação de estoque recusada.";
        }
        else if (stockLots.Length == 0 && x.MadeAvailable == 0)
        {
            currentStock = 0m;
            stockAvailability = "AVAILABLE";
            stockExplanation = "Nenhum lote com saldo remanescente em estoque.";
        }
        else if (stockLots.Length > 0)
        {
            var units = stockLots.Select(s => s.Unit).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToArray();
            if (units.Length > 1)
            {
                stockExplanation = "Unidades físicas heterogêneas nos lotes de estoque relacionados; consolidação recusada por integridade.";
            }
            else
            {
                currentStock = stockLots.Sum(s => s.Quantity);
                stockAvailability = "AVAILABLE";
                stockExplanation = "Saldo atual dos lotes rastreados originados dos recebimentos físicos desta safra.";
            }
        }
        else
        {
            stockExplanation = "Não há atribuição exata de todos os saldos atuais à safra.";
        }

        var revenueData = await db.QuerySingleOrDefaultAsync<RevenueClosingRow>(new CommandDefinition("""
            with season_receipts as (
                select distinct q.lot_number
                from agro360.production_receipts q
                join agro360.harvest_records r on r.tenant_id = q.tenant_id and r.id = q.harvest_record_id
                join agro360.harvest_plans p on p.tenant_id = r.tenant_id and p.id = r.plan_id
                where p.tenant_id = @TenantId and p.season_id = @SeasonId and q.received_at::date <= @Cutoff
            ),
            season_lots as (
                select l.id LotId
                from agro360.inventory_stock_lots l
                where l.tenant_id = @TenantId and l.lot_number in (select lot_number from season_receipts)
            ),
            shipment_data as (
                select si.id,
                       coalesce(sum(dai.accepted_quantity), 0) AcceptedQty,
                       coalesce((select sum(ret.quantity) from agro360.fulfillment_returns ret where ret.tenant_id = si.tenant_id and ret.shipment_item_id = si.id and ret.status in ('RELEASED','BLOCKED','DISPOSED')), 0) ReturnedQty,
                       oi.unit_price UnitPrice,
                       s.status ShipmentStatus
                from agro360.fulfillment_shipment_items si
                join agro360.fulfillment_shipments s on s.tenant_id = si.tenant_id and s.id = si.shipment_id and s.deleted_at is null
                join agro360.sales_order_items oi on oi.tenant_id = si.tenant_id and oi.id = si.order_item_id
                left join agro360.fulfillment_delivery_attempt_items dai on dai.tenant_id = si.tenant_id and dai.shipment_item_id = si.id
                left join agro360.fulfillment_delivery_attempts da on da.tenant_id = dai.tenant_id and da.id = dai.attempt_id and da.occurred_at::date <= @Cutoff
                where si.tenant_id = @TenantId
                  and si.stock_lot_id in (select LotId from season_lots)
                group by si.id, oi.unit_price, s.status
            )
            select coalesce(sum(greatest(0, AcceptedQty - ReturnedQty) * UnitPrice), 0) RecognizedRevenue,
                   count(*) TotalShipmentItems,
                   count(*) filter (where ShipmentStatus not in ('RECONCILED','CANCELLED') and AcceptedQty = 0) PendingShipmentItems
            from shipment_data;
            """, new { tenant.TenantId, SeasonId = seasonId, Cutoff = cutoff }, tx, cancellationToken: ct));

        decimal? recognizedRevenue = null;
        string revenueAvailability = "UNAVAILABLE";
        string? revenueExplanation = null;

        if (revenueData is null || revenueData.TotalShipmentItems == 0)
        {
            recognizedRevenue = 0m;
            revenueAvailability = "AVAILABLE";
            revenueExplanation = "Nenhuma remessa comercial expedida para os lotes desta safra até a data de corte.";
        }
        else if (revenueData.PendingShipmentItems > 0)
        {
            recognizedRevenue = null;
            revenueAvailability = "UNAVAILABLE";
            revenueExplanation = "Há remessas com entrega pendente de confirmação; receita reconhecida exige elos de entrega aceita concluídos.";
        }
        else
        {
            recognizedRevenue = revenueData.RecognizedRevenue;
            revenueAvailability = "AVAILABLE";
            revenueExplanation = "Receita reconhecida a partir de entregas formalmente aceitas dos lotes da safra, deduzidos retornos destinados.";
        }

        var unavailable=x.UnitCount>1;string availability=unavailable?"UNAVAILABLE":"AVAILABLE";string? explanation=unavailable?"Há unidades físicas incompatíveis no escopo; selecione/corrija a origem antes de consolidar.":null;SeasonClosingIndicatorDto Q(string code,string label,decimal value,string definition,string source)=>new(code,label,unavailable?null:value,unit,availability,definition,source,explanation);
        return [new SeasonClosingIndicatorDto(Code:"registered-area",Label:"Área cadastrada",Value:x.RegisteredArea,Unit:"ha",Availability:"AVAILABLE",Definition:"Soma da área planejada ativa da safra; exclui planos cancelados e não representa área executada.",SourceUrl:"/Harvest?kind=PLAN",Explanation:null),new SeasonClosingIndicatorDto(Code:"harvested-area",Label:"Área colhida",Value:x.HarvestedArea,Unit:"ha",Availability:"AVAILABLE",Definition:"Soma das áreas efetivamente informadas em apontamentos não cancelados cuja data operacional não ultrapassa o corte.",SourceUrl:"/Harvest?kind=HARVEST",Explanation:null),Q("harvested","Produção apontada",x.Harvested,"Apontamentos não cancelados por data operacional até o corte.","/Harvest?kind=HARVEST"),Q("received","Produção recebida",x.Received,"Recebimentos físicos por data operacional até o corte.","/Harvest?kind=RECEIPT"),Q("awaiting-quality","Aguardando inspeção",x.AwaitingQuality,"Recebido sem decisão de qualidade; é pendência operacional.","/Harvest?kind=RECEIPT"),Q("approved","Quantidade aprovada",x.Approved,"Quantidade aceita pela decisão de qualidade vigente.","/Harvest?kind=RECEIPT"),Q("allocated","Quantidade destinada",x.Allocated,"Destinações registradas até o corte, sem assumir equivalência com produto transformado.","/Harvest?kind=RECEIPT"),Q("made-available","Entrada disponibilizada",x.MadeAvailable,"Destinações AVAILABLE que criaram movimento; não é o estoque atual.","/Inventory"),Q("processing","Em processamento",x.InProcessing,"Saldo legitimamente destinado a reprocessamento ou reclassificação.","/Production"),Q("loss","Perdas",x.Loss,"Destinações físicas de perda ou descarte.","/Harvest?kind=RECEIPT"),new("appropriated-cost","Custos apropriados",x.Costs,"BRL","AVAILABLE","Lançamentos de custo vinculados diretamente à safra até o corte; compra e pagamento não são somados.","/Finance",null),new("unit-cost","Custo por unidade aprovada",x.Approved>0?decimal.Round(x.Costs/x.Approved,6,MidpointRounding.AwayFromZero):null,"BRL/"+unit,x.Approved>0?"PROVISIONAL":"UNAVAILABLE","Custos apropriados ÷ quantidade aprovada; arredondamento AwayFromZero em 6 casas.","/Finance",x.Approved>0?"Não inclui componentes sem vínculo confiável com a safra.":"Quantidade aprovada zero; divisão não calculada."),new("current-stock","Estoque atual relacionado",currentStock,unit,stockAvailability,"Saldo atual exige genealogia completa por lote de origem; produção acumulada não é usada como estoque.","/Inventory",stockExplanation),new("recognized-revenue","Receita reconhecida",recognizedRevenue,"BRL",revenueAvailability,"Receita exige política de reconhecimento e vínculo inequívoco com a produção desta safra.","/Commercial",revenueExplanation)];
    }
    private static List<SeasonClosingIssueDto> BuildIssues(IReadOnlyCollection<SeasonClosingIndicatorDto> indicators)
    {
        decimal V(string code)=>indicators.First(x=>x.Code==code).Value??0;var list=new List<SeasonClosingIssueDto>();var unit=indicators.First(x=>x.Code=="harvested").Unit;void Add(string code,string category,string severity,string title,decimal expected,decimal found,string rule,string impact,string action,string url)=>list.Add(new(Guid.CreateVersion7(),code,category,severity,title,expected,found,unit,rule,impact,category=="OPERATIONAL"?"Operação agrícola":"Conferência de estoque e colheita",action,url,"OPEN"));
        if(V("received")>V("harvested"))Add("RECEIPT_EXCEEDS_HARVEST","QUANTITY","BLOCKER","Recebido excede o apontado",V("harvested"),V("received"),"recebido ≤ apontado, mesma unidade e corte","Resultado quantitativo inconsistente","Abrir recebimentos","/Harvest?kind=RECEIPT");else if(V("received")<V("harvested"))Add("AWAITING_RECEIPT","OPERATIONAL","WARNING","Há produção aguardando recebimento",V("harvested"),V("received"),"apontado − recebido = saldo em trânsito/pendente","Indicadores permanecem provisórios","Registrar recebimento","/Harvest?kind=HARVEST");
        if(V("awaiting-quality")>0)Add("AWAITING_QUALITY","OPERATIONAL","WARNING","Recebimentos aguardam qualidade",0,V("awaiting-quality"),"recebido sem decisão = pendência operacional","Quantidade aprovada é provisória","Concluir inspeção","/Harvest?kind=RECEIPT");if(V("allocated")>V("approved"))Add("ALLOCATION_EXCEEDS_APPROVED","QUANTITY","BLOCKER","Destinações excedem material aprovado",V("approved"),V("allocated"),"destinado ≤ aprovado; processamento legítimo é exibido separadamente","Compromete estoque e resultado","Revisar destinações","/Harvest?kind=RECEIPT");
        return list;
    }
    private SeasonClosingIssueDto[] DeserializeIssues(string? json) =>
        DeserializeSnapshot<SeasonClosingIssueDto>(json, "pendências");

    private SeasonClosingIndicatorDto[] DeserializeIndicators(string? json) =>
        DeserializeSnapshot<SeasonClosingIndicatorDto>(json, "indicadores")
            .Select(x => string.IsNullOrWhiteSpace(x.Definition)
                ? x with
                {
                    Definition = "Definição não registrada no snapshot legado; consulte os critérios da versão que o gerou.",
                    Explanation = "Snapshot histórico anterior à inclusão da definição; seus valores não foram recalculados."
                }
                : x)
            .ToArray();

    private T[] DeserializeSnapshot<T>(string? json, string snapshotKind) where T : class
    {
        // Runs and versions created before snapshots were introduced may not have a value.
        // A malformed or structurally incompatible value must never look like a successful empty check.
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            return [];

        try
        {
            var snapshot = JsonSerializer.Deserialize<T[]>(json, SnapshotJsonOptions)
                ?? throw new JsonException("O snapshot não contém uma coleção JSON.");
            if (snapshot.Any(item => item is null))
                throw new JsonException("O snapshot contém um item incompatível.");
            return snapshot;
        }
        catch (JsonException exception)
        {
            LogInvalidClosingSnapshot(logger, snapshotKind, exception);
            throw new DomainException(
                "O histórico do fechamento está incompatível ou corrompido. Não é seguro continuar até restaurar o snapshot.",
                "closing.invalid_snapshot");
        }
    }
    private static bool SnapshotMatches(IEnumerable<SeasonClosingIndicatorDto> snapshot,IEnumerable<SeasonClosingIndicatorDto> current)=>snapshot.OrderBy(x=>x.Code).Select(x=>(x.Code,x.Value,x.Unit,x.Availability)).SequenceEqual(current.OrderBy(x=>x.Code).Select(x=>(x.Code,x.Value,x.Unit,x.Availability)));
    private SeasonClosingVersionDto MapVersion(ClosingVersionRow x)=>new(x.Id,x.Version,x.State,x.CutoffDate,x.GeneratedAt,x.ResponsibleId,x.SupersedesId,x.Reason,x.Notes,DeserializeIndicators(x.Indicators),DeserializeIssues(x.Issues));
    private async Task<HarvestOperationDto?> Replay(System.Data.IDbConnection db,System.Data.IDbTransaction tx,string table,string key,string hash,string kind,CancellationToken ct){var row=await db.QuerySingleOrDefaultAsync<ReplayRow>(new CommandDefinition($"select id,request_hash Hash,created_at At from agro360.{table} where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=key},tx,cancellationToken:ct));if(row is null)return null;if(row.Hash!=hash)throw new ConflictException("A chave idempotente já foi usada com outro conteúdo.","harvest.idempotency_conflict");return new HarvestOperationDto(row.Id,kind,"REPLAYED",0,"-",0,row.At,key,1);}
    private Task<int> Audit(System.Data.IDbConnection db,System.Data.IDbTransaction tx,string action,string entity,Guid id,CancellationToken ct)=>db.ExecuteAsync(new CommandDefinition("insert into agro360.audit_logs(id,tenant_id,user_id,action,entity_type,entity_id,occurred_at) values(@AuditId,@TenantId,@UserId,@Action,@Entity,@Id,now())",new{AuditId=Guid.CreateVersion7(),tenant.TenantId,UserId=tenant.UserId,Action=action,Entity=entity,Id=id},tx,cancellationToken:ct));
    private sealed record ClosingScopeRow(Guid SeasonId,Guid FarmId,string Season,string Farm,string Crop,DateOnly StartsOn,DateOnly EndsOn,string Unit);
    private sealed record ClosingTotals(decimal RegisteredArea,decimal HarvestedArea,decimal Harvested,decimal Received,decimal AwaitingQuality,decimal Approved,decimal Allocated,decimal MadeAvailable,decimal InProcessing,decimal Loss,decimal Costs,int UnitCount);
    private sealed record ClosingRunRow(Guid Id,DateTimeOffset GeneratedAt,string CriteriaVersion,string Issues);
    private sealed record ClosingVersionRow(Guid Id,Guid SeasonId,int Version,string State,DateOnly CutoffDate,DateTimeOffset GeneratedAt,Guid ResponsibleId,Guid? SupersedesId,string? Reason,string? Notes,string Indicators,string Issues);
    private sealed record PreviousClosing(Guid Id,int Version);
    private sealed class ReceiptSource
    {
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public Guid ProductId { get; set; }
        public Guid WarehouseId { get; set; }
        public DateTime OperationalAt { get; set; }
        public decimal Received { get; set; }
    }
    private sealed record PlanReferences(Guid FarmId,short SeasonStatus,Guid FieldFarmId,decimal FieldArea,string ProductUnit,Guid WarehouseFarmId);private sealed record PlanRow(decimal Quantity,string Unit,string Status,decimal Area);private sealed record ReceiptRow(decimal Quantity,string Unit,string Status,Guid ProductId,string Reference);private sealed record SpecRow(int Version,Guid ProductId,string Status);private sealed record ParameterRow(Guid Id,bool Required,bool EvidenceRequired,decimal? Minimum,decimal? Maximum,string Name);private sealed record AllocationSource(decimal Quantity,decimal Accepted,string Unit,string Status,Guid ProductId,Guid WarehouseId,string Reference,decimal Allocated,decimal AvailableAllocated);private sealed record BalanceRow(Guid Id,decimal Available,decimal AverageCost,long Version);private sealed record DashboardRow(decimal Planned,decimal Harvested,decimal Received,decimal AwaitingQuality,decimal Approved,decimal Loss,decimal Costs,decimal Area,string? Unit,int UnitCount);private sealed record TraceRow(Guid ReceiptId,string LotNumber,string QualityStatus,Guid FarmId,Guid SeasonId,Guid FieldId,Guid HarvestRecordId,decimal ReceivedQuantity,decimal AllocatedQuantity,string Unit);
    private sealed class ReplayRow
    {
        public Guid Id { get; set; }
        public string Hash { get; set; } = string.Empty;
        public DateTime At { get; set; }
    }
    private sealed class InspectionReplay
    {
        public Guid Id { get; set; }
        public string Hash { get; set; } = string.Empty;
        public DateTime At { get; set; }
    }
    public Task<SeasonGenealogyDto> GetSeasonGenealogyAsync(Guid seasonId, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (db, tx) =>
        {
            var season = await db.QuerySingleOrDefaultAsync<SeasonInfoRow>(new CommandDefinition("""
                select s.id SeasonId, s.name SeasonName, f.name FarmName, s.crop Crop
                from agro360.agriculture_seasons s
                join agro360.geo_farms f on f.tenant_id = s.tenant_id and f.id = s.farm_id
                where s.tenant_id = @TenantId and s.id = @SeasonId and s.deleted_at is null
                """, new { tenant.TenantId, SeasonId = seasonId }, tx, cancellationToken: cancellationToken))
                ?? throw new NotFoundException("Safra", seasonId);

            var harvestRecords = (await db.QueryAsync<HarvestRecordGenealogyRow>(new CommandDefinition("""
                select r.id HarvestRecordId, r.commercial_reference CommercialReference, r.operational_at OperationalAt,
                       r.harvested_quantity HarvestedQuantity, r.unit HarvestUnit, f.name FieldName, p.id PlanId
                from agro360.harvest_plans p
                join agro360.geo_fields f on f.tenant_id = p.tenant_id and f.id = p.field_id
                join agro360.harvest_records r on r.tenant_id = p.tenant_id and r.plan_id = p.id
                where p.tenant_id = @TenantId and p.season_id = @SeasonId and p.status <> 'CANCELLED' and r.status <> 'CANCELLED'
                order by r.operational_at
                """, new { tenant.TenantId, SeasonId = seasonId }, tx, cancellationToken: cancellationToken))).ToArray();

            var recordIds = harvestRecords.Select(r => r.HarvestRecordId).Distinct().ToArray();

            var receipts = recordIds.Length == 0 ? [] : (await db.QueryAsync<ReceiptGenealogyRow>(new CommandDefinition("""
                select q.id ReceiptId, q.harvest_record_id HarvestRecordId, q.lot_number LotNumber, q.received_at ReceivedAt,
                       q.received_quantity ReceivedQuantity, q.accepted_quantity AcceptedQuantity, q.unit Unit,
                       q.quality_status QualityStatus, w.name WarehouseName, pr.name ProductName
                from agro360.production_receipts q
                join agro360.inventory_warehouses w on w.tenant_id = q.tenant_id and w.id = q.warehouse_id
                join agro360.inventory_products pr on pr.tenant_id = q.tenant_id and pr.id = q.product_id
                where q.tenant_id = @TenantId and q.harvest_record_id = any(@RecordIds)
                order by q.received_at
                """, new { tenant.TenantId, RecordIds = recordIds }, tx, cancellationToken: cancellationToken))).ToArray();

            var receiptIds = receipts.Select(q => q.ReceiptId).Distinct().ToArray();
            var lotNumbers = receipts.Select(q => q.LotNumber).Distinct().ToArray();

            var intents = receiptIds.Length == 0 ? [] : (await db.QueryAsync<QualityIntentGenealogyRow>(new CommandDefinition("""
                select i.id IntentId, i.origin_id ReceiptId, i.status IntentStatus, i.error_code ErrorCode, i.created_at CreatedAt,
                       ins.id InspectionId, ins.status InspectionStatus, ins.result InspectionResult, ins.inspected_at InspectedAt
                from agro360.quality_inspection_event_intents i
                left join agro360.quality_inspections ins on ins.tenant_id = i.tenant_id and ins.production_receipt_id = i.origin_id and ins.deleted_at is null
                where i.tenant_id = @TenantId and i.process = 'HARVEST_RECEIPT' and i.origin_id = any(@ReceiptIds)
                order by i.created_at
                """, new { tenant.TenantId, ReceiptIds = receiptIds }, tx, cancellationToken: cancellationToken))).ToArray();

            var allocations = receiptIds.Length == 0 ? [] : (await db.QueryAsync<AllocationGenealogyRow>(new CommandDefinition("""
                select a.id AllocationId, a.receipt_id ReceiptId, a.destination Destination, a.quantity Quantity,
                       a.reason Reason, a.created_at CreatedAt, a.stock_movement_id StockMovementId
                from agro360.harvest_material_allocations a
                where a.tenant_id = @TenantId and a.receipt_id = any(@ReceiptIds)
                order by a.created_at
                """, new { tenant.TenantId, ReceiptIds = receiptIds }, tx, cancellationToken: cancellationToken))).ToArray();

            var stockLots = lotNumbers.Length == 0 ? [] : (await db.QueryAsync<StockLotGenealogyRow>(new CommandDefinition("""
                select l.id LotId, l.lot_number LotNumber, l.warehouse_id WarehouseId, l.product_id ProductId,
                       l.quantity Quantity, l.quality_status QualityStatus, l.unit Unit, w.name WarehouseName, pr.name ProductName
                from agro360.inventory_stock_lots l
                join agro360.inventory_warehouses w on w.tenant_id = l.tenant_id and w.id = l.warehouse_id
                join agro360.inventory_products pr on pr.tenant_id = l.tenant_id and pr.id = l.product_id
                where l.tenant_id = @TenantId and l.lot_number = any(@LotNumbers)
                """, new { tenant.TenantId, LotNumbers = lotNumbers }, tx, cancellationToken: cancellationToken))).ToArray();

            var stockLotIds = stockLots.Select(l => l.LotId).Distinct().ToArray();

            var industrialReservations = receiptIds.Length == 0 ? [] : (await db.QueryAsync<IndustrialGenealogyRow>(new CommandDefinition("""
                select pmr.id ReservationId, pmr.receipt_id ReceiptId, pmr.order_id OrderId, pmr.quantity ReservedQuantity,
                       pmr.consumed_quantity ConsumedQuantity, pmr.unit Unit, pmr.status Status,
                       po.order_number OrderNumber,
                       pb.id BatchId, pb.batch_number BatchNumber, pb.quantity BatchQuantity, pb.unit BatchUnit, pb.quality_status BatchQualityStatus
                from agro360.production_material_reservations pmr
                join agro360.production_orders po on po.tenant_id = pmr.tenant_id and po.id = pmr.order_id
                left join agro360.production_batches pb on pb.tenant_id = po.tenant_id and pb.order_id = po.id and pb.deleted_at is null
                where pmr.tenant_id = @TenantId and pmr.receipt_id = any(@ReceiptIds)
                """, new { tenant.TenantId, ReceiptIds = receiptIds }, tx, cancellationToken: cancellationToken))).ToArray();

            var shipments = stockLotIds.Length == 0 ? [] : (await db.QueryAsync<ShipmentGenealogyRow>(new CommandDefinition("""
                select si.id ShipmentItemId, si.shipment_id ShipmentId, si.stock_lot_id StockLotId, l.lot_number LotNumber,
                       si.checked_quantity CheckedQuantity, si.accepted_quantity AcceptedQuantity, si.returned_quantity ReturnedQuantity,
                       si.lost_quantity LostQuantity, si.unit Unit, s.number ShipmentNumber, s.status ShipmentStatus, s.dispatched_at DispatchedAt,
                       c.name CustomerName, oi.unit_price UnitPrice,
                       da.status DeliveryStatus, da.occurred_at DeliveryOccurredAt,
                       ret.status ReturnStatus, ret.quantity ReturnQuantity
                from agro360.fulfillment_shipment_items si
                join agro360.inventory_stock_lots l on l.tenant_id = si.tenant_id and l.id = si.stock_lot_id
                join agro360.fulfillment_shipments s on s.tenant_id = si.tenant_id and s.id = si.shipment_id and s.deleted_at is null
                join agro360.crm_customers c on c.tenant_id = s.tenant_id and c.id = s.customer_id
                join agro360.sales_order_items oi on oi.tenant_id = si.tenant_id and oi.id = si.order_item_id
                left join agro360.fulfillment_delivery_attempt_items dai on dai.tenant_id = si.tenant_id and dai.shipment_item_id = si.id
                left join agro360.fulfillment_delivery_attempts da on da.tenant_id = dai.tenant_id and da.id = dai.attempt_id
                left join agro360.fulfillment_returns ret on ret.tenant_id = si.tenant_id and ret.shipment_item_id = si.id
                where si.tenant_id = @TenantId and si.stock_lot_id = any(@StockLotIds)
                """, new { tenant.TenantId, StockLotIds = stockLotIds }, tx, cancellationToken: cancellationToken))).ToArray();

            var manualLinks = (await db.QueryAsync<ManualGenealogyLinkRow>(new CommandDefinition("""
                select id Id, kind Kind, origin_type OriginType, origin_id OriginId, destination_type DestinationType,
                       destination_id DestinationId, season_id SeasonId, field_id FieldId, lot_number LotNumber,
                       quantity Quantity, unit Unit, status Status, created_at CreatedAt
                from agro360.operational_genealogy_links
                where tenant_id = @TenantId and (season_id = @SeasonId or lot_number = any(@LotNumbers))
                """, new { tenant.TenantId, SeasonId = seasonId, LotNumbers = lotNumbers }, tx, cancellationToken: cancellationToken))).ToArray();

            var nodes = new List<GenealogyNodeDto>();

            foreach (var r in harvestRecords)
            {
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Colheita e Talhão",
                    OriginLabel: $"Safra {season.SeasonName} · Talhão {r.FieldName}",
                    DestinationLabel: $"Apontamento {r.CommercialReference}",
                    Status: "Colhido",
                    Quantity: r.HarvestedQuantity,
                    Unit: r.HarvestUnit,
                    LotNumber: null,
                    HasGap: false,
                    GapReason: null,
                    OccurredAt: r.OperationalAt,
                    Details: $"Apontamento {r.CommercialReference}"));
            }

            foreach (var q in receipts)
            {
                var relatedRecord = harvestRecords.FirstOrDefault(r => r.HarvestRecordId == q.HarvestRecordId);
                var origin = relatedRecord is not null ? $"Apontamento {relatedRecord.CommercialReference}" : "Apontamento não identificado";
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Recebimento Físico",
                    OriginLabel: origin,
                    DestinationLabel: $"Depósito {q.WarehouseName} (Lote {q.LotNumber})",
                    Status: TranslateQualityStatus(q.QualityStatus),
                    Quantity: q.ReceivedQuantity,
                    Unit: q.Unit,
                    LotNumber: q.LotNumber,
                    HasGap: relatedRecord is null,
                    GapReason: relatedRecord is null ? "Recebimento sem apontamento de colheita vinculado" : null,
                    OccurredAt: q.ReceivedAt,
                    Details: $"Recebido em {q.WarehouseName} · Aprovado: {q.AcceptedQuantity:N2} {q.Unit}"));
            }

            foreach (var i in intents)
            {
                var q = receipts.FirstOrDefault(r => r.ReceiptId == i.ReceiptId);
                var lot = q?.LotNumber ?? "Lote não identificado";
                var isPendingModel = string.Equals(i.IntentStatus, "PENDING_MODEL", StringComparison.OrdinalIgnoreCase);
                var isAmbiguous = string.Equals(i.IntentStatus, "AMBIGUOUS", StringComparison.OrdinalIgnoreCase);
                var hasGap = isPendingModel || isAmbiguous;
                var gapReason = isPendingModel ? "Sem modelo de inspeção publicado para o evento de colheita"
                              : isAmbiguous ? "Ambiguidade entre múltiplos modelos de inspeção" : null;

                nodes.Add(new GenealogyNodeDto(
                    Stage: "Qualidade",
                    OriginLabel: $"Lote {lot}",
                    DestinationLabel: i.InspectionId.HasValue ? "Laudo de Inspeção" : "Intent de Qualidade",
                    Status: TranslateIntentOrInspection(i.IntentStatus, i.InspectionResult),
                    Quantity: q?.ReceivedQuantity,
                    Unit: q?.Unit,
                    LotNumber: lot,
                    HasGap: hasGap,
                    GapReason: gapReason,
                    OccurredAt: i.InspectedAt ?? i.CreatedAt,
                    Details: i.InspectionId.HasValue ? $"Inspeção concluída com resultado {i.InspectionResult}" : $"Gatilho operacional {i.IntentStatus}"));
            }

            foreach (var a in allocations)
            {
                var q = receipts.FirstOrDefault(r => r.ReceiptId == a.ReceiptId);
                var lot = q?.LotNumber ?? "Lote não identificado";
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Destinação Agrícola",
                    OriginLabel: $"Lote {lot}",
                    DestinationLabel: TranslateDestination(a.Destination),
                    Status: a.Destination == "AVAILABLE" ? "Disponibilizado" : "Destinado",
                    Quantity: a.Quantity,
                    Unit: q?.Unit,
                    LotNumber: lot,
                    HasGap: false,
                    GapReason: null,
                    OccurredAt: a.CreatedAt,
                    Details: $"Destinação {a.Destination}: {a.Reason}"));
            }

            foreach (var sl in stockLots)
            {
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Estoque Físico",
                    OriginLabel: $"Lote {sl.LotNumber} em {sl.WarehouseName}",
                    DestinationLabel: "Saldo em Depósito",
                    Status: sl.QualityStatus == "APPROVED" ? "Liberado" : "Bloqueado",
                    Quantity: sl.Quantity,
                    Unit: sl.Unit,
                    LotNumber: sl.LotNumber,
                    HasGap: false,
                    GapReason: null,
                    OccurredAt: null,
                    Details: $"Produto: {sl.ProductName} · Depósito: {sl.WarehouseName}"));
            }

            foreach (var ind in industrialReservations)
            {
                var q = receipts.FirstOrDefault(r => r.ReceiptId == ind.ReceiptId);
                var lot = q?.LotNumber ?? "Lote não identificado";
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Beneficiamento Industrial",
                    OriginLabel: $"Reserva do Lote {lot}",
                    DestinationLabel: $"Ordem {ind.OrderNumber}" + (ind.BatchNumber != null ? $" (Lote Ind. {ind.BatchNumber})" : ""),
                    Status: ind.Status == "CONSUMED" ? "Consumido" : "Reservado",
                    Quantity: ind.ConsumedQuantity > 0 ? ind.ConsumedQuantity : ind.ReservedQuantity,
                    Unit: ind.Unit,
                    LotNumber: ind.BatchNumber ?? lot,
                    HasGap: false,
                    GapReason: null,
                    OccurredAt: null,
                    Details: $"Ordem industrial {ind.OrderNumber}"));
            }

            var hasMissingFieldOrigin = false;
            foreach (var s in shipments)
            {
                var hasOrigin = harvestRecords.Any();
                if (!hasOrigin) hasMissingFieldOrigin = true;

                nodes.Add(new GenealogyNodeDto(
                    Stage: "Expedição",
                    OriginLabel: $"Lote {s.LotNumber}",
                    DestinationLabel: $"Remessa {s.ShipmentNumber} · Cliente {s.CustomerName}",
                    Status: TranslateShipmentStatus(s.ShipmentStatus),
                    Quantity: s.CheckedQuantity,
                    Unit: s.Unit,
                    LotNumber: s.LotNumber,
                    HasGap: !hasOrigin,
                    GapReason: !hasOrigin ? "Lote expedido sem talhão de colheita de origem" : null,
                    OccurredAt: s.DispatchedAt,
                    Details: $"Remessa {s.ShipmentNumber} para {s.CustomerName}"));

                if (s.AcceptedQuantity > 0 || s.ReturnedQuantity > 0)
                {
                    nodes.Add(new GenealogyNodeDto(
                        Stage: "Entrega Comercial",
                        OriginLabel: $"Remessa {s.ShipmentNumber}",
                        DestinationLabel: $"Cliente {s.CustomerName}",
                        Status: s.DeliveryStatus == "ACCEPTED" ? "Entregue e Aceito" : "Entrega Registrada",
                        Quantity: s.AcceptedQuantity,
                        Unit: s.Unit,
                        LotNumber: s.LotNumber,
                        HasGap: s.ReturnedQuantity > 0 && s.ReturnStatus == "AWAITING_QUALITY",
                        GapReason: s.ReturnedQuantity > 0 && s.ReturnStatus == "AWAITING_QUALITY" ? "Devolução física pendente de destinação de qualidade" : null,
                        OccurredAt: s.DeliveryOccurredAt,
                        Details: $"Aceito: {s.AcceptedQuantity:N2} {s.Unit} · Retornado: {s.ReturnedQuantity:N2} {s.Unit}"));
                }
            }

            foreach (var m in manualLinks)
            {
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Vínculo Rastreado",
                    OriginLabel: $"{m.OriginType}",
                    DestinationLabel: $"{m.DestinationType}",
                    Status: m.Status == "ACTIVE" ? "Ativo" : m.Status,
                    Quantity: m.Quantity,
                    Unit: m.Unit,
                    LotNumber: m.LotNumber,
                    HasGap: false,
                    GapReason: null,
                    OccurredAt: m.CreatedAt,
                    Details: $"Vínculo {m.Kind}"));
            }

            var totalReceived = receipts.Sum(q => q.ReceivedQuantity);
            var totalAllocated = allocations.Sum(a => a.Quantity);

            decimal? currentStockQty = null;
            string? currentStockUnit = null;
            string currentStockStatus = "UNAVAILABLE";
            string? currentStockExplanation = null;

            if (stockLots.Length == 0)
            {
                currentStockQty = 0m;
                currentStockStatus = "AVAILABLE";
                currentStockExplanation = "Nenhum lote com saldo remanescente em estoque.";
            }
            else
            {
                var units = stockLots.Select(sl => sl.Unit).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToArray();
                if (units.Length > 1)
                {
                    currentStockExplanation = "Unidades físicas heterogêneas nos lotes de estoque relacionados; consolidação recusada por integridade.";
                }
                else
                {
                    currentStockUnit = units.FirstOrDefault();
                    currentStockQty = stockLots.Sum(sl => sl.Quantity);
                    currentStockStatus = "AVAILABLE";
                    currentStockExplanation = "Saldo atual dos lotes rastreados originados desta safra.";
                }
            }

            decimal? recognizedRevenue = null;
            string revenueStatus = "UNAVAILABLE";
            string? revenueExplanation = null;

            if (shipments.Length == 0)
            {
                recognizedRevenue = 0m;
                revenueStatus = "AVAILABLE";
                revenueExplanation = "Nenhuma remessa comercial expedida para os lotes desta safra.";
            }
            else
            {
                var pendingShipments = shipments.Any(s => s.ShipmentStatus is not ("RECONCILED" or "CANCELLED") && s.AcceptedQuantity == 0);
                if (pendingShipments)
                {
                    revenueExplanation = "Há remessas com entrega pendente de confirmação; receita reconhecida exige elos de entrega aceita concluídos.";
                }
                else
                {
                    decimal totalRev = 0m;
                    foreach (var s in shipments)
                    {
                        var netDelivered = GenealogyRules.CalculateNetDeliveredQuantity(s.AcceptedQuantity, s.ReturnedQuantity);
                        totalRev += netDelivered * s.UnitPrice;
                    }
                    recognizedRevenue = totalRev;
                    revenueStatus = "AVAILABLE";
                    revenueExplanation = "Receita reconhecida a partir de entregas formalmente aceitas dos lotes da safra, deduzidos retornos destinados.";
                }
            }

            return new SeasonGenealogyDto(
                SeasonId: season.SeasonId,
                SeasonName: season.SeasonName,
                FarmName: season.FarmName,
                Crop: season.Crop,
                Nodes: nodes,
                HasMissingFieldOrigin: hasMissingFieldOrigin,
                HasUnlinkedShipments: false,
                TotalReceivedQuantity: totalReceived,
                TotalAllocatedQuantity: totalAllocated,
                CurrentStockQuantity: currentStockQty,
                CurrentStockUnit: currentStockUnit,
                CurrentStockStatus: currentStockStatus,
                CurrentStockExplanation: currentStockExplanation,
                RecognizedRevenue: recognizedRevenue,
                RevenueCurrency: "BRL",
                RevenueStatus: revenueStatus,
                RevenueExplanation: revenueExplanation);
        }, cancellationToken);

    public Task<LotGenealogyDto> GetLotGenealogyAsync(string lotNumber, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (db, tx) =>
        {
            if (string.IsNullOrWhiteSpace(lotNumber))
                throw new DomainException("O número do lote é obrigatório.", "genealogy.lot_required");

            var trimmedLot = lotNumber.Trim();

            var receipt = await db.QuerySingleOrDefaultAsync<ReceiptGenealogyRow>(new CommandDefinition("""
                select q.id ReceiptId, q.harvest_record_id HarvestRecordId, q.lot_number LotNumber, q.received_at ReceivedAt,
                       q.received_quantity ReceivedQuantity, q.accepted_quantity AcceptedQuantity, q.unit Unit,
                       q.quality_status QualityStatus, w.name WarehouseName, pr.name ProductName
                from agro360.production_receipts q
                join agro360.inventory_warehouses w on w.tenant_id = q.tenant_id and w.id = q.warehouse_id
                join agro360.inventory_products pr on pr.tenant_id = q.tenant_id and pr.id = q.product_id
                where q.tenant_id = @TenantId and q.lot_number = @LotNumber
                """, new { tenant.TenantId, LotNumber = trimmedLot }, tx, cancellationToken: cancellationToken));

            var stockLot = await db.QuerySingleOrDefaultAsync<StockLotGenealogyRow>(new CommandDefinition("""
                select l.id LotId, l.lot_number LotNumber, l.warehouse_id WarehouseId, l.product_id ProductId,
                       l.quantity Quantity, l.quality_status QualityStatus, l.unit Unit, w.name WarehouseName, pr.name ProductName
                from agro360.inventory_stock_lots l
                join agro360.inventory_warehouses w on w.tenant_id = l.tenant_id and w.id = l.warehouse_id
                join agro360.inventory_products pr on pr.tenant_id = l.tenant_id and pr.id = l.product_id
                where l.tenant_id = @TenantId and l.lot_number = @LotNumber
                """, new { tenant.TenantId, LotNumber = trimmedLot }, tx, cancellationToken: cancellationToken));

            var batch = await db.QuerySingleOrDefaultAsync<IndustrialGenealogyRow>(new CommandDefinition("""
                select pb.id BatchId, pb.batch_number BatchNumber, pb.quantity BatchQuantity, pb.unit BatchUnit,
                       pb.quality_status BatchQualityStatus, po.order_number OrderNumber, po.id OrderId,
                       pp.name ProductName,
                       exists(select 1 from agro360.production_batch_traceability pbt
                              where pbt.tenant_id = pb.tenant_id and pbt.batch_id = pb.id
                                and pbt.source_entity = 'production_receipts') HasAgriculturalOrigin
                from agro360.production_batches pb
                join agro360.production_orders po on po.tenant_id = pb.tenant_id and po.id = pb.order_id
                join agro360.production_products pp on pp.tenant_id = pb.tenant_id and pp.id = pb.product_id
                where pb.tenant_id = @TenantId and pb.batch_number = @LotNumber and pb.deleted_at is null
                """, new { tenant.TenantId, LotNumber = trimmedLot }, tx, cancellationToken: cancellationToken));

            if (receipt is null && stockLot is null && batch is null)
                throw new NotFoundException("Lote", trimmedLot);

            HarvestRecordGenealogyRow? record = null;
            SeasonInfoRow? season = null;

            if (receipt is not null)
            {
                record = await db.QuerySingleOrDefaultAsync<HarvestRecordGenealogyRow>(new CommandDefinition("""
                    select r.id HarvestRecordId, r.commercial_reference CommercialReference, r.operational_at OperationalAt,
                           r.harvested_quantity HarvestedQuantity, r.unit HarvestUnit, f.name FieldName, p.id PlanId,
                           s.id SeasonId, s.name SeasonName, fa.name FarmName, s.crop Crop
                    from agro360.harvest_records r
                    join agro360.harvest_plans p on p.tenant_id = r.tenant_id and p.id = r.plan_id
                    join agro360.geo_fields f on f.tenant_id = p.tenant_id and f.id = p.field_id
                    join agro360.agriculture_seasons s on s.tenant_id = p.tenant_id and s.id = p.season_id
                    join agro360.geo_farms fa on fa.tenant_id = s.tenant_id and fa.id = s.farm_id
                    where r.tenant_id = @TenantId and r.id = @RecordId
                    """, new { tenant.TenantId, RecordId = receipt.HarvestRecordId }, tx, cancellationToken: cancellationToken));

                if (record is not null)
                {
                    season = await db.QuerySingleOrDefaultAsync<SeasonInfoRow>(new CommandDefinition("""
                        select s.id SeasonId, s.name SeasonName, f.name FarmName, s.crop Crop
                        from agro360.harvest_plans p
                        join agro360.agriculture_seasons s on s.tenant_id = p.tenant_id and s.id = p.season_id
                        join agro360.geo_farms f on f.tenant_id = s.tenant_id and f.id = s.farm_id
                        where p.tenant_id = @TenantId and p.id = @PlanId
                        """, new { tenant.TenantId, record.PlanId }, tx, cancellationToken: cancellationToken));
                }
            }

            var shipments = stockLot is null ? [] : (await db.QueryAsync<ShipmentGenealogyRow>(new CommandDefinition("""
                select si.id ShipmentItemId, si.shipment_id ShipmentId, si.stock_lot_id StockLotId, @LotNumber LotNumber,
                       si.checked_quantity CheckedQuantity, si.accepted_quantity AcceptedQuantity, si.returned_quantity ReturnedQuantity,
                       si.lost_quantity LostQuantity, si.unit Unit, s.number ShipmentNumber, s.status ShipmentStatus, s.dispatched_at DispatchedAt,
                       c.name CustomerName, oi.unit_price UnitPrice,
                       da.status DeliveryStatus, da.occurred_at DeliveryOccurredAt,
                       ret.status ReturnStatus, ret.quantity ReturnQuantity
                from agro360.fulfillment_shipment_items si
                join agro360.fulfillment_shipments s on s.tenant_id = si.tenant_id and s.id = si.shipment_id and s.deleted_at is null
                join agro360.crm_customers c on c.tenant_id = s.tenant_id and c.id = s.customer_id
                join agro360.sales_order_items oi on oi.tenant_id = si.tenant_id and oi.id = si.order_item_id
                left join agro360.fulfillment_delivery_attempt_items dai on dai.tenant_id = si.tenant_id and dai.shipment_item_id = si.id
                left join agro360.fulfillment_delivery_attempts da on da.tenant_id = dai.tenant_id and da.id = dai.attempt_id
                left join agro360.fulfillment_returns ret on ret.tenant_id = si.tenant_id and ret.shipment_item_id = si.id
                where si.tenant_id = @TenantId and si.stock_lot_id = @StockLotId
                """, new { tenant.TenantId, LotNumber = trimmedLot, StockLotId = stockLot.LotId }, tx, cancellationToken: cancellationToken))).ToArray();

            var nodes = new List<GenealogyNodeDto>();
            var hasGap = false;
            string? gapReason = null;

            if (record is not null && season is not null)
            {
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Colheita e Talhão",
                    OriginLabel: $"Safra {season.SeasonName} · Talhão {record.FieldName}",
                    DestinationLabel: $"Apontamento {record.CommercialReference}",
                    Status: "Colhido",
                    Quantity: record.HarvestedQuantity,
                    Unit: record.HarvestUnit,
                    LotNumber: trimmedLot,
                    HasGap: false,
                    GapReason: null,
                    OccurredAt: record.OperationalAt,
                    Details: $"Apontamento {record.CommercialReference}"));
            }
            else if (shipments.Length > 0)
            {
                hasGap = true;
                gapReason = "Lote expedido sem talhão de colheita ou safra de origem associada.";
            }

            if (receipt is not null)
            {
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Recebimento Físico",
                    OriginLabel: record is not null ? $"Apontamento {record.CommercialReference}" : "Não vinculado",
                    DestinationLabel: $"Depósito {receipt.WarehouseName} (Lote {receipt.LotNumber})",
                    Status: TranslateQualityStatus(receipt.QualityStatus),
                    Quantity: receipt.ReceivedQuantity,
                    Unit: receipt.Unit,
                    LotNumber: receipt.LotNumber,
                    HasGap: record is null,
                    GapReason: record is null ? "Recebimento sem apontamento de colheita de origem" : null,
                    OccurredAt: receipt.ReceivedAt,
                    Details: $"Recebido em {receipt.WarehouseName}"));

                if (record is null)
                {
                    hasGap = true;
                    gapReason ??= "Recebimento sem apontamento de colheita ou safra de origem associada.";
                }
            }

            if (stockLot is not null)
            {
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Estoque Físico",
                    OriginLabel: $"Lote {stockLot.LotNumber}",
                    DestinationLabel: $"Depósito {stockLot.WarehouseName}",
                    Status: stockLot.QualityStatus == "APPROVED" ? "Liberado" : "Bloqueado",
                    Quantity: stockLot.Quantity,
                    Unit: stockLot.Unit,
                    LotNumber: stockLot.LotNumber,
                    HasGap: false,
                    GapReason: null,
                    OccurredAt: null,
                    Details: $"Saldo atual em estoque: {stockLot.Quantity:N2} {stockLot.Unit}"));
            }

            if (batch is not null)
            {
                var hasBatchOrigin = batch.HasAgriculturalOrigin;
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Beneficiamento Industrial",
                    OriginLabel: hasBatchOrigin ? $"Lote {trimmedLot}" : "Origem agrícola não vinculada",
                    DestinationLabel: $"Ordem {batch.OrderNumber} (Lote Ind. {batch.BatchNumber})",
                    Status: TranslateQualityStatus(batch.BatchQualityStatus ?? "PENDING"),
                    Quantity: batch.BatchQuantity,
                    Unit: batch.BatchUnit,
                    LotNumber: batch.BatchNumber,
                    HasGap: !hasBatchOrigin,
                    GapReason: !hasBatchOrigin ? "Lote industrial sem recebimento ou lote de estoque agrícola vinculado." : null,
                    OccurredAt: null,
                    Details: $"Lote produzido pela ordem industrial {batch.OrderNumber}"));

                if (!hasBatchOrigin)
                {
                    hasGap = true;
                    gapReason ??= "Lote industrial sem origem agrícola vinculada.";
                }
            }

            foreach (var s in shipments)
            {
                nodes.Add(new GenealogyNodeDto(
                    Stage: "Expedição",
                    OriginLabel: $"Lote {trimmedLot}",
                    DestinationLabel: $"Remessa {s.ShipmentNumber} · Cliente {s.CustomerName}",
                    Status: TranslateShipmentStatus(s.ShipmentStatus),
                    Quantity: s.CheckedQuantity,
                    Unit: s.Unit,
                    LotNumber: trimmedLot,
                    HasGap: hasGap,
                    GapReason: gapReason,
                    OccurredAt: s.DispatchedAt,
                    Details: $"Expedição para {s.CustomerName}"));

                if (s.AcceptedQuantity > 0)
                {
                    nodes.Add(new GenealogyNodeDto(
                        Stage: "Entrega Comercial",
                        OriginLabel: $"Remessa {s.ShipmentNumber}",
                        DestinationLabel: $"Cliente {s.CustomerName}",
                        Status: s.DeliveryStatus == "ACCEPTED" ? "Entregue e Aceito" : "Entrega Registrada",
                        Quantity: s.AcceptedQuantity,
                        Unit: s.Unit,
                        LotNumber: trimmedLot,
                        HasGap: false,
                        GapReason: null,
                        OccurredAt: s.DeliveryOccurredAt,
                        Details: $"Quantidade aceita: {s.AcceptedQuantity:N2} {s.Unit}"));
                }
            }

            var productName = receipt?.ProductName ?? stockLot?.ProductName ?? batch?.ProductName ?? "Produto Agrícola";
            var status = stockLot?.QualityStatus ?? receipt?.QualityStatus ?? batch?.BatchQualityStatus ?? "REGISTRADO";

            return new LotGenealogyDto(
                LotNumber: trimmedLot,
                ProductName: productName,
                SeasonId: season?.SeasonId,
                SeasonName: season?.SeasonName,
                FarmName: season?.FarmName,
                FieldName: record?.FieldName,
                Status: TranslateQualityStatus(status),
                Nodes: nodes,
                HasGap: hasGap,
                GapReason: gapReason);
        }, cancellationToken);

    public Task<Guid> RecordGenealogyLinkAsync(RecordGenealogyLinkCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (db, tx) =>
        {
            var key = Required(command.IdempotencyKey, nameof(command.IdempotencyKey));
            GenealogyRules.ValidateLink(
                command.Kind,
                command.OriginType,
                command.OriginId,
                command.DestinationType,
                command.DestinationId,
                command.Quantity,
                command.Unit,
                key);

            var prior = await db.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "select id from agro360.operational_genealogy_links where tenant_id = @TenantId and idempotency_key = @Key",
                new { tenant.TenantId, Key = key }, tx, cancellationToken: cancellationToken));
            if (prior.HasValue) return prior.Value;

            var id = Guid.CreateVersion7();
            await db.ExecuteAsync(new CommandDefinition("""
                insert into agro360.operational_genealogy_links(
                    id, tenant_id, kind, origin_type, origin_id, destination_type, destination_id,
                    season_id, field_id, lot_number, quantity, unit, status, metadata, idempotency_key, created_by
                ) values (
                    @Id, @TenantId, @Kind, @OriginType, @OriginId, @DestinationType, @DestinationId,
                    @SeasonId, @FieldId, @LotNumber, @Quantity, @Unit, 'ACTIVE', cast(@Metadata as jsonb), @Key, @UserId
                )
                on conflict (tenant_id, kind, origin_id, destination_id) do update set
                    quantity = excluded.quantity,
                    unit = excluded.unit,
                    lot_number = coalesce(excluded.lot_number, agro360.operational_genealogy_links.lot_number),
                    metadata = coalesce(excluded.metadata, agro360.operational_genealogy_links.metadata);
                """, new
            {
                Id = id,
                tenant.TenantId,
                command.Kind,
                command.OriginType,
                command.OriginId,
                command.DestinationType,
                command.DestinationId,
                command.SeasonId,
                command.FieldId,
                command.LotNumber,
                command.Quantity,
                command.Unit,
                Metadata = string.IsNullOrWhiteSpace(command.Metadata) ? "{}" : command.Metadata,
                Key = key,
                UserId = tenant.UserId
            }, tx, cancellationToken: cancellationToken));

            await Audit(db, tx, "record_link", "OperationalGenealogyLink", id, cancellationToken);
            return id;
        }, cancellationToken);

    private static string TranslateQualityStatus(string status) => status switch
    {
        "AWAITING_INSPECTION" => "Aguardando Inspeção",
        "APPROVED" => "Aprovado",
        "BLOCKED" => "Bloqueado",
        "QUARANTINE" => "Quarentena",
        "REJECTED" => "Reprovado",
        "PARTIALLY_ALLOCATED" => "Parcialmente Destinado",
        "ALLOCATED" => "Destinado",
        _ => status
    };

    private static string TranslateIntentOrInspection(string intentStatus, string? result)
    {
        if (!string.IsNullOrWhiteSpace(result))
        {
            return result switch
            {
                "CONFORMING" => "Conforme",
                "NON_CONFORMING" => "Não Conforme",
                "INCONCLUSIVE" => "Inconclusivo",
                _ => result
            };
        }

        return intentStatus switch
        {
            "STARTED" => "Em Inspeção",
            "PENDING_MODEL" => "Sem Modelo Aplicável",
            "AMBIGUOUS" => "Ambiguidade de Critério",
            "SKIPPED_NO_ACTOR" => "Pendente de Inspetor",
            "PENDING" => "Gatilho Pendente",
            _ => intentStatus
        };
    }

    private static string TranslateDestination(string destination) => destination switch
    {
        "AVAILABLE" => "Estoque Disponível",
        "QUARANTINE" => "Quarentena",
        "RECLASSIFICATION" => "Reclassificação",
        "REPROCESSING" => "Reprocessamento",
        "RETURN_TO_ORIGIN" => "Devolução à Origem",
        "LOSS" => "Perda Física",
        "DISPOSAL" => "Descarte",
        _ => destination
    };

    private static string TranslateShipmentStatus(string status) => status switch
    {
        "PREPARING" => "Em Preparação",
        "CHECKED" => "Conferido",
        "DISPATCHED" => "Despachado",
        "IN_DELIVERY" => "Em Transporte",
        "PARTIAL" => "Entrega Parcial",
        "RETURN_PENDING" => "Retorno Pendente",
        "RECONCILED" => "Reconciliado",
        "CANCELLED" => "Cancelado",
        _ => status
    };

    private sealed class StockLotClosingRow
    {
        public decimal Quantity { get; set; }
        public string? Unit { get; set; }
    }

    private sealed class RevenueClosingRow
    {
        public decimal RecognizedRevenue { get; set; }
        public int TotalShipmentItems { get; set; }
        public int PendingShipmentItems { get; set; }
    }

    private sealed class SeasonInfoRow
    {
        public Guid SeasonId { get; set; }
        public string SeasonName { get; set; } = string.Empty;
        public string FarmName { get; set; } = string.Empty;
        public string Crop { get; set; } = string.Empty;
    }

    private sealed class HarvestRecordGenealogyRow
    {
        public Guid HarvestRecordId { get; set; }
        public string CommercialReference { get; set; } = string.Empty;
        public DateTimeOffset OperationalAt { get; set; }
        public decimal HarvestedQuantity { get; set; }
        public string HarvestUnit { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public Guid PlanId { get; set; }
    }

    private sealed class ReceiptGenealogyRow
    {
        public Guid ReceiptId { get; set; }
        public Guid HarvestRecordId { get; set; }
        public string LotNumber { get; set; } = string.Empty;
        public DateTimeOffset ReceivedAt { get; set; }
        public decimal ReceivedQuantity { get; set; }
        public decimal AcceptedQuantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string QualityStatus { get; set; } = string.Empty;
        public string WarehouseName { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
    }

    private sealed class QualityIntentGenealogyRow
    {
        public Guid IntentId { get; set; }
        public Guid ReceiptId { get; set; }
        public string IntentStatus { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public Guid? InspectionId { get; set; }
        public string? InspectionStatus { get; set; }
        public string? InspectionResult { get; set; }
        public DateTimeOffset? InspectedAt { get; set; }
    }

    private sealed class AllocationGenealogyRow
    {
        public Guid AllocationId { get; set; }
        public Guid ReceiptId { get; set; }
        public string Destination { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public Guid? StockMovementId { get; set; }
    }

    private sealed class StockLotGenealogyRow
    {
        public Guid LotId { get; set; }
        public string LotNumber { get; set; } = string.Empty;
        public Guid WarehouseId { get; set; }
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }
        public string QualityStatus { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
    }

    private sealed class IndustrialGenealogyRow
    {
        public Guid ReservationId { get; set; }
        public Guid ReceiptId { get; set; }
        public Guid OrderId { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal ConsumedQuantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string OrderNumber { get; set; } = string.Empty;
        public Guid? BatchId { get; set; }
        public string? BatchNumber { get; set; }
        public decimal? BatchQuantity { get; set; }
        public string? BatchUnit { get; set; }
        public string? BatchQualityStatus { get; set; }
        public string? ProductName { get; set; }
        public bool HasAgriculturalOrigin { get; set; }
    }

    private sealed class ShipmentGenealogyRow
    {
        public Guid ShipmentItemId { get; set; }
        public Guid ShipmentId { get; set; }
        public Guid StockLotId { get; set; }
        public string LotNumber { get; set; } = string.Empty;
        public decimal CheckedQuantity { get; set; }
        public decimal AcceptedQuantity { get; set; }
        public decimal ReturnedQuantity { get; set; }
        public decimal LostQuantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string ShipmentNumber { get; set; } = string.Empty;
        public string ShipmentStatus { get; set; } = string.Empty;
        public DateTimeOffset? DispatchedAt { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public string? DeliveryStatus { get; set; }
        public DateTimeOffset? DeliveryOccurredAt { get; set; }
        public string? ReturnStatus { get; set; }
        public decimal? ReturnQuantity { get; set; }
    }

    private sealed class ManualGenealogyLinkRow
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string OriginType { get; set; } = string.Empty;
        public Guid OriginId { get; set; }
        public string DestinationType { get; set; } = string.Empty;
        public Guid DestinationId { get; set; }
        public Guid? SeasonId { get; set; }
        public Guid? FieldId { get; set; }
        public string? LotNumber { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }
}
