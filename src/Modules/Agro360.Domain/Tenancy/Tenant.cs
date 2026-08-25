using Agro360.SharedKernel;

namespace Agro360.Domain.Tenancy;

public enum TenantStatus
{
    Trial = 1,
    Active = 2,
    Suspended = 3,
    CancellationPending = 4,
    Cancelled = 5
}

public sealed class Tenant : AggregateRoot
{
    private Tenant()
    {
    }

    private Tenant(Guid id, string name, string slug, string timeZoneId)
        : base(id)
    {
        Name = Guard.Required(name, nameof(name), 160);
        Slug = NormalizeSlug(slug);
        TimeZoneId = Guard.Required(timeZoneId, nameof(timeZoneId), 64);
        Status = TenantStatus.Trial;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string TimeZoneId { get; private set; } = "America/Belem";

    public TenantStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Tenant Create(string name, string slug, string timeZoneId = "America/Belem") =>
        new(Guid.CreateVersion7(), name, slug, timeZoneId);

    public void Activate() => Status = TenantStatus.Active;

    public void EnsureCanOperate()
    {
        if (Status is TenantStatus.Suspended or TenantStatus.CancellationPending or TenantStatus.Cancelled)
        {
            throw new ForbiddenException("O tenant não está autorizado a gerar novas operações.");
        }
    }

    private static string NormalizeSlug(string slug)
    {
        var normalized = Guard.Required(slug, nameof(slug), 80).ToLowerInvariant();
        if (normalized.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            throw new DomainException("O slug aceita somente letras, números e hífen.", "tenant.slug_invalid");
        }

        return normalized;
    }
}
