using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record SupplierCommand(string Name, string Type, string Document, string? StateRegistration, string? Email, string? Phone, string? Address, string[] Categories, string Status, string? Notes);
public sealed record SupplierDto(Guid Id, string Name, string Type, string Document, string? Email, string? Phone, string[] Categories, string Status, DateTimeOffset UpdatedAt);
public sealed record PurchaseItemCommand(Guid ProductId, string Description, decimal Quantity, decimal UnitPrice);
public sealed record PurchaseCommand(Guid SupplierId, Guid? FarmId, string? CostCenter, Guid? SeasonId, decimal Discount, decimal Freight, decimal Taxes, PurchaseItemCommand[] Items);
public sealed record PurchaseDto(Guid Id, Guid SupplierId, string Status, decimal Total, DateTimeOffset CreatedAt);
public sealed record ReceiptItem(Guid PurchaseItemId, Guid WarehouseId, decimal Quantity, string? LotNumber, DateOnly? ExpiresOn);
public sealed record ReceiptCommand(ReceiptItem[] Items);
public sealed record InventoryItemCommand(string Sku, string Name, string Type, string Unit, decimal Minimum, bool RequiresLot, bool IsPerishable);
public sealed record InventoryMovementCommand(Guid ProductId, Guid WarehouseId, decimal Quantity, decimal UnitCost, string? LotNumber, DateOnly? ExpiresOn, string? Reason);
public sealed record TransferCommand(Guid ProductId, Guid SourceWarehouseId, Guid DestinationWarehouseId, decimal Quantity, string? Reason);
public sealed record FleetAssetCommand(string Type, string Brand, string Model, int? Year, string Identification, decimal? HourMeter, decimal? Odometer, Guid? FarmId, Guid? ResponsibleId, string Status);
public sealed record MaintenanceOrderCommand(Guid AssetId, string Type, string Description, Guid? SupplierId, Guid? ResponsibleId, DateOnly? ScheduledFor, DateOnly? NextReviewDate, decimal? NextHourMeter, decimal? NextOdometer, decimal LaborCost, MaintenancePartCommand[] Parts);
public sealed record MaintenancePartCommand(Guid ProductId, Guid WarehouseId, decimal Quantity, decimal UnitCost);
public sealed record CompleteMaintenanceCommand(DateTimeOffset CompletedAt, string CompletionNotes, string AssetFinalStatus = "AVAILABLE");
public sealed record FuelFillUpCommand(Guid AssetId, Guid FuelProductId, Guid WarehouseId, decimal Quantity, decimal UnitPrice, decimal? HourMeter, decimal? Odometer, Guid? ResponsibleId, string? Location);
public sealed record OperationalDashboardDto(long ActiveSuppliers, long OpenPurchases, long AwaitingApproval, long ReceivedThisMonth, decimal PurchasedThisMonth, long LowStockItems, long ExpiringItems, long AvailableAssets, long AssetsInMaintenance, long OverdueMaintenance, decimal MaintenanceCostThisMonth, decimal FuelThisMonth);

public interface IOperationsService
{
    Task<PagedResult<SupplierDto>> ListSuppliersAsync(string? search, string? status, int page, int pageSize, CancellationToken ct);
    Task<SupplierDto?> GetSupplierAsync(Guid id, CancellationToken ct);
    Task<SupplierDto> CreateSupplierAsync(SupplierCommand command, CancellationToken ct);
    Task<SupplierDto> UpdateSupplierAsync(Guid id, SupplierCommand command, CancellationToken ct);
    Task DeleteSupplierAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<PurchaseDto>> ListPurchasesAsync(string? status, CancellationToken ct);
    Task<PurchaseDto> CreatePurchaseAsync(PurchaseCommand command, CancellationToken ct);
    Task ChangePurchaseStatusAsync(Guid id, string action, CancellationToken ct);
    Task ReceivePurchaseAsync(Guid id, ReceiptCommand command, CancellationToken ct);
    Task<Guid> CreateInventoryItemAsync(InventoryItemCommand command, CancellationToken ct);
    Task<Guid> MoveInventoryAsync(string type, InventoryMovementCommand command, CancellationToken ct);
    Task TransferInventoryAsync(TransferCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListMovementsAsync(CancellationToken ct);
    Task<Guid> CreateAssetAsync(FleetAssetCommand command, CancellationToken ct);
    Task UpdateAssetAsync(Guid id, FleetAssetCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListAssetsAsync(CancellationToken ct);
    Task<Guid> CreateMaintenanceAsync(MaintenanceOrderCommand command, CancellationToken ct);
    Task CompleteMaintenanceAsync(Guid id, CompleteMaintenanceCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListMaintenanceAsync(CancellationToken ct);
    Task<Guid> CreateFillUpAsync(FuelFillUpCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListFillUpsAsync(CancellationToken ct);
    Task<OperationalDashboardDto> DashboardAsync(CancellationToken ct);
}
