using System.ComponentModel.DataAnnotations;
namespace Agro360.Application.Contracts;

public sealed record FleetLookup(Guid Id, string Name);
public sealed record FleetDashboard(int AvailableAssets, int OperatingAssets, int MaintenanceAssets, int UnavailableAssets, int OverdueMaintenances, int UpcomingMaintenances, int OpenWorkOrders, int CriticalWorkOrders, int OverdueWorkOrders, decimal MonthFuelQuantity, decimal MonthFuelCost, int OpenDowntimes, decimal DowntimeHours, decimal AvailabilityPercent, decimal TotalCost, int WaitingParts = 0, int ActiveBlocks = 0, int ActiveReservations = 0);
public sealed record FleetAsset(Guid Id, string InternalCode, string Name, string Type, string Status, string? Brand, string? Model, string? Plate, decimal Odometer, decimal HourMeter, string? PropertyName, string? CostCenterName);
public sealed record FleetAssetCommand(
    [Required, MaxLength(40)] string InternalCode,
    [Required, MaxLength(160)] string Name,
    [Required] Guid AssetTypeId,
    [Required] string Status,
    [MaxLength(80)] string? Brand,
    [MaxLength(80)] string? Model,
    [Range(1900, 2200)] int? Year,
    [MaxLength(20)] string? Plate,
    [MaxLength(80)] string? SerialNumber,
    Guid? PropertyId,
    Guid? CostCenterId,
    [Range(0, double.MaxValue)] decimal Odometer,
    [Range(0, double.MaxValue)] decimal HourMeter,
    [Range(0, double.MaxValue)] decimal? FuelCapacity,
    Guid? MainOperatorId,
    DateOnly? AcquiredOn,
    [Range(0, double.MaxValue)] decimal? AcquisitionValue,
    [MaxLength(2000)] string? Notes,
    string? MeterJustification,
    string CadastralStatus = "ACTIVE",
    string Ownership = "OWNED",
    [MaxLength(40)] string? EnergySource = null,
    DateOnly? CommissionedOn = null);
public sealed record FleetOperatorCommand([Required, MaxLength(160)] string Name, [MaxLength(40)] string? Document, [Required, MaxLength(40)] string EmploymentType, [Required, MaxLength(80)] string Role, [Required] string Status, Guid? PropertyId, [MaxLength(80)] string? LicenseCategories, DateOnly? LicenseExpiresOn, [MaxLength(2000)] string? Notes);
public sealed record MaintenancePlanCommand(
    [Required] Guid AssetId,
    [Required, MaxLength(80)] string MaintenanceType,
    [Required, MaxLength(2000)] string Description,
    [Range(0.01, double.MaxValue)] decimal Periodicity,
    [Required] string ControlUnit,
    DateTimeOffset? NextExecutionAt,
    decimal? NextMeter,
    [Required] string Status,
    Guid? ResponsibleId,
    [Range(0, double.MaxValue)] decimal EstimatedCost,
    [Required, MinLength(1)] IReadOnlyList<string> Checklist,
    string DuePolicy = "FIRST_CRITERION",
    int? DateIntervalDays = null,
    decimal? HourInterval = null,
    decimal? KmInterval = null);
public sealed record WorkOrder(Guid Id, string Code, string AssetName, string Type, string Priority, string Status, DateTimeOffset OpenedAt, DateTimeOffset? DueAt, string Description, decimal Cost);
public sealed record WorkOrderCommand([Required] Guid AssetId, [Required] string Type, [Required] string Priority, Guid? ResponsibleId, DateTimeOffset? DueAt, [Required, MaxLength(4000)] string Description, bool BlockAsset);
public sealed record WorkOrderTransitionCommand([Required] string Status, [MaxLength(4000)] string? Diagnosis, [MaxLength(4000)] string? ServicesPerformed, [MaxLength(1000)] string? Reason, decimal? Odometer, decimal? HourMeter, string? MeterJustification);
public sealed record RefuelingCommand([Required] Guid AssetId, Guid? OperatorId, [Required] Guid FuelTypeId, [Range(0.001, double.MaxValue)] decimal Quantity, [Range(0, double.MaxValue)] decimal UnitPrice, DateTimeOffset OccurredAt, decimal? Odometer, decimal? HourMeter, [MaxLength(160)] string? Location, Guid? PropertyId, Guid? CostCenterId, Guid? EvidenceDocumentId, [MaxLength(2000)] string? Notes, string? MeterJustification);
public sealed record DowntimeCommand([Required] Guid AssetId, [Required] string Type, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, [Required, MaxLength(2000)] string Reason, [Required] Guid ResponsibleId, [MaxLength(2000)] string? OperationalImpact, Guid? SeasonId, Guid? FieldId, Guid? RouteId, bool MakesUnavailable);
public interface IFleetService
{
    Task<FleetDashboard> DashboardAsync(CancellationToken ct); Task<IReadOnlyList<FleetLookup>> LookupsAsync(string kind, string? search, CancellationToken ct);
    Task<IReadOnlyList<FleetAsset>> AssetsAsync(string? search, string? status, int page, int pageSize, CancellationToken ct); Task<Guid> SaveAssetAsync(Guid? id, FleetAssetCommand command, CancellationToken ct);
    Task<Guid> CreateOperatorAsync(FleetOperatorCommand command, CancellationToken ct); Task<Guid> CreateMaintenancePlanAsync(MaintenancePlanCommand command, CancellationToken ct);
    Task<IReadOnlyList<WorkOrder>> WorkOrdersAsync(string? search, string? status, int page, int pageSize, CancellationToken ct); Task<Guid> OpenWorkOrderAsync(WorkOrderCommand command, CancellationToken ct); Task TransitionWorkOrderAsync(Guid id, WorkOrderTransitionCommand command, bool meterOverride, CancellationToken ct);
    Task<Guid> RefuelAsync(RefuelingCommand command, bool meterOverride, CancellationToken ct); Task<Guid> OpenDowntimeAsync(DowntimeCommand command, CancellationToken ct);
}

public sealed record MeterReadingCommand(Guid AssetId, string MeterKind, DateTimeOffset OccurredAt, decimal PhysicalValue, string Unit, string Origin, bool IsReset, Guid? ResponsibleId, string? Justification, string? IdempotencyKey);
public sealed record AssetReservationCommand(Guid AssetId, string Purpose, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? ReferenceType, Guid? ReferenceId, string? Notes, string? IdempotencyKey);
public sealed record WorkOrderPartCommand(Guid WorkOrderId, Guid ProductId, Guid WarehouseId, decimal Quantity, string Unit, decimal UnitCost, string Description, string? IdempotencyKey);
public sealed record PartReturnCommand(decimal ReturnedQuantity, decimal LostQuantity, bool Reusable, string? Notes);
public sealed record InspectionCommand(string Result, IReadOnlyList<string> Checklist, int BlockingFailures, string? Notes);
public sealed record TimeLogCommand(Guid TechnicianId, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? Notes);
public sealed record OperationalRefuelCommand(Guid AssetId, Guid FuelTypeId, string Source, decimal Quantity, decimal UnitPrice, DateTimeOffset OccurredAt, Guid? OperatorId, Guid? WarehouseId, Guid? ProductId, string? LotNumber, bool TankFull, decimal? Odometer, decimal? HourMeter, string? Location, Guid? PropertyId, string? Notes, string? MeterJustification, string? IdempotencyKey);
public sealed record FleetExportFilter(string Kind, string? Status, Guid? AssetId);

public interface IFleetOperationsService
{
    Task<dynamic?> AssetDetailAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListReadingsAsync(Guid assetId, string? meterKind, CancellationToken ct);
    Task<Guid> RecordReadingAsync(MeterReadingCommand command, bool meterOverride, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListPlansAsync(Guid? assetId, CancellationToken ct);
    Task EvaluatePlansAsync(CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListRequestsAsync(string? status, CancellationToken ct);
    Task<Guid> CreateRequestAsync(Guid assetId, string defectClass, string severity, string problem, bool blocksAsset, CancellationToken ct);
    Task<dynamic?> WorkOrderDetailAsync(Guid id, CancellationToken ct);
    Task ReservePartAsync(WorkOrderPartCommand command, CancellationToken ct);
    Task ConsumePartAsync(Guid partLineId, decimal quantity, string? idempotencyKey, CancellationToken ct);
    Task ReturnPartAsync(Guid partLineId, PartReturnCommand command, CancellationToken ct);
    Task LogTimeAsync(Guid workOrderId, TimeLogCommand command, CancellationToken ct);
    Task InspectAsync(Guid workOrderId, InspectionCommand command, CancellationToken ct);
    Task ReleaseAssetAsync(Guid assetId, string reason, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> AvailabilityAsync(Guid assetId, DateTimeOffset from, DateTimeOffset until, CancellationToken ct);
    Task<Guid> ReserveAssetAsync(AssetReservationCommand command, CancellationToken ct);
    Task CancelReservationAsync(Guid id, string reason, CancellationToken ct);
    Task<Guid> RefuelOperationalAsync(OperationalRefuelCommand command, bool meterOverride, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListRefuelingsAsync(Guid? assetId, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> CostSummaryAsync(Guid? assetId, DateOnly? from, DateOnly? until, CancellationToken ct);
    Task<byte[]> ExportCsvAsync(FleetExportFilter filter, CancellationToken ct);
}
