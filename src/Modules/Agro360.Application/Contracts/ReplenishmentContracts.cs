namespace Agro360.Application.Contracts;

public sealed record ReplenishmentPolicyCommand(Guid ProductId, Guid WarehouseId, decimal MinimumStock, decimal TargetStock,
    int? ReplenishmentDays, decimal? MinimumPurchase, decimal? PurchaseMultiple, string PurchaseUnit,
    decimal StockPerPurchaseUnit, Guid ResponsibleId, string Status, long? Version = null);
public sealed record MaterialNeedQuery(Guid? ProductId = null, Guid? WarehouseId = null, Guid? ResponsibleId = null,
    string? Status = null, DateOnly? NeededUntil = null, bool? UncoveredOnly = null, int Page = 1, int PageSize = 25);
public sealed record AnalyzeMaterialNeedCommand(Guid PolicyId, DateOnly NeededOn, DateOnly HorizonOn);
public sealed record ConfirmMaterialNeedCommand(long Version, decimal? ConfirmedPurchaseQuantity, string? Justification, string IdempotencyKey);
public interface IReplenishmentService
{
    Task<dynamic> OptionsAsync(CancellationToken ct);
    Task<IReadOnlyList<dynamic>> PoliciesAsync(MaterialNeedQuery query, CancellationToken ct);
    Task<Guid> SavePolicyAsync(Guid? id, ReplenishmentPolicyCommand command, CancellationToken ct);
    Task<dynamic> NeedsAsync(MaterialNeedQuery query, CancellationToken ct);
    Task<Guid> AnalyzeAsync(AnalyzeMaterialNeedCommand command, CancellationToken ct);
    Task<dynamic> DetailAsync(Guid id, CancellationToken ct);
    Task<Guid> ConfirmPurchaseAsync(Guid id, ConfirmMaterialNeedCommand command, CancellationToken ct);
    Task DismissAsync(Guid id, long version, string reason, CancellationToken ct);
}
