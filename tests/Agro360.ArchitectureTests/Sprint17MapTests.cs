namespace Agro360.ArchitectureTests;
public sealed class Sprint17MapTests
{
 private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
 [Fact] public void Forms_never_request_technical_ids(){var html=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Web/Pages/Maps/Index.cshtml"));Assert.DoesNotContain("name=\"id\"",html,StringComparison.OrdinalIgnoreCase);Assert.Contains("data-lookup=\"users\"",html);Assert.Contains("data-feature-form",html);}
 [Fact] public void Endpoints_are_secured_and_controllers_delegate(){var code=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Api/Controllers/MapsController.cs"));Assert.Contains("Authorize(Policy=Permissions.MapsRead)",code);Assert.Contains("IGeospatialService service",code);Assert.DoesNotContain("Dapper",code);}
 [Fact] public void Full_sql_is_postgresql_only_and_multitenant(){var sql=File.ReadAllText(Path.Combine(Root,"database/agro360-postgres-full.sql"));Assert.Contains("create schema if not exists geospatial",sql);Assert.Contains("enable row level security",sql);Assert.Contains("geojson jsonb",sql);Assert.DoesNotContain("postgis",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("\\i",sql);}
 [Fact] public void Ui_has_all_real_sprint_screens(){var html=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Web/Pages/Maps/Index.cshtml"));foreach(var screen in new[]{"Mapa operacional","Camadas do mapa","Editar propriedade","Editar talhão","Pastagem / piquete","Zonas de manejo","Ocorrências geolocalizadas","Importação GeoJSON","Exportação GeoJSON","Rotas regionais","Monitoramento territorial","Dashboard geoespacial"})Assert.Contains(screen,html);}
}
