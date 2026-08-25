namespace Agro360.SharedKernel;

public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("O identificador da entidade é obrigatório.", "entity.id_required");
        }

        Id = id;
    }

    public Guid Id { get; protected set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public IReadOnlyCollection<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}

public abstract class AggregateRoot : Entity
{
    protected AggregateRoot()
    {
    }

    protected AggregateRoot(Guid id)
        : base(id)
    {
    }
}

public abstract class TenantEntity : AggregateRoot
{
    protected TenantEntity()
    {
    }

    protected TenantEntity(Guid id, Guid tenantId)
        : base(id)
    {
        TenantId = Guard.Required(tenantId, nameof(tenantId));
    }

    public Guid TenantId { get; protected set; }

    public Guid? OrganizationId { get; protected set; }

    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; protected set; }

    public DateTimeOffset? DeletedAt { get; protected set; }

    public long Version { get; protected set; } = 1;

    protected void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        Version++;
    }
}
