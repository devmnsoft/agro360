using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agro360.Application.Contracts;
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
                select r.harvested_quantity Quantity, r.unit, p.product_id ProductId, p.destination_warehouse_id WarehouseId, r.operational_at,
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
        var unavailable=x.UnitCount>1;string availability=unavailable?"UNAVAILABLE":"AVAILABLE";string? explanation=unavailable?"Há unidades físicas incompatíveis no escopo; selecione/corrija a origem antes de consolidar.":null;SeasonClosingIndicatorDto Q(string code,string label,decimal value,string definition,string source)=>new(code,label,unavailable?null:value,unit,availability,definition,source,explanation);
        return [new SeasonClosingIndicatorDto(Code:"registered-area",Label:"Área cadastrada",Value:x.RegisteredArea,Unit:"ha",Availability:"AVAILABLE",Definition:"Soma da área planejada ativa da safra; exclui planos cancelados e não representa área executada.",SourceUrl:"/Harvest?kind=PLAN",Explanation:null),new SeasonClosingIndicatorDto(Code:"harvested-area",Label:"Área colhida",Value:x.HarvestedArea,Unit:"ha",Availability:"AVAILABLE",Definition:"Soma das áreas efetivamente informadas em apontamentos não cancelados cuja data operacional não ultrapassa o corte.",SourceUrl:"/Harvest?kind=HARVEST",Explanation:null),Q("harvested","Produção apontada",x.Harvested,"Apontamentos não cancelados por data operacional até o corte.","/Harvest?kind=HARVEST"),Q("received","Produção recebida",x.Received,"Recebimentos físicos por data operacional até o corte.","/Harvest?kind=RECEIPT"),Q("awaiting-quality","Aguardando inspeção",x.AwaitingQuality,"Recebido sem decisão de qualidade; é pendência operacional.","/Harvest?kind=RECEIPT"),Q("approved","Quantidade aprovada",x.Approved,"Quantidade aceita pela decisão de qualidade vigente.","/Harvest?kind=RECEIPT"),Q("allocated","Quantidade destinada",x.Allocated,"Destinações registradas até o corte, sem assumir equivalência com produto transformado.","/Harvest?kind=RECEIPT"),Q("made-available","Entrada disponibilizada",x.MadeAvailable,"Destinações AVAILABLE que criaram movimento; não é o estoque atual.","/Inventory"),Q("processing","Em processamento",x.InProcessing,"Saldo legitimamente destinado a reprocessamento ou reclassificação.","/Production"),Q("loss","Perdas",x.Loss,"Destinações físicas de perda ou descarte.","/Harvest?kind=RECEIPT"),new("appropriated-cost","Custos apropriados",x.Costs,"BRL","AVAILABLE","Lançamentos de custo vinculados diretamente à safra até o corte; compra e pagamento não são somados.","/Finance",null),new("unit-cost","Custo por unidade aprovada",x.Approved>0?decimal.Round(x.Costs/x.Approved,6,MidpointRounding.AwayFromZero):null,"BRL/"+unit,x.Approved>0?"PROVISIONAL":"UNAVAILABLE","Custos apropriados ÷ quantidade aprovada; arredondamento AwayFromZero em 6 casas.","/Finance",x.Approved>0?"Não inclui componentes sem vínculo confiável com a safra.":"Quantidade aprovada zero; divisão não calculada."),new("current-stock","Estoque atual relacionado",null,unit,"UNAVAILABLE","Saldo atual exige genealogia completa por lote de origem; produção acumulada não é usada como estoque.","/Inventory","Não há atribuição exata de todos os saldos atuais à safra."),new("recognized-revenue","Receita reconhecida",null,"BRL","UNAVAILABLE","Receita exige política de reconhecimento e vínculo inequívoco com a produção desta safra.","/Commercial","Pedidos, faturamento e recebimentos permanecem distintos; vínculo exato indisponível.")];
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
    private sealed record PlanReferences(Guid FarmId,short SeasonStatus,Guid FieldFarmId,decimal FieldArea,string ProductUnit,Guid WarehouseFarmId);private sealed record PlanRow(decimal Quantity,string Unit,string Status,decimal Area);private sealed record ReceiptSource(decimal Quantity,string Unit,Guid ProductId,Guid WarehouseId,decimal Received);private sealed record ReceiptRow(decimal Quantity,string Unit,string Status,Guid ProductId,string Reference);private sealed record SpecRow(int Version,Guid ProductId,string Status);private sealed record ParameterRow(Guid Id,bool Required,bool EvidenceRequired,decimal? Minimum,decimal? Maximum,string Name);private sealed record AllocationSource(decimal Quantity,decimal Accepted,string Unit,string Status,Guid ProductId,Guid WarehouseId,string Reference,decimal Allocated,decimal AvailableAllocated);private sealed record BalanceRow(Guid Id,decimal Available,decimal AverageCost,long Version);private sealed record DashboardRow(decimal Planned,decimal Harvested,decimal Received,decimal AwaitingQuality,decimal Approved,decimal Loss,decimal Costs,decimal Area,string? Unit,int UnitCount);private sealed record TraceRow(Guid ReceiptId,string LotNumber,string QualityStatus,Guid FarmId,Guid SeasonId,Guid FieldId,Guid HarvestRecordId,decimal ReceivedQuantity,decimal AllocatedQuantity,string Unit);private sealed record ReplayRow(Guid Id,string Hash,DateTimeOffset At);private sealed record InspectionReplay(Guid Id,string Hash,DateTimeOffset At);
}
