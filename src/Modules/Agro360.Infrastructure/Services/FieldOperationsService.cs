using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class FieldOperationsService(DatabaseExecutor database, ITenantContext tenant) : IFieldOperationsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyCollection<string> UnknownMaterialCostIssue = Array.AsReadOnly(new[] { "Há material sem custo vigente." });

    public Task<FieldOrderDetail> DetailAsync(Guid orderId, CancellationToken cancellationToken) => database.InTenantTransactionAsync<FieldOrderDetail>(async (c, t) =>
    {
        var row = await c.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("select id,module,status,created_at CreatedAt,data::text Data,version from agro360.agriculture_records where tenant_id=@TenantId and id=@Id and module='work-orders' and deleted_at is null", new { tenant.TenantId, Id = orderId }, t, cancellationToken: cancellationToken));
        if (row is null) throw new NotFoundException("Ordem de campo", orderId);
        var resources = (await c.QueryAsync(new CommandDefinition("select id,resource_type,resource_id,starts_at,ends_at,status,version from agro360.field_work_order_resources where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null order by starts_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: cancellationToken))).ToArray();
        var logs = (await c.QueryAsync(new CommandDefinition("select id,operator_id,equipment_id,stage,starts_at,ends_at,performed_quantity,unit,physical_area_ha,initial_meter,final_meter,interruption_minutes,interruption_reason,notes,evidence_reference,confirmed_at,version from agro360.field_work_logs where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null order by starts_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: cancellationToken))).ToArray();
        var materials = (await c.QueryAsync(new CommandDefinition("select id,product_id,warehouse_id,unit,planned_quantity,reserved_quantity,delivered_quantity,consumed_quantity,returned_quantity,lost_quantity,(delivered_quantity-consumed_quantity-returned_quantity-lost_quantity) team_custody_quantity,unit_cost,status,version from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null order by created_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: cancellationToken))).ToArray();
        var materialHistory = (await c.QueryAsync(new CommandDefinition("select e.id,e.work_order_material_id,e.event_type,e.quantity,e.reason,e.inventory_movement_id,e.source_event_id,e.created_at,e.created_by from agro360.field_material_events e join agro360.field_work_order_materials m on m.tenant_id=e.tenant_id and m.id=e.work_order_material_id where e.tenant_id=@TenantId and m.work_order_id=@Id order by e.created_at,e.id", new { tenant.TenantId, Id = orderId }, t, cancellationToken: cancellationToken))).ToArray();
        var history = (await c.QueryAsync(new CommandDefinition("select from_status,to_status,reason,changed_at,changed_by from agro360.agriculture_status_history where tenant_id=@TenantId and record_id=@Id order by changed_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: cancellationToken))).ToArray();
        var issues = await IssuesAsync(c, t, orderId, row, cancellationToken);
        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.Data, JsonOptions) ?? [];
        var planned = Decimal(data, "estimatedCost");
        var materialCost = await c.ExecuteScalarAsync<decimal?>(new CommandDefinition("select sum((consumed_quantity+lost_quantity)*unit_cost) from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null and unit_cost is not null", new { tenant.TenantId, Id = orderId }, t, cancellationToken: cancellationToken));
        var hasUnknownMaterialCost = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null and unit_cost is null)", new { tenant.TenantId, Id = orderId }, t, cancellationToken: cancellationToken));
        var pending = hasUnknownMaterialCost ? UnknownMaterialCostIssue : [];
        var actual = pending.Count == 0 ? materialCost : null;
        return new FieldOrderDetail(new AgricultureRecord(row.Id, row.Module, row.Status, row.CreatedAt, data), row.Version, resources, logs, materials, materialHistory, history, issues,
            new FieldCostSummary(planned, materialCost, null, null, null, null, actual, planned is not null && actual is not null ? actual - planned : null, pending));
    }, cancellationToken);

    public Task AddResourceAsync(Guid orderId, FieldResourceCommand command, CancellationToken cancellationToken)
    {
        var type = command.ResourceType.Trim().ToUpperInvariant();
        if (type is not ("PERSON" or "EQUIPMENT") || command.EndsAt <= command.StartsAt) throw new DomainException("Recurso ou período de reserva inválido.", "agriculture.resource_invalid");
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            await RequireEditableOrder(c, t, orderId, cancellationToken);
            var exists = type == "PERSON"
                ? await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.identity_users where tenant_id=@TenantId and id=@ResourceId and status='ACTIVE' and deleted_at is null)", new { tenant.TenantId, command.ResourceId }, t, cancellationToken: cancellationToken))
                : await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.fleet_assets where tenant_id=@TenantId and id=@ResourceId and deleted_at is null and cadastral_status='ACTIVE' and status not in('INACTIVE','MAINTENANCE','BLOCKED','WRITTEN_OFF','SOLD'))", new { tenant.TenantId, command.ResourceId }, t, cancellationToken: cancellationToken));
            if (!exists) throw new ConflictException("Recurso inexistente, inativo, bloqueado ou em manutenção.", "agriculture.resource_unavailable");
            var conflict = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_order_resources where tenant_id=@TenantId and resource_type=@Type and resource_id=@ResourceId and status='RESERVED' and deleted_at is null and starts_at<@EndsAt and ends_at>@StartsAt)", new { tenant.TenantId, Type = type, command.ResourceId, command.StartsAt, command.EndsAt }, t, cancellationToken: cancellationToken));
            if (conflict) throw new ConflictException("O recurso já está reservado em parte deste período.", "agriculture.resource_overlap");
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_work_order_resources(id,tenant_id,work_order_id,resource_type,resource_id,starts_at,ends_at,created_by,updated_by) values(@Id,@TenantId,@OrderId,@Type,@ResourceId,@StartsAt,@EndsAt,@UserId,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = orderId, Type = type, command.ResourceId, command.StartsAt, command.EndsAt, tenant.UserId }, t, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public Task AddWorkLogAsync(Guid orderId, FieldWorkLogCommand command, CancellationToken cancellationToken)
    {
        if (command.EndsAt <= command.StartsAt || command.PerformedQuantity < 0 || command.PhysicalAreaHa < 0 || command.InitialMeter < 0 || command.FinalMeter < command.InitialMeter || command.InterruptionMinutes < 0 || string.IsNullOrWhiteSpace(command.IdempotencyKey)) throw new DomainException("Revise duração, quantidades, medidor e chave da requisição.", "agriculture.work_log_invalid");
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            var status = await RequireOrder(c, t, orderId, cancellationToken);
            if (status is not ("IN_PROGRESS" or "PAUSED" or "AWAITING_REVIEW")) throw new ConflictException("A ordem precisa estar em execução ou aguardando conferência.");
            var overlap = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_logs where tenant_id=@TenantId and deleted_at is null and ((operator_id=@OperatorId) or (@EquipmentId is not null and equipment_id=@EquipmentId)) and starts_at<@EndsAt and ends_at>@StartsAt)", new { tenant.TenantId, command.OperatorId, command.EquipmentId, command.StartsAt, command.EndsAt }, t, cancellationToken: cancellationToken));
            if (overlap) throw new ConflictException("Operador ou equipamento possui apontamento sobreposto.", "agriculture.work_log_overlap");
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_work_logs(id,tenant_id,work_order_id,operator_id,equipment_id,stage,starts_at,ends_at,performed_quantity,unit,physical_area_ha,initial_meter,final_meter,interruption_minutes,interruption_reason,notes,evidence_reference,idempotency_key,created_by,updated_by) values(@Id,@TenantId,@OrderId,@OperatorId,@EquipmentId,@Stage,@StartsAt,@EndsAt,@PerformedQuantity,@Unit,@PhysicalAreaHa,@InitialMeter,@FinalMeter,@InterruptionMinutes,@InterruptionReason,@Notes,@EvidenceReference,@IdempotencyKey,@UserId,@UserId) on conflict(tenant_id,idempotency_key) do nothing", new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = orderId, command.OperatorId, command.EquipmentId, Stage = command.Stage.Trim(), command.StartsAt, command.EndsAt, command.PerformedQuantity, Unit = command.Unit.Trim().ToLowerInvariant(), command.PhysicalAreaHa, command.InitialMeter, command.FinalMeter, command.InterruptionMinutes, command.InterruptionReason, command.Notes, command.EvidenceReference, command.IdempotencyKey, tenant.UserId }, t, cancellationToken: cancellationToken));
            await c.ExecuteAsync(new CommandDefinition("update agro360.agriculture_plan_operations o set status='PARTIAL',updated_at=now(),updated_by=@UserId,version=version+1 from agro360.agriculture_operation_orders l where l.tenant_id=o.tenant_id and l.operation_id=o.id and l.work_order_id=@OrderId and o.tenant_id=@TenantId and o.status='PLANNED'", new { tenant.TenantId, OrderId = orderId, tenant.UserId }, t, cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public Task AddMaterialAsync(Guid orderId, FieldMaterialCommand command, CancellationToken cancellationToken)
    {
        if (command.PlannedQuantity <= 0 || command.UnitCost < 0) throw new DomainException("Quantidade planejada e custo são inválidos.", "agriculture.material_invalid");
        return database.InTenantTransactionAsync(async (c, t) => { await RequireEditableOrder(c, t, orderId, cancellationToken); await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_work_order_materials(id,tenant_id,work_order_id,product_id,warehouse_id,unit,planned_quantity,unit_cost,created_by,updated_by) select @Id,@TenantId,@OrderId,p.id,@WarehouseId,p.base_unit,@Quantity,@UnitCost,@UserId,@UserId from agro360.inventory_products p where p.tenant_id=@TenantId and p.id=@ProductId and p.deleted_at is null on conflict(tenant_id,work_order_id,product_id,warehouse_id) do update set planned_quantity=excluded.planned_quantity,unit_cost=excluded.unit_cost,updated_at=now(),updated_by=excluded.updated_by,version=agro360.field_work_order_materials.version+1", new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = orderId, command.ProductId, command.WarehouseId, Quantity = command.PlannedQuantity, command.UnitCost, tenant.UserId }, t, cancellationToken: cancellationToken)); }, cancellationToken);
    }

    public Task ApplyMaterialEventAsync(Guid orderId, Guid materialId, FieldMaterialEventCommand command, CancellationToken cancellationToken) => database.InTenantTransactionAsync(async (c, t) =>
    {
        if (command.Quantity <= 0 || string.IsNullOrWhiteSpace(command.IdempotencyKey)) throw new DomainException("Quantidade e chave da requisição são obrigatórias.", "agriculture.material_event_invalid");
        var kind = command.EventType.Trim().ToUpperInvariant(); if (kind is not ("RESERVE" or "DELIVER" or "CONSUME" or "RETURN" or "LOSS" or "RELEASE" or "REVERSAL")) throw new DomainException("Evento de material inválido.");
        if (kind is "LOSS" or "REVERSAL" && string.IsNullOrWhiteSpace(command.Reason)) throw new DomainException("Informe uma justificativa para esta movimentação.");
        var orderStatus = await RequireOrder(c, t, orderId, cancellationToken);
        if (orderStatus is "COMPLETED" or "CANCELLED") throw new ConflictException("Uma ordem consolidada não aceita novas movimentações de material.", "agriculture.material_order_closed");
        if ((kind is "CONSUME" or "LOSS" or "REVERSAL") && orderStatus is not ("IN_PROGRESS" or "PAUSED" or "AWAITING_REVIEW")) throw new ConflictException("Consumo e estorno exigem uma ordem em execução ou conferência.", "agriculture.material_order_not_running");
        var m = await c.QuerySingleOrDefaultAsync<MaterialRow>(new CommandDefinition("select * from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@OrderId and id=@Id and deleted_at is null for update", new { tenant.TenantId, OrderId = orderId, Id = materialId }, t, cancellationToken: cancellationToken));
        if (m is null) throw new NotFoundException("Material da ordem", materialId);
        var repeated = await c.QuerySingleOrDefaultAsync<MaterialEventRow>(new CommandDefinition("select work_order_material_id WorkOrderMaterialId,event_type EventType,quantity,reason,source_event_id SourceEventId from agro360.field_material_events where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: cancellationToken));
        if (repeated is not null) { if (repeated.WorkOrderMaterialId != materialId || repeated.EventType != kind || repeated.Quantity != command.Quantity || repeated.Reason != command.Reason || repeated.SourceEventId != command.SourceEventId) throw new ConflictException("A chave de idempotência já foi usada com outro conteúdo.", "agriculture.idempotency_payload_conflict"); return; }
        if (kind == "RESERVE" && m.ReservedQuantity + command.Quantity > m.PlannedQuantity) throw new ConflictException("A reserva supera o previsto; revise o planejamento.");
        if (kind == "DELIVER" && m.DeliveredQuantity + command.Quantity > m.ReservedQuantity) throw new ConflictException("A entrega supera a quantidade reservada.");
        if (kind is "CONSUME" or "LOSS" && m.ConsumedQuantity + m.ReturnedQuantity + m.LostQuantity + command.Quantity > m.DeliveredQuantity) throw new ConflictException("A baixa supera o material entregue à equipe.");
        if (kind == "RETURN" && (command.SourceEventId is null || m.ConsumedQuantity + m.ReturnedQuantity + m.LostQuantity + command.Quantity > m.DeliveredQuantity)) throw new ConflictException("A devolução exige entrega de origem e saldo devolvível.");
        MaterialEventRow? source = null;
        if (kind == "REVERSAL")
        {
            if (command.SourceEventId is null) throw new DomainException("O estorno deve referenciar o consumo original.", "agriculture.reversal_source_required");
            source = await c.QuerySingleOrDefaultAsync<MaterialEventRow>(new CommandDefinition("select work_order_material_id WorkOrderMaterialId,event_type EventType,quantity,reason,source_event_id SourceEventId from agro360.field_material_events where tenant_id=@TenantId and id=@Id for update", new { tenant.TenantId, Id = command.SourceEventId }, t, cancellationToken: cancellationToken));
            if (source is null || source.WorkOrderMaterialId != materialId || source.EventType != "CONSUME" || source.Quantity != command.Quantity) throw new ConflictException("O estorno deve corresponder integralmente a um consumo desta operação.", "agriculture.reversal_source_invalid");
            if (await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_material_events where tenant_id=@TenantId and source_event_id=@Id and event_type='REVERSAL')", new { tenant.TenantId, Id = command.SourceEventId }, t, cancellationToken: cancellationToken))) throw new ConflictException("Este consumo já foi estornado.", "agriculture.reversal_duplicate");
        }
        Guid? movement = null;
        if (kind is "CONSUME" or "LOSS" or "RETURN" or "REVERSAL") { if (m.WarehouseId is null) throw new ConflictException("Defina o depósito antes de movimentar estoque."); var qty = kind is "RETURN" or "REVERSAL" ? command.Quantity : -command.Quantity; movement = await c.ExecuteScalarAsync<Guid>(new CommandDefinition("select agro360.inventory_apply_stock_movement(@TenantId,@WarehouseId,@ProductId,@Quantity,coalesce(@UnitCost,0),@Type,@Reference,null,null,@UserId,@Reason)", new { tenant.TenantId, m.WarehouseId, m.ProductId, Quantity = qty, m.UnitCost, Type = kind is "RETURN" or "REVERSAL" ? "ENTRY" : "EXIT", Reference = orderId, tenant.UserId, command.Reason }, t, cancellationToken: cancellationToken)); }
        var column = kind switch { "RESERVE" => "reserved_quantity", "DELIVER" => "delivered_quantity", "CONSUME" => "consumed_quantity", "RETURN" => "returned_quantity", "LOSS" => "lost_quantity", _ => "reserved_quantity" }; var delta = kind == "RELEASE" ? -command.Quantity : command.Quantity;
        if (kind == "REVERSAL") { column = "consumed_quantity"; delta = -command.Quantity; }
        await c.ExecuteAsync(new CommandDefinition($"update agro360.field_work_order_materials set {column}={column}+@Delta,updated_at=now(),updated_by=@UserId,version=version+1,status=case when @Kind='REVERSAL' then 'DELIVERED' when delivered_quantity-consumed_quantity-returned_quantity-lost_quantity-@Delta=0 and delivered_quantity>0 then 'SETTLED' when @Kind='DELIVER' then 'DELIVERED' when @Kind='RESERVE' then 'RESERVED' else status end where tenant_id=@TenantId and id=@Id", new { Delta = delta, tenant.UserId, Kind = kind, tenant.TenantId, Id = materialId }, t, cancellationToken: cancellationToken));
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_material_events(id,tenant_id,work_order_material_id,event_type,quantity,reason,inventory_movement_id,source_event_id,idempotency_key,created_by) values(@Id,@TenantId,@MaterialId,@Kind,@Quantity,@Reason,@Movement,@SourceEventId,@Key,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, MaterialId = materialId, Kind = kind, command.Quantity, command.Reason, Movement = movement, command.SourceEventId, Key = command.IdempotencyKey, tenant.UserId }, t, cancellationToken: cancellationToken));
    }, cancellationToken);

    public Task ReviewAsync(Guid orderId, FieldReviewCommand command, CancellationToken cancellationToken) => database.InTenantTransactionAsync(async (c, t) =>
    {
        var row = await c.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("select id,module,status,created_at CreatedAt,data::text Data,version from agro360.agriculture_records where tenant_id=@TenantId and id=@Id and module='work-orders' and deleted_at is null for update", new { tenant.TenantId, Id = orderId }, t, cancellationToken: cancellationToken));
        if (row is null) throw new NotFoundException("Ordem de campo", orderId); if (row.Version != command.Version) throw new ConflictException("A ordem foi alterada. Recarregue e revise as mudanças antes de concluir.", "agriculture.version_conflict");
        var issues = await IssuesAsync(c, t, orderId, row, cancellationToken); if (issues.Any(i => i.Severity == "BLOCKER")) throw new ConflictException("Resolva os bloqueios obrigatórios antes de concluir.", "agriculture.review_blocked");
        var changed = await c.ExecuteAsync(new CommandDefinition("update agro360.agriculture_records set status='COMPLETED',updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and id=@Id and version=@Version and status='AWAITING_REVIEW'", new { tenant.TenantId, Id = orderId, command.Version, tenant.UserId }, t, cancellationToken: cancellationToken)); if (changed != 1) throw new ConflictException("Somente uma conferência pode concluir a ordem.");
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_work_order_reviews(id,tenant_id,work_order_id,order_version,outcome,summary,notes,created_by) values(@Id,@TenantId,@OrderId,@Version,'COMPLETED',cast(@Summary as jsonb),@Notes,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = orderId, command.Version, Summary = JsonSerializer.Serialize(issues, JsonOptions), command.Notes, tenant.UserId }, t, cancellationToken: cancellationToken));
        await c.ExecuteAsync(new CommandDefinition("""
            update agro360.agriculture_plan_operations o set status='COMPLETED',updated_at=now(),updated_by=@UserId,version=version+1
            where o.tenant_id=@TenantId and exists(select 1 from agro360.agriculture_operation_orders l where l.tenant_id=o.tenant_id and l.operation_id=o.id and l.work_order_id=@OrderId)
              and not exists(select 1 from agro360.agriculture_operation_orders l join agro360.agriculture_records r on r.tenant_id=l.tenant_id and r.id=l.work_order_id where l.tenant_id=o.tenant_id and l.operation_id=o.id and r.status<>'COMPLETED')
            """, new { tenant.TenantId, OrderId = orderId, tenant.UserId }, t, cancellationToken: cancellationToken));
    }, cancellationToken);

    private async Task<IReadOnlyCollection<FieldIssue>> IssuesAsync(System.Data.IDbConnection c, System.Data.IDbTransaction t, Guid id, OrderRow row, CancellationToken cancellationToken)
    {
        var list = new List<FieldIssue>(); var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.Data, JsonOptions) ?? [];
        if (!data.TryGetValue("responsibleId", out var responsibleId) || responsibleId is null) list.Add(new("BLOCKER", "RESPONSIBLE_REQUIRED", "Defina o responsável pela ordem.", "planning"));
        if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_logs where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null)", new { tenant.TenantId, Id = id }, t, cancellationToken: cancellationToken))) list.Add(new("BLOCKER", "WORK_LOG_REQUIRED", "Registre ao menos um apontamento confirmado.", "execution"));
        if (await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null and delivered_quantity>consumed_quantity+returned_quantity+lost_quantity)", new { tenant.TenantId, Id = id }, t, cancellationToken: cancellationToken))) list.Add(new("BLOCKER", "MATERIAL_CUSTODY", "Há material entregue ainda sob responsabilidade da equipe.", "materials"));
        return list;
    }
    private async Task<string> RequireOrder(System.Data.IDbConnection c, System.Data.IDbTransaction t, Guid id, CancellationToken cancellationToken) => await c.ExecuteScalarAsync<string?>(new CommandDefinition("select status from agro360.agriculture_records where tenant_id=@TenantId and id=@Id and module='work-orders' and deleted_at is null", new { tenant.TenantId, Id = id }, t, cancellationToken: cancellationToken)) ?? throw new NotFoundException("Ordem de campo", id);
    private async Task RequireEditableOrder(System.Data.IDbConnection c, System.Data.IDbTransaction t, Guid id, CancellationToken cancellationToken) { if (await RequireOrder(c, t, id, cancellationToken) is not ("OPEN" or "PLANNED" or "AWAITING_RESOURCES")) throw new ConflictException("A programação está bloqueada no estado atual."); }
    private static decimal? Decimal(Dictionary<string, object?> data, string key) => data.TryGetValue(key, out var value) && value is JsonElement e && e.ValueKind == JsonValueKind.Number && e.TryGetDecimal(out var number) ? number : null;
    private sealed record OrderRow(Guid Id, string Module, string Status, DateTimeOffset CreatedAt, string Data, long Version);
    private sealed record MaterialRow(Guid Id, Guid ProductId, Guid? WarehouseId, decimal PlannedQuantity, decimal ReservedQuantity, decimal DeliveredQuantity, decimal ConsumedQuantity, decimal ReturnedQuantity, decimal LostQuantity, decimal? UnitCost);
    private sealed record MaterialEventRow(Guid WorkOrderMaterialId, string EventType, decimal Quantity, string? Reason, Guid? SourceEventId);
}
