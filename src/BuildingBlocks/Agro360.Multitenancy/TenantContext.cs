using Agro360.SharedKernel;

namespace Agro360.Multitenancy;

public interface ITenantContext
{
    Guid TenantId { get; }

    Guid UserId { get; }

    Guid? OrganizationId { get; }

    Guid? FarmId { get; }

    string TimeZoneId { get; }

    bool IsAvailable { get; }
}

public interface IMutableTenantContext : ITenantContext
{
    void SetScope(TenantScope scope);

    void Clear();
}

public sealed record TenantScope(
    Guid TenantId,
    Guid UserId,
    Guid? OrganizationId,
    Guid? FarmId,
    string TimeZoneId = "America/Belem");

public sealed class TenantContext : IMutableTenantContext
{
    private TenantScope? _scope;

    public Guid TenantId => _scope?.TenantId
        ?? throw new ForbiddenException("O contexto do tenant não foi estabelecido.");

    public Guid UserId => _scope?.UserId
        ?? throw new ForbiddenException("O contexto do usuário não foi estabelecido.");

    public Guid? OrganizationId => _scope?.OrganizationId;

    public Guid? FarmId => _scope?.FarmId;

    public string TimeZoneId => _scope?.TimeZoneId ?? "UTC";

    public bool IsAvailable => _scope is not null;

    public void SetScope(TenantScope scope)
    {
        if (_scope is not null)
        {
            throw new InvalidOperationException("O contexto do tenant já foi definido para esta requisição.");
        }

        _scope = scope with
        {
            TenantId = Guard.Required(scope.TenantId, nameof(scope.TenantId)),
            UserId = Guard.Required(scope.UserId, nameof(scope.UserId)),
            TimeZoneId = Guard.Required(scope.TimeZoneId, nameof(scope.TimeZoneId), 64)
        };
    }

    public void Clear() => _scope = null;
}
