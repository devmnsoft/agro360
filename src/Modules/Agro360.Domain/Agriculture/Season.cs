using Agro360.SharedKernel;

namespace Agro360.Domain.Agriculture;

public enum SeasonStatus
{
    Planned = 1,
    Active = 2,
    Harvested = 3,
    Closed = 4,
    Cancelled = 5
}

public sealed record SeasonCreated(Guid SeasonId, Guid FarmId, string Crop) : DomainEvent;

public sealed record CropPlanted(Guid SeasonId, Guid FieldId, decimal AreaHa) : DomainEvent;

public sealed record CropHarvested(Guid SeasonId, Guid FieldId, decimal Quantity, string Unit) : DomainEvent;

public sealed class Season : TenantEntity
{
    private Season()
    {
    }

    private Season(
        Guid id,
        Guid tenantId,
        Guid farmId,
        string name,
        string crop,
        DateOnly startDate,
        DateOnly endDate)
        : base(id, tenantId)
    {
        if (endDate <= startDate)
        {
            throw new DomainException("O fim da safra deve ser posterior ao início.", "season.invalid_period");
        }

        FarmId = Guard.Required(farmId, nameof(farmId));
        Name = Guard.Required(name, nameof(name), 120);
        Crop = Guard.Required(crop, nameof(crop), 80);
        StartDate = startDate;
        EndDate = endDate;
        Status = SeasonStatus.Planned;
        Raise(new SeasonCreated(Id, FarmId, Crop));
    }

    public Guid FarmId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Crop { get; private set; } = string.Empty;

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public SeasonStatus Status { get; private set; }

    public decimal PlannedAreaHa { get; private set; }

    public decimal ExpectedYieldPerHa { get; private set; }

    public static Season Create(
        Guid tenantId,
        Guid farmId,
        string name,
        string crop,
        DateOnly startDate,
        DateOnly endDate) =>
        new(Guid.CreateVersion7(), tenantId, farmId, name, crop, startDate, endDate);

    public void Plan(decimal areaHa, decimal expectedYieldPerHa)
    {
        PlannedAreaHa = Guard.Positive(areaHa, nameof(areaHa));
        ExpectedYieldPerHa = Guard.Positive(expectedYieldPerHa, nameof(expectedYieldPerHa));
        Touch();
    }

    public void RegisterPlanting(Guid fieldId, decimal areaHa)
    {
        if (Status is SeasonStatus.Harvested or SeasonStatus.Closed or SeasonStatus.Cancelled)
        {
            throw new ConflictException("Não é possível plantar em uma safra encerrada.", "season.not_open");
        }

        Status = SeasonStatus.Active;
        Touch();
        Raise(new CropPlanted(Id, Guard.Required(fieldId, nameof(fieldId)), Guard.Positive(areaHa, nameof(areaHa))));
    }

    public void RegisterHarvest(Guid fieldId, Quantity quantity)
    {
        if (Status != SeasonStatus.Active)
        {
            throw new ConflictException("A colheita exige uma safra ativa.", "season.not_active");
        }

        Status = SeasonStatus.Harvested;
        Touch();
        Raise(new CropHarvested(Id, Guard.Required(fieldId, nameof(fieldId)), quantity.Value, quantity.Unit));
    }
}
