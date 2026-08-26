using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record GeoFeature(Guid Id,string EntityType,string Name,string GeometryType,string GeoJson,decimal? CentroidLatitude,decimal? CentroidLongitude,string Status,decimal? InformedAreaHa,decimal? CalculatedAreaHa,string Origin,DateTimeOffset UpdatedAt);
public sealed record GeoFeatureCommand([Required,RegularExpression("^(PROPERTY|FIELD|PASTURE|PADDOCK|WAREHOUSE|ROUTE|LOGISTICS_POINT|OCCURRENCE|MANAGEMENT_ZONE|ENVIRONMENTAL_AREA)$")]string EntityType,[Required,MaxLength(180)]string Name,[Required]string GeoJson,Guid? PropertyId,Guid? ParentId,[Range(0.0001,double.MaxValue)]decimal? InformedAreaHa,[Required,RegularExpression("^(DRAFT|ACTIVE|INACTIVE|BLOCKED|RESOLVED)$")]string Status="ACTIVE",[Required,MaxLength(40)]string Origin="MANUAL");
public sealed record OccurrenceCommand([Required,MaxLength(180)]string Name,[Required,RegularExpression("^(PEST|DISEASE|PLANTING_FAILURE|EROSION|FLOODING|SICK_ANIMAL|BROKEN_FENCE|MAINTENANCE|LOGISTICS|CLAIM|ENVIRONMENTAL_RISK|NON_CONFORMITY)$")]string Type,[Required,RegularExpression("^(LOW|MEDIUM|HIGH|CRITICAL)$")]string Severity,[Required]string GeoJson,[Required]Guid ResponsibleId,Guid? PropertyId,[MaxLength(1000)]string? Notes);
public sealed record RouteSegmentCommand([Required]Guid RouteId,[Required,RegularExpression("^(RIVER|RURAL_ROAD|FERRY|HIGHWAY)$")]string Type,[Required,MaxLength(180)]string Name,[Required]string GeoJson,[Range(0.01,double.MaxValue)]decimal DistanceKm,[Range(1,100000)]int EstimatedMinutes,[Required,RegularExpression("^(ACTIVE|RESTRICTED|BLOCKED)$")]string Status,[MaxLength(500)]string? Restrictions,string? OperationalWindow,bool AuthorizedOverride=false);
public sealed record GeoJsonImportCommand([Required,RegularExpression("^(PROPERTY|FIELD|PASTURE|MANAGEMENT_ZONE)$")]string EntityType,[Required,MaxLength(240)]string FileName,[Required]string GeoJson,bool Confirm=false);
public sealed record GeoImportResult(Guid ImportId,bool Valid,int TotalFeatures,int ValidFeatures,IReadOnlyList<GeoImportError> Errors,bool Persisted);
public sealed record GeoImportError(int Feature,string Message);
public sealed record GeoDashboard(int GeoreferencedProperties,int MappedFields,int MappedPastures,int AreasWithoutGeometry,int OpenOccurrences,int CriticalOccurrences,int ActiveRoutes,int TripsAtRisk,int EnvironmentalAreas,int ManagementZones,int TerritorialAlerts);

public interface IGeospatialService
{
 Task<IReadOnlyList<GeoFeature>> ListAsync(string? entityType,string? status,CancellationToken ct); Task<GeoFeature> GetAsync(Guid id,CancellationToken ct); Task<Guid> SaveAsync(Guid? id,GeoFeatureCommand command,CancellationToken ct);
 Task<Guid> AddOccurrenceAsync(OccurrenceCommand command,CancellationToken ct); Task<Guid> AddRouteSegmentAsync(RouteSegmentCommand command,CancellationToken ct);
 Task<GeoImportResult> ImportAsync(GeoJsonImportCommand command,CancellationToken ct); Task<string> ExportAsync(string? entityType,CancellationToken ct); Task<GeoDashboard> DashboardAsync(CancellationToken ct); Task<object> LayersAsync(CancellationToken ct);
}
