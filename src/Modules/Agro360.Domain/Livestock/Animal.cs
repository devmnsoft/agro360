using Agro360.SharedKernel;

namespace Agro360.Domain.Livestock;

public enum AnimalStatus
{
    Active = 1,
    Quarantine = 2,
    Sold = 3,
    Dead = 4,
    Slaughtered = 5
}

public sealed record AnimalRegistered(Guid AnimalId, string Tag) : DomainEvent;

public sealed record AnimalWeighed(Guid AnimalId, decimal WeightKg, decimal? DailyGainKg) : DomainEvent;

public sealed record AnimalSold(Guid AnimalId, DateTimeOffset SoldAt) : DomainEvent;

public sealed class Animal : TenantEntity
{
    private Animal()
    {
    }

    private Animal(
        Guid id,
        Guid tenantId,
        Guid farmId,
        string tag,
        string species,
        string sex,
        DateOnly birthDate)
        : base(id, tenantId)
    {
        FarmId = Guard.Required(farmId, nameof(farmId));
        Tag = Guard.Required(tag, nameof(tag), 80);
        Species = Guard.Required(species, nameof(species), 60);
        Sex = Guard.Required(sex, nameof(sex), 20);
        BirthDate = Guard.Range(birthDate, new DateOnly(1980, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow), nameof(birthDate));
        Status = AnimalStatus.Active;
        Raise(new AnimalRegistered(Id, Tag));
    }

    public Guid FarmId { get; private set; }

    public Guid? HerdId { get; private set; }

    public string Tag { get; private set; } = string.Empty;

    public string Species { get; private set; } = string.Empty;

    public string Sex { get; private set; } = string.Empty;

    public DateOnly BirthDate { get; private set; }

    public AnimalStatus Status { get; private set; }

    public decimal? CurrentWeightKg { get; private set; }

    public DateOnly? LastWeightDate { get; private set; }

    public DateOnly? WithdrawalUntil { get; private set; }

    public static Animal Register(
        Guid tenantId,
        Guid farmId,
        string tag,
        string species,
        string sex,
        DateOnly birthDate) =>
        new(Guid.CreateVersion7(), tenantId, farmId, tag, species, sex, birthDate);

    public decimal? Weigh(decimal weightKg, DateOnly measuredOn)
    {
        var newWeight = Guard.Positive(weightKg, nameof(weightKg));
        decimal? dailyGain = null;

        if (CurrentWeightKg.HasValue && LastWeightDate.HasValue)
        {
            var days = measuredOn.DayNumber - LastWeightDate.Value.DayNumber;
            if (days <= 0)
            {
                throw new ConflictException("A nova pesagem deve ocorrer após a pesagem anterior.", "livestock.weight_date_invalid");
            }

            dailyGain = decimal.Round((newWeight - CurrentWeightKg.Value) / days, 4);
        }

        CurrentWeightKg = newWeight;
        LastWeightDate = measuredOn;
        Touch();
        Raise(new AnimalWeighed(Id, newWeight, dailyGain));
        return dailyGain;
    }

    public void ApplyTreatment(DateOnly appliedOn, int withdrawalDays)
    {
        if (withdrawalDays < 0)
        {
            throw new DomainException("O período de carência não pode ser negativo.", "livestock.withdrawal_invalid");
        }

        var until = appliedOn.AddDays(withdrawalDays);
        if (!WithdrawalUntil.HasValue || until > WithdrawalUntil.Value)
        {
            WithdrawalUntil = until;
        }

        Touch();
    }

    public void Sell(DateOnly saleDate)
    {
        if (Status != AnimalStatus.Active)
        {
            throw new ConflictException("Somente animal ativo pode ser vendido.", "livestock.animal_not_active");
        }

        if (WithdrawalUntil.HasValue && saleDate <= WithdrawalUntil.Value)
        {
            throw new ConflictException(
                $"O animal está em carência sanitária até {WithdrawalUntil:dd/MM/yyyy}.",
                "livestock.withdrawal_period_active");
        }

        Status = AnimalStatus.Sold;
        Touch();
        Raise(new AnimalSold(Id, DateTimeOffset.UtcNow));
    }
}
