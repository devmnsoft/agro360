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

    public Task<FieldOrderDetail> DetailAsync(Guid orderId, CancellationToken ct) => database.InTenantTransactionAsync(async (c, t) =>
    {
        var row = await c.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("select id,module,status,created_at CreatedAt,data::text Data,version from agro360.agriculture_records where tenant_id=@TenantId and id=@Id and module='work-orders' and deleted_at is null", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct));
        if (row is null) throw new NotFoundException("Ordem de campo", orderId);
        var resources = (await c.QueryAsync(new CommandDefinition("select id,resource_type,resource_id,starts_at,ends_at,status,version from agro360.field_work_order_resources where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null order by starts_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct))).ToArray();
        var logs = (await c.QueryAsync(new CommandDefinition("select id,operator_id,equipment_id,stage,starts_at,ends_at,performed_quantity,unit,physical_area_ha,initial_meter,final_meter,interruption_minutes,interruption_reason,notes,evidence_reference,confirmed_at,version from agro360.field_work_logs where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null order by starts_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct))).ToArray();
        var materials = (await c.QueryAsync(new CommandDefinition("select id,product_id,warehouse_id,unit,planned_quantity,reserved_quantity,delivered_quantity,consumed_quantity,returned_quantity,lost_quantity,(delivered_quantity-consumed_quantity-returned_quantity-lost_quantity) team_custody_quantity,unit_cost,status,version from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null order by created_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct))).ToArray();
        var history = (await c.QueryAsync(new CommandDefinition("select from_status,to_status,reason,changed_at,changed_by from agro360.agriculture_status_history where tenant_id=@TenantId and record_id=@Id order by changed_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct))).ToArray();
        var issues = await IssuesAsync(c, t, orderId, row, ct);
        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.Data, JsonOptions) ?? [];
        var planned = Decimal(data, "estimatedCost");
        var materialCost = await c.ExecuteScalarAsync<decimal?>(new CommandDefinition("select sum((consumed_quantity+lost_quantity)*unit_cost) from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null and unit_cost is not null", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct));
        var hasUnknownMaterialCost = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null and unit_cost is null)", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct));
        var pending = hasUnknownMaterialCost ? new[] { "Há material sem custo vigente." } : Array.Empty<string>();
        var actual = pending.Length == 0 ? materialCost : null;
        return new FieldOrderDetail(new AgricultureRecord(row.Id, row.Module, row.Status, row.CreatedAt, data), row.Version, resources, logs, materials, history, issues,
            new FieldCostSummary(planned, materialCost, null, null, null, null, actual, planned is not null && actual is not null ? actual - planned : null, pending));
    }, ct);

    public Task AddResourceAsync(Guid orderId, FieldResourceCommand x, CancellationToken ct)
    {
        var type = x.ResourceType.Trim().ToUpperInvariant();
        if (type is not ("PERSON" or "EQUIPMENT") || x.EndsAt <= x.StartsAt) throw new DomainException("Recurso ou período de reserva inválido.", "agriculture.resource_invalid");
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            await RequireEditableOrder(c, t, orderId, ct);
            var exists = type == "PERSON"
                ? await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.identity_users where tenant_id=@TenantId and id=@ResourceId and status='ACTIVE' and deleted_at is null)", new { tenant.TenantId, x.ResourceId }, t, cancellationToken: ct))
                : await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.fleet_assets where tenant_id=@TenantId and id=@ResourceId and deleted_at is null and cadastral_status='ACTIVE' and status not in('INACTIVE','MAINTENANCE','BLOCKED','WRITTEN_OFF','SOLD'))", new { tenant.TenantId, x.ResourceId }, t, cancellationToken: ct));
            if (!exists) throw new ConflictException("Recurso inexistente, inativo, bloqueado ou em manutenção.", "agriculture.resource_unavailable");
            var conflict = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_order_resources where tenant_id=@TenantId and resource_type=@Type and resource_id=@ResourceId and status='RESERVED' and deleted_at is null and starts_at<@EndsAt and ends_at>@StartsAt)", new { tenant.TenantId, Type = type, x.ResourceId, x.StartsAt, x.EndsAt }, t, cancellationToken: ct));
            if (conflict) throw new ConflictException("O recurso já está reservado em parte deste período.", "agriculture.resource_overlap");
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_work_order_resources(id,tenant_id,work_order_id,resource_type,resource_id,starts_at,ends_at,created_by,updated_by) values(@Id,@TenantId,@OrderId,@Type,@ResourceId,@StartsAt,@EndsAt,@UserId,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = orderId, Type = type, x.ResourceId, x.StartsAt, x.EndsAt, tenant.UserId }, t, cancellationToken: ct));
        }, ct);
    }

    public Task AddWorkLogAsync(Guid orderId, FieldWorkLogCommand x, CancellationToken ct)
    {
        if (x.EndsAt <= x.StartsAt || x.PerformedQuantity < 0 || x.PhysicalAreaHa < 0 || x.InitialMeter < 0 || x.FinalMeter < x.InitialMeter || x.InterruptionMinutes < 0 || string.IsNullOrWhiteSpace(x.IdempotencyKey)) throw new DomainException("Revise duração, quantidades, medidor e chave da requisição.", "agriculture.work_log_invalid");
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            var status = await RequireOrder(c, t, orderId, ct);
            if (status is not ("IN_PROGRESS" or "PAUSED" or "AWAITING_REVIEW")) throw new ConflictException("A ordem precisa estar em execução ou aguardando conferência.");
            var overlap = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_logs where tenant_id=@TenantId and deleted_at is null and ((operator_id=@OperatorId) or (@EquipmentId is not null and equipment_id=@EquipmentId)) and starts_at<@EndsAt and ends_at>@StartsAt)", new { tenant.TenantId, x.OperatorId, x.EquipmentId, x.StartsAt, x.EndsAt }, t, cancellationToken: ct));
            if (overlap) throw new ConflictException("Operador ou equipamento possui apontamento sobreposto.", "agriculture.work_log_overlap");
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_work_logs(id,tenant_id,work_order_id,operator_id,equipment_id,stage,starts_at,ends_at,performed_quantity,unit,physical_area_ha,initial_meter,final_meter,interruption_minutes,interruption_reason,notes,evidence_reference,idempotency_key,created_by,updated_by) values(@Id,@TenantId,@OrderId,@OperatorId,@EquipmentId,@Stage,@StartsAt,@EndsAt,@PerformedQuantity,@Unit,@PhysicalAreaHa,@InitialMeter,@FinalMeter,@InterruptionMinutes,@InterruptionReason,@Notes,@EvidenceReference,@IdempotencyKey,@UserId,@UserId) on conflict(tenant_id,idempotency_key) do nothing", new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = orderId, x.OperatorId, x.EquipmentId, Stage = x.Stage.Trim(), x.StartsAt, x.EndsAt, x.PerformedQuantity, Unit = x.Unit.Trim().ToLowerInvariant(), x.PhysicalAreaHa, x.InitialMeter, x.FinalMeter, x.InterruptionMinutes, x.InterruptionReason, x.Notes, x.EvidenceReference, x.IdempotencyKey, tenant.UserId }, t, cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition("update agro360.agriculture_plan_operations o set status='PARTIAL',updated_at=now(),updated_by=@UserId,version=version+1 from agro360.agriculture_operation_orders l where l.tenant_id=o.tenant_id and l.operation_id=o.id and l.work_order_id=@OrderId and o.tenant_id=@TenantId and o.status='PLANNED'", new { tenant.TenantId, OrderId = orderId, tenant.UserId }, t, cancellationToken: ct));
        }, ct);
    }

    public Task AddMaterialAsync(Guid orderId, FieldMaterialCommand x, CancellationToken ct)
    {
        if (x.PlannedQuantity <= 0 || x.UnitCost < 0) throw new DomainException("Quantidade planejada e custo são inválidos.", "agriculture.material_invalid");
        return database.InTenantTransactionAsync(async (c, t) => { await RequireEditableOrder(c, t, orderId, ct); await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_work_order_materials(id,tenant_id,work_order_id,product_id,warehouse_id,unit,planned_quantity,unit_cost,created_by,updated_by) select @Id,@TenantId,@OrderId,p.id,@WarehouseId,p.base_unit,@Quantity,@UnitCost,@UserId,@UserId from agro360.inventory_products p where p.tenant_id=@TenantId and p.id=@ProductId and p.deleted_at is null on conflict(tenant_id,work_order_id,product_id,warehouse_id) do update set planned_quantity=excluded.planned_quantity,unit_cost=excluded.unit_cost,updated_at=now(),updated_by=excluded.updated_by,version=agro360.field_work_order_materials.version+1", new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = orderId, x.ProductId, x.WarehouseId, Quantity = x.PlannedQuantity, x.UnitCost, tenant.UserId }, t, cancellationToken: ct)); }, ct);
    }

    public Task ApplyMaterialEventAsync(Guid orderId, Guid materialId, FieldMaterialEventCommand x, CancellationToken ct) => database.InTenantTransactionAsync(async (c, t) =>
    {
        if (x.Quantity <= 0 || string.IsNullOrWhiteSpace(x.IdempotencyKey)) throw new DomainException("Quantidade e chave da requisição são obrigatórias.", "agriculture.material_event_invalid");
        var kind = x.EventType.Trim().ToUpperInvariant(); if (kind is not ("RESERVE" or "DELIVER" or "CONSUME" or "RETURN" or "LOSS" or "RELEASE")) throw new DomainException("Evento de material inválido.");
        if (kind == "LOSS" && string.IsNullOrWhiteSpace(x.Reason)) throw new DomainException("Informe o motivo e responsável pela perda.");
        var m = await c.QuerySingleOrDefaultAsync<MaterialRow>(new CommandDefinition("select * from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@OrderId and id=@Id and deleted_at is null for update", new { tenant.TenantId, OrderId = orderId, Id = materialId }, t, cancellationToken: ct));
        if (m is null) throw new NotFoundException("Material da ordem", materialId);
        if (await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_material_events where tenant_id=@TenantId and idempotency_key=@Key)", new { tenant.TenantId, Key = x.IdempotencyKey }, t, cancellationToken: ct))) return;
        if (kind == "RESERVE" && m.ReservedQuantity + x.Quantity > m.PlannedQuantity) throw new ConflictException("A reserva supera o previsto; revise o planejamento.");
        if (kind == "DELIVER" && m.DeliveredQuantity + x.Quantity > m.ReservedQuantity) throw new ConflictException("A entrega supera a quantidade reservada.");
        if (kind is "CONSUME" or "LOSS" && m.ConsumedQuantity + m.ReturnedQuantity + m.LostQuantity + x.Quantity > m.DeliveredQuantity) throw new ConflictException("A baixa supera o material entregue à equipe.");
        if (kind == "RETURN" && (x.SourceEventId is null || m.ConsumedQuantity + m.ReturnedQuantity + m.LostQuantity + x.Quantity > m.DeliveredQuantity)) throw new ConflictException("A devolução exige entrega de origem e saldo devolvível.");
        Guid? movement = null;
        if (kind is "CONSUME" or "LOSS" or "RETURN") { if (m.WarehouseId is null) throw new ConflictException("Defina o depósito antes de movimentar estoque."); var qty = kind == "RETURN" ? x.Quantity : -x.Quantity; movement = await c.ExecuteScalarAsync<Guid>(new CommandDefinition("select agro360.inventory_apply_stock_movement(@TenantId,@WarehouseId,@ProductId,@Quantity,coalesce(@UnitCost,0),@Type,@Reference,null,null,@UserId,@Reason)", new { tenant.TenantId, m.WarehouseId, m.ProductId, Quantity = qty, m.UnitCost, Type = kind == "RETURN" ? "ENTRY" : "EXIT", Reference = orderId, tenant.UserId, x.Reason }, t, cancellationToken: ct)); }
        var column = kind switch { "RESERVE" => "reserved_quantity", "DELIVER" => "delivered_quantity", "CONSUME" => "consumed_quantity", "RETURN" => "returned_quantity", "LOSS" => "lost_quantity", _ => "reserved_quantity" }; var delta = kind == "RELEASE" ? -x.Quantity : x.Quantity;
        await c.ExecuteAsync(new CommandDefinition($"update agro360.field_work_order_materials set {column}={column}+@Delta,updated_at=now(),updated_by=@UserId,version=version+1,status=case when delivered_quantity-consumed_quantity-returned_quantity-lost_quantity=0 and delivered_quantity>0 then 'SETTLED' when @Kind='DELIVER' then 'DELIVERED' when @Kind='RESERVE' then 'RESERVED' else status end where tenant_id=@TenantId and id=@Id", new { Delta = delta, tenant.UserId, Kind = kind, tenant.TenantId, Id = materialId }, t, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_material_events(id,tenant_id,work_order_material_id,event_type,quantity,reason,inventory_movement_id,source_event_id,idempotency_key,created_by) values(@Id,@TenantId,@MaterialId,@Kind,@Quantity,@Reason,@Movement,@SourceEventId,@Key,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, MaterialId = materialId, Kind = kind, x.Quantity, x.Reason, Movement = movement, x.SourceEventId, Key = x.IdempotencyKey, tenant.UserId }, t, cancellationToken: ct));
    }, ct);

    public Task ReviewAsync(Guid orderId, FieldReviewCommand x, CancellationToken ct) => database.InTenantTransactionAsync(async (c, t) =>
    {
        var row = await c.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("select id,module,status,created_at CreatedAt,data::text Data,version from agro360.agriculture_records where tenant_id=@TenantId and id=@Id and module='work-orders' and deleted_at is null for update", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct));
        if (row is null) throw new NotFoundException("Ordem de campo", orderId); if (row.Version != x.Version) throw new ConflictException("A ordem foi alterada. Recarregue e revise as mudanças antes de concluir.", "agriculture.version_conflict");
        var issues = await IssuesAsync(c, t, orderId, row, ct); if (issues.Any(i => i.Severity == "BLOCKER")) throw new ConflictException("Resolva os bloqueios obrigatórios antes de concluir.", "agriculture.review_blocked");
        var changed = await c.ExecuteAsync(new CommandDefinition("update agro360.agriculture_records set status='COMPLETED',updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and id=@Id and version=@Version and status='AWAITING_REVIEW'", new { tenant.TenantId, Id = orderId, x.Version, tenant.UserId }, t, cancellationToken: ct)); if (changed != 1) throw new ConflictException("Somente uma conferência pode concluir a ordem.");
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.field_work_order_reviews(id,tenant_id,work_order_id,order_version,outcome,summary,notes,created_by) values(@Id,@TenantId,@OrderId,@Version,'COMPLETED',cast(@Summary as jsonb),@Notes,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = orderId, x.Version, Summary = JsonSerializer.Serialize(issues, JsonOptions), x.Notes, tenant.UserId }, t, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("""
            update agro360.agriculture_plan_operations o set status='COMPLETED',updated_at=now(),updated_by=@UserId,version=version+1
            where o.tenant_id=@TenantId and exists(select 1 from agro360.agriculture_operation_orders l where l.tenant_id=o.tenant_id and l.operation_id=o.id and l.work_order_id=@OrderId)
              and not exists(select 1 from agro360.agriculture_operation_orders l join agro360.agriculture_records r on r.tenant_id=l.tenant_id and r.id=l.work_order_id where l.tenant_id=o.tenant_id and l.operation_id=o.id and r.status<>'COMPLETED')
            """, new { tenant.TenantId, OrderId = orderId, tenant.UserId }, t, cancellationToken: ct));
    }, ct);

    private async Task<IReadOnlyCollection<FieldIssue>> IssuesAsync(System.Data.IDbConnection c, System.Data.IDbTransaction t, Guid id, OrderRow row, CancellationToken ct)
    {
        var list = new List<FieldIssue>(); var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.Data, JsonOptions) ?? [];
        if (!data.ContainsKey("responsibleId") || data["responsibleId"] is null) list.Add(new("BLOCKER", "RESPONSIBLE_REQUIRED", "Defina o responsável pela ordem.", "planning"));
        if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_logs where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null)", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))) list.Add(new("BLOCKER", "WORK_LOG_REQUIRED", "Registre ao menos um apontamento confirmado.", "execution"));
        if (await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null and delivered_quantity>consumed_quantity+returned_quantity+lost_quantity)", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))) list.Add(new("BLOCKER", "MATERIAL_CUSTODY", "Há material entregue ainda sob responsabilidade da equipe.", "materials"));
        return list;
    }
    private async Task<string> RequireOrder(System.Data.IDbConnection c, System.Data.IDbTransaction t, Guid id, CancellationToken ct) => await c.ExecuteScalarAsync<string?>(new CommandDefinition("select status from agro360.agriculture_records where tenant_id=@TenantId and id=@Id and module='work-orders' and deleted_at is null", new { tenant.TenantId, Id = id }, t, cancellationToken: ct)) ?? throw new NotFoundException("Ordem de campo", id);
    private async Task RequireEditableOrder(System.Data.IDbConnection c, System.Data.IDbTransaction t, Guid id, CancellationToken ct) { if (await RequireOrder(c, t, id, ct) is not ("OPEN" or "PLANNED" or "AWAITING_RESOURCES")) throw new ConflictException("A programação está bloqueada no estado atual."); }
    private static decimal? Decimal(Dictionary<string, object?> data, string key) => data.TryGetValue(key, out var value) && value is JsonElement e && e.ValueKind == JsonValueKind.Number && e.TryGetDecimal(out var number) ? number : null;
    private sealed record OrderRow(Guid Id, string Module, string Status, DateTimeOffset CreatedAt, string Data, long Version);
    private sealed record MaterialRow(Guid Id, Guid ProductId, Guid? WarehouseId, decimal PlannedQuantity, decimal ReservedQuantity, decimal DeliveredQuantity, decimal ConsumedQuantity, decimal ReturnedQuantity, decimal LostQuantity, decimal? UnitCost);
}
