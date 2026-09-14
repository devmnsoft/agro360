using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class HarvestService(DatabaseExecutor database, ITenantContext tenant) : IHarvestService
{
    private static readonly string[] AllowedAllocationDestinations =
        ["AVAILABLE", "QUARANTINE", "RECLASSIFICATION", "REPROCESSING", "RETURN_TO_ORIGIN", "LOSS", "DISPOSAL"];
    private static readonly string[] AllowedOperationKinds = ["PLAN", "HARVEST", "RECEIPT"];

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

    public Task<HarvestOperationDto> ReceiveAsync(ReceiveHarvestCommand command,CancellationToken cancellationToken)=>database.InTenantTransactionAsync(async(db,tx)=>
    {
        var key=Required(command.IdempotencyKey,nameof(command.IdempotencyKey));var hash=Hash(command);var replay=await Replay(db,tx,"production_receipts",key,hash,"RECEIPT",cancellationToken);if(replay is not null)return replay;
        var qty=Guard.Positive(command.ReceivedQuantity,nameof(command.ReceivedQuantity));var unit=Required(command.Unit,nameof(command.Unit),16).ToLowerInvariant();
        var source=await db.QuerySingleOrDefaultAsync<ReceiptSource>(new CommandDefinition("""
          select r.harvested_quantity Quantity,r.unit,p.product_id ProductId,p.destination_warehouse_id WarehouseId,r.operational_at,
          coalesce((select sum(x.received_quantity) from agro360.production_receipts x where x.tenant_id=r.tenant_id and x.harvest_record_id=r.id),0) Received
          from agro360.harvest_records r join agro360.harvest_plans p on p.tenant_id=r.tenant_id and p.id=r.plan_id
          where r.tenant_id=@TenantId and r.id=@Id and r.status<>'CANCELLED' for update of r;
          """,new{tenant.TenantId,Id=command.HarvestRecordId},tx,cancellationToken:cancellationToken))??throw new NotFoundException("Apontamento de colheita",command.HarvestRecordId);
        if(qty>source.Quantity-source.Received)throw new ConflictException("A quantidade excede o saldo ainda não recebido.","harvest.receipt_exceeds_balance");
        if(command.WarehouseId!=source.WarehouseId)throw new DomainException("O local difere do destino conferido no planejamento.","harvest.warehouse_mismatch");
        if(!string.Equals(unit,source.Unit,StringComparison.OrdinalIgnoreCase))throw new DomainException("A unidade difere da origem.","harvest.unit_mismatch");
        decimal? net=null;if(command.GrossWeight.HasValue||command.TareWeight.HasValue){if(unit is not("kg" or "t" or "g" or "arroba"))throw new DomainException("Tara só é aceita para unidade de massa.","harvest.tare_incompatible");if(!command.GrossWeight.HasValue||!command.TareWeight.HasValue||command.GrossWeight.Value<command.TareWeight.Value)throw new DomainException("O peso bruto deve ser maior ou igual à tara.","harvest.invalid_weight");net=command.GrossWeight.Value-command.TareWeight.Value;}
        var id=Guid.CreateVersion7();var remaining=source.Quantity-source.Received-qty;var status=remaining==0?"RECEIVED":"PARTIALLY_RECEIVED";
        await db.ExecuteAsync(new CommandDefinition("""
          insert into agro360.production_receipts(id,tenant_id,harvest_record_id,warehouse_id,product_id,received_at,received_quantity,unit,gross_weight,tare_weight,net_weight,lot_number,entry_mode,divergence_reason,notes,idempotency_key,request_hash,created_by)
          values(@Id,@TenantId,@HarvestId,@WarehouseId,@ProductId,@At,@Quantity,@Unit,@Gross,@Tare,@Net,@Lot,@Mode,@Divergence,@Notes,@Key,@Hash,@UserId);
          update agro360.harvest_records set status=@Status,updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and id=@HarvestId;
          """,new{Id=id,tenant.TenantId,HarvestId=command.HarvestRecordId,command.WarehouseId,source.ProductId,At=command.ReceivedAt,Quantity=qty,Unit=unit,Gross=command.GrossWeight,Tare=command.TareWeight,Net=net,Lot=Required(command.LotNumber,nameof(command.LotNumber),100),Mode=Required(command.EntryMode,nameof(command.EntryMode),16).ToUpperInvariant(),Divergence=command.DivergenceReason,command.Notes,Key=key,Hash=hash,UserId=tenant.UserId,Status=status},tx,cancellationToken:cancellationToken));
        await Audit(db,tx,"receive","ProductionReceipt",id,cancellationToken);
        return new HarvestOperationDto(id,"RECEIPT","AWAITING_INSPECTION",qty,unit,qty,command.ReceivedAt,command.LotNumber,1);
    },cancellationToken);

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
    private async Task<HarvestOperationDto?> Replay(System.Data.IDbConnection db,System.Data.IDbTransaction tx,string table,string key,string hash,string kind,CancellationToken ct){var row=await db.QuerySingleOrDefaultAsync<ReplayRow>(new CommandDefinition($"select id,request_hash Hash,created_at At from agro360.{table} where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=key},tx,cancellationToken:ct));if(row is null)return null;if(row.Hash!=hash)throw new ConflictException("A chave idempotente já foi usada com outro conteúdo.","harvest.idempotency_conflict");return new HarvestOperationDto(row.Id,kind,"REPLAYED",0,"-",0,row.At,key,1);}
    private Task<int> Audit(System.Data.IDbConnection db,System.Data.IDbTransaction tx,string action,string entity,Guid id,CancellationToken ct)=>db.ExecuteAsync(new CommandDefinition("insert into agro360.audit_logs(id,tenant_id,user_id,action,entity_type,entity_id,occurred_at) values(@AuditId,@TenantId,@UserId,@Action,@Entity,@Id,now())",new{AuditId=Guid.CreateVersion7(),tenant.TenantId,UserId=tenant.UserId,Action=action,Entity=entity,Id=id},tx,cancellationToken:ct));
    private sealed record PlanReferences(Guid FarmId,short SeasonStatus,Guid FieldFarmId,decimal FieldArea,string ProductUnit,Guid WarehouseFarmId);private sealed record PlanRow(decimal Quantity,string Unit,string Status,decimal Area);private sealed record ReceiptSource(decimal Quantity,string Unit,Guid ProductId,Guid WarehouseId,decimal Received);private sealed record ReceiptRow(decimal Quantity,string Unit,string Status,Guid ProductId,string Reference);private sealed record SpecRow(int Version,Guid ProductId,string Status);private sealed record ParameterRow(Guid Id,bool Required,bool EvidenceRequired,decimal? Minimum,decimal? Maximum,string Name);private sealed record AllocationSource(decimal Quantity,decimal Accepted,string Unit,string Status,Guid ProductId,Guid WarehouseId,string Reference,decimal Allocated,decimal AvailableAllocated);private sealed record BalanceRow(Guid Id,decimal Available,decimal AverageCost,long Version);private sealed record DashboardRow(decimal Planned,decimal Harvested,decimal Received,decimal AwaitingQuality,decimal Approved,decimal Loss,decimal Costs,decimal Area,string? Unit,int UnitCount);private sealed record TraceRow(Guid ReceiptId,string LotNumber,string QualityStatus,Guid FarmId,Guid SeasonId,Guid FieldId,Guid HarvestRecordId,decimal ReceivedQuantity,decimal AllocatedQuantity,string Unit);private sealed record ReplayRow(Guid Id,string Hash,DateTimeOffset At);private sealed record InspectionReplay(Guid Id,string Hash,DateTimeOffset At);
}
