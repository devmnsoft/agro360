using Agro360.SharedKernel;

namespace Agro360.Domain.Properties;

public sealed class Farm : TenantEntity
{
    private Farm()
    {
    }

    private Farm(Guid id, Guid tenantId, Guid organizationId, string name, string state, decimal totalAreaHa)
        : base(id, tenantId)
    {
        OrganizationId = Guard.Required(organizationId, nameof(organizationId));
        Name = Guard.Required(name, nameof(name), 160);
        State = Guard.Required(state, nameof(state), 2).ToUpperInvariant();
        TotalAreaHa = Guard.Positive(totalAreaHa, nameof(totalAreaHa));
    }

    public string Name { get; private set; } = string.Empty;

    public string State { get; private set; } = string.Empty;

    public decimal TotalAreaHa { get; private set; }

    public string? RegistrationNumber { get; private set; }

    public string? CarNumber { get; private set; }

    public static Farm Create(
        Guid tenantId,
        Guid organizationId,
        string name,
        string state,
        decimal totalAreaHa) =>
        new(Guid.CreateVersion7(), tenantId, organizationId, name, state, totalAreaHa);
}

public sealed class Field : TenantEntity
{
    private Field()
    {
    }

    private Field(Guid id, Guid tenantId, Guid farmId, string name, decimal areaHa)
        : base(id, tenantId)
    {
        FarmId = Guard.Required(farmId, nameof(farmId));
        Name = Guard.Required(name, nameof(name), 120);
        AreaHa = Guard.Positive(areaHa, nameof(areaHa));
    }

    public Guid FarmId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public decimal AreaHa { get; private set; }

    public static Field Create(Guid tenantId, Guid farmId, string name, decimal areaHa) =>
        new(Guid.CreateVersion7(), tenantId, farmId, name, areaHa);
}
