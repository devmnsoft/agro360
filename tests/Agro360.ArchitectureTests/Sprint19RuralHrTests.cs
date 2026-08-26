namespace Agro360.ArchitectureTests;
public sealed class Sprint19RuralHrTests
{
 private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
 [Fact]public void Api_exposes_all_required_resources_and_delegates(){var c=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Api/Controllers/RuralHrController.cs"));Assert.Contains("IRuralHrService",c);Assert.DoesNotContain("Dapper",c);foreach(var x in new[]{"people","teams","time-entries","allocations","labor-costs","trainings","ppe","safety-risks","incidents","corrective-actions","accommodations","transport","dashboard"})Assert.Contains($"\"{x}\"",c);}
 [Fact]public void Every_screen_exists_and_forms_use_lookups(){var h=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Web/Pages/RuralHr/Index.cshtml"));foreach(var x in new[]{"Pessoas","Equipes","Jornada","Alocações","Custos de Mão de Obra","Treinamentos","EPIs","Segurança","Incidentes","Ações Corretivas","Alojamento","Transporte","Dashboard RH Rural/SST"})Assert.Contains(x,h);Assert.DoesNotContain("name=\"id\"",h,StringComparison.OrdinalIgnoreCase);Assert.Contains("data-lookup=\"people\"",h);Assert.Contains("data-lookup=\"properties\"",h);Assert.Contains("required",h);}
 [Fact]public void Database_is_standalone_and_multitenant(){var s=File.ReadAllText(Path.Combine(Root,"database/agro360-postgres-full.sql"));Assert.Contains("create schema if not exists rural_hr",s);Assert.Contains("unique(tenant_id,document)",s);Assert.Contains("force row level security",s);Assert.DoesNotContain("\\i",s);}
}
