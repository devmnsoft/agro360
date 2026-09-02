using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Authorize(Policy=Permissions.MapsRead)]
public sealed class MapsController(IGeospatialService service,ILogger<MapsController> logger):ControllerBase
{
 [HttpGet("api/maps/layers")]public Task<object> Layers(CancellationToken ct)=>service.LayersAsync(ct);
 [HttpGet("api/maps/features")]public Task<IReadOnlyList<GeoFeature>> Features([FromQuery]string? entityType,[FromQuery]string? status,CancellationToken ct)=>service.ListAsync(entityType,status,ct);
 [HttpGet("api/maps/features/{id:guid}")]public Task<GeoFeature> Feature(Guid id,CancellationToken ct)=>service.GetAsync(id,ct);
 [HttpPost("api/maps/features"),Authorize(Policy=Permissions.MapsWrite)]public Task<IActionResult> Create(GeoFeatureCommand x,CancellationToken ct)=>Created(()=>service.SaveAsync(null,x,ct),"api/maps/features");
 [HttpPut("api/maps/features/{id:guid}"),Authorize(Policy=Permissions.MapsWrite)]public async Task<IActionResult> Update(Guid id,GeoFeatureCommand x,CancellationToken ct){await Boundary("update feature",()=>service.SaveAsync(id,x,ct));return NoContent();}
 [HttpPost("api/maps/imports/geojson"),Authorize(Policy=Permissions.MapsWrite)]public Task<GeoImportResult> Import(GeoJsonImportCommand x,CancellationToken ct)=>Boundary("import GeoJSON",()=>service.ImportAsync(x,ct));
 [HttpGet("api/maps/exports/geojson")]public async Task<IActionResult> Export([FromQuery]string? entityType,CancellationToken ct)=>File(System.Text.Encoding.UTF8.GetBytes(await service.ExportAsync(entityType,ct)),"application/geo+json","agro360.geojson");
 [HttpGet("api/maps/dashboard")]public Task<GeoDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 private async Task<IActionResult> Created(Func<Task<Guid>> op,string route){var id=await Boundary("create feature",op);return base.Created($"{route}/{id}",new{id});}
 private async Task<T> Boundary<T>(string operation,Func<Task<T>> op){try{return await op();}catch(Exception ex){ApiLogMessages.GeospatialBoundaryFailed(logger,operation,ex);throw;}}
}

[ApiController,Route("api/geospatial"),Authorize(Policy=Permissions.MapsRead)]
public sealed class GeospatialController(IGeospatialService service,ILogger<GeospatialController> logger):ControllerBase
{
 private static readonly Dictionary<string,string> EntityRoutes=new(StringComparer.OrdinalIgnoreCase){{"properties","PROPERTY"},{"fields","FIELD"},{"pastures","PASTURE"},{"paddocks","PADDOCK"},{"management-zones","MANAGEMENT_ZONE"},{"environmental-areas","ENVIRONMENTAL_AREA"},{"routes","ROUTE"}};
 [HttpGet("{entity:regex(^properties|fields|pastures|paddocks|management-zones|environmental-areas|routes$)}")]public Task<IReadOnlyList<GeoFeature>> List(string entity,CancellationToken ct)=>service.ListAsync(Resolve(entity),null,ct);
 [HttpPost("{entity:regex(^properties|fields|pastures|paddocks|management-zones|environmental-areas|routes$)}"),Authorize(Policy=Permissions.MapsWrite)]public async Task<IActionResult> Create(string entity,GeoFeatureCommand x,CancellationToken ct){var expected=Resolve(entity);if(x.EntityType!=expected)return BadRequest(new{error="Tipo incompatível com a rota."});var id=await Boundary("create",()=>service.SaveAsync(null,x,ct));return Created($"api/geospatial/{entity}/{id}",new{id});}
 [HttpPut("{entity:regex(^properties|fields|pastures|paddocks|management-zones|environmental-areas|routes$)}/{id:guid}"),Authorize(Policy=Permissions.MapsWrite)]public async Task<IActionResult> Update(string entity,Guid id,GeoFeatureCommand x,CancellationToken ct){if(x.EntityType!=Resolve(entity))return BadRequest();await Boundary("update",()=>service.SaveAsync(id,x,ct));return NoContent();}
 [HttpGet("occurrences")]public Task<IReadOnlyList<GeoFeature>> Occurrences(CancellationToken ct)=>service.ListAsync("OCCURRENCE",null,ct);
 [HttpPost("occurrences"),Authorize(Policy=Permissions.MapsWrite)]public async Task<IActionResult> Occurrence(OccurrenceCommand x,CancellationToken ct){var id=await Boundary("occurrence",()=>service.AddOccurrenceAsync(x,ct));return Created($"api/geospatial/occurrences/{id}",new{id});}
 [HttpPost("route-segments"),Authorize(Policy=Permissions.MapsWrite)]public async Task<IActionResult> Segment(RouteSegmentCommand x,CancellationToken ct){var id=await Boundary("route segment",()=>service.AddRouteSegmentAsync(x,ct));return Created($"api/geospatial/route-segments/{id}",new{id});}
 private static string Resolve(string route)=>EntityRoutes.TryGetValue(route,out var type)?type:throw new KeyNotFoundException("Recurso geoespacial desconhecido.");
 private async Task<T> Boundary<T>(string operation,Func<Task<T>> op){try{return await op();}catch(Exception ex){ApiLogMessages.GeospatialBoundaryFailed(logger,operation,ex);throw;}}
}
