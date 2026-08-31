using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record SustainabilityDashboard(int RegularProperties,int PendingProperties,int ExpiredDocuments,int ExpiringDocuments,int BlockedLots,int CompliantLots,int BlockedSuppliers,int CriticalIndicators,decimal WaterConsumption,decimal EnergyConsumption,decimal EstimatedEmissions,decimal WasteQuantity,int OpenAudits,int OverdueActions,int ActiveCarbonProjects,int CriticalAlerts);
public sealed record EnvironmentalComplianceCommand([Required]Guid FarmId,[Required,MaxLength(200)]string ProducerName,[Range(0.000001,double.MaxValue)]decimal TotalArea,[Range(0,double.MaxValue)]decimal ProductiveArea,[Range(0,double.MaxValue)]decimal PreservationArea,[Range(0,double.MaxValue)]decimal AppArea,[Range(0,double.MaxValue)]decimal LegalReserveArea,[MaxLength(80)]string? CarNumber,[Required]string CarStatus,[MaxLength(120)]string? EnvironmentalLicense,DateOnly? LicenseValidUntil,[MaxLength(160)]string? IssuingAgency,bool Georeferenced,[Required]string Status,[Required]string Risk,[MaxLength(2000)]string? Notes);
public sealed record EnvironmentalComplianceListItem(Guid Id,string FarmName,string ProducerName,decimal TotalArea,decimal ProductiveArea,string Status,string Risk,DateOnly? LicenseValidUntil);
public sealed record SustainabilityFarmOption(Guid Id,string Name);
public interface ISustainabilityService
{
    Task<SustainabilityDashboard> DashboardAsync(CancellationToken ct);
    Task<IReadOnlyList<EnvironmentalComplianceListItem>> CompliancesAsync(string? status,CancellationToken ct);
    Task<IReadOnlyList<SustainabilityFarmOption>> FarmsAsync(string? search,CancellationToken ct);
    Task<Guid> SaveComplianceAsync(EnvironmentalComplianceCommand command,CancellationToken ct);
    Task<byte[]> ExportCsvAsync(string report,CancellationToken ct);
}
