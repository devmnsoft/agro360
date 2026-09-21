using System.Security.Cryptography;
using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.Agriculture;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class PublicTraceabilityService(DatabaseExecutor database, ITenantContext tenant) : IPublicTraceabilityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<PublicTracePublicationDto> PublishAsync(PublishPublicTraceCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (db, tx) =>
        {
            var lotNumber = Guard.Required(command.LotNumber, nameof(command.LotNumber), 100);
            var idempotencyKey = Guard.Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 160);
            var existing = await db.QuerySingleOrDefaultAsync<PublicationRow>(new CommandDefinition("""
                select public_code PublicCode, status Status, published_at PublishedAt, revoked_at RevokedAt
                from agro360.public_trace_publications
                where tenant_id=@TenantId and idempotency_key=@IdempotencyKey
                """, new { tenant.TenantId, IdempotencyKey = idempotencyKey }, tx, cancellationToken: cancellationToken));
            if (existing is not null)
                return new(existing.PublicCode, existing.Status, existing.PublishedAt, existing.RevokedAt);

            var source = await db.QuerySingleOrDefaultAsync<PublishSource>(new CommandDefinition("""
                select l.id LotId, l.lot_number LotNumber, p.name Product, l.quality_status QualityStatus,
                       f.name Farm, s.name Season, s.crop Crop,
                       r.received_at ReceivedAt,
                       exists(select 1 from agro360.harvest_records hr
                         join agro360.harvest_plans hp on hp.tenant_id=hr.tenant_id and hp.id=hr.plan_id
                         where hr.tenant_id=l.tenant_id and hr.id=r.harvest_record_id and hp.season_id=s.id) HasOrigin
                from agro360.inventory_stock_lots l
                join agro360.inventory_products p on p.tenant_id=l.tenant_id and p.id=l.product_id
                left join agro360.production_receipts r on r.tenant_id=l.tenant_id and r.lot_number=l.lot_number
                left join agro360.harvest_records hr0 on hr0.tenant_id=r.tenant_id and hr0.id=r.harvest_record_id
                left join agro360.harvest_plans hp0 on hp0.tenant_id=hr0.tenant_id and hp0.id=hr0.plan_id
                left join agro360.agriculture_seasons s on s.tenant_id=hp0.tenant_id and s.id=hp0.season_id
                left join agro360.geo_farms f on f.tenant_id=s.tenant_id and f.id=s.farm_id
                where l.tenant_id=@TenantId and l.lot_number=@LotNumber
                limit 1 for update of l
                """, new { tenant.TenantId, LotNumber = lotNumber }, tx, cancellationToken: cancellationToken))
                ?? throw new DomainException($"Lote '{lotNumber}' não foi encontrado.", "genealogy.lot_not_found");

            PublicTraceabilityRules.EnsurePublishable(source.QualityStatus, source.HasOrigin);
            var publicCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;
            var payload = new PublicTraceDto(publicCode, source.Product, source.LotNumber, "Produção agrícola rastreada",
                command.PublishFarm ? source.Farm : null, source.Season, source.Crop, [],
                [new("Recebimento", "Recebido", source.ReceivedAt), new("Qualidade", "Aprovado", now), new("Estoque", "Liberado", now)],
                "LIBERADO", now);
            var id = Guid.CreateVersion7();
            await db.ExecuteAsync(new CommandDefinition("""
                insert into agro360.public_trace_publications
                  (id,tenant_id,entity_type,entity_id,public_code,status,published_at,safe_payload,idempotency_key,created_by,updated_by)
                values(@Id,@TenantId,'STOCK_LOT',@LotId,@PublicCode,'PUBLISHED',@Now,@Payload::jsonb,@IdempotencyKey,@UserId,@UserId);
                insert into agro360.audit_logs(id,tenant_id,user_id,action,entity_type,entity_id,occurred_at)
                values(gen_random_uuid(),@TenantId,@UserId,'publish','PublicTracePublication',@Id,now());
                """, new { Id = id, tenant.TenantId, source.LotId, PublicCode = publicCode, Now = now,
                    Payload = JsonSerializer.Serialize(payload, JsonOptions), IdempotencyKey = idempotencyKey, tenant.UserId }, tx,
                cancellationToken: cancellationToken));
            return new PublicTracePublicationDto(publicCode, "PUBLISHED", now, null);
        }, cancellationToken);

    public Task RevokeAsync(string publicCode, RevokePublicTraceCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (db, tx) =>
        {
            var code = Guard.Required(publicCode, nameof(publicCode), 64);
            var reason = PublicTraceabilityRules.RequireRevocationReason(command.Reason);
            var id = await db.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("""
                update agro360.public_trace_publications
                set status='REVOKED', revoked_at=now(), revocation_reason=@Reason, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and public_code=@Code and status='PUBLISHED'
                returning id
                """, new { tenant.TenantId, Code = code, Reason = reason, tenant.UserId }, tx, cancellationToken: cancellationToken));
            if (!id.HasValue) throw new ConflictException("A publicação não existe ou já foi revogada.", "traceability.publication_unavailable");
            await db.ExecuteAsync(new CommandDefinition("""
                insert into agro360.audit_logs(id,tenant_id,user_id,action,entity_type,entity_id,occurred_at)
                values(gen_random_uuid(),@TenantId,@UserId,'revoke','PublicTracePublication',@Id,now())
                """, new { tenant.TenantId, tenant.UserId, Id = id.Value }, tx, cancellationToken: cancellationToken));
        }, cancellationToken);

    public Task<PublicTraceDto?> GetAsync(string publicCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicCode) || publicCode.Length > 64) return Task.FromResult<PublicTraceDto?>(null);
        return database.InSystemTransactionAsync(async (db, tx) =>
        {
            var json = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
                select safe_payload::text from agro360.public_trace_publications
                where public_code=@PublicCode and status='PUBLISHED' and revoked_at is null
                """, new { PublicCode = publicCode.Trim() }, tx, cancellationToken: cancellationToken));
            return json is null ? null : JsonSerializer.Deserialize<PublicTraceDto>(json, JsonOptions);
        }, cancellationToken);
    }

    private sealed class PublicationRow
    {
        public string PublicCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset PublishedAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
    }
    private sealed class PublishSource
    {
        public Guid LotId { get; set; }
        public string LotNumber { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string QualityStatus { get; set; } = string.Empty;
        public string? Farm { get; set; }
        public string? Season { get; set; }
        public string? Crop { get; set; }
        public DateTimeOffset? ReceivedAt { get; set; }
        public bool HasOrigin { get; set; }
    }
}
