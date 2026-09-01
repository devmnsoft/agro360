namespace Agro360.ArchitectureTests;
public sealed class IntelligenceFormTests
{
 [Fact] public void Intelligence_ui_does_not_request_technical_ids()
 {
   var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
   var html=File.ReadAllText(Path.Combine(root,"src/Hosts/Agro360.Web/Pages/Intelligence/Index.cshtml"));
   Assert.DoesNotContain("name=\"farmId\" type=\"text\"",html,StringComparison.OrdinalIgnoreCase);
   Assert.DoesNotContain("type=\"hidden\" name=\"farmId\"",html,StringComparison.OrdinalIgnoreCase);
   Assert.Contains("<select name=\"farmId\"",html,StringComparison.OrdinalIgnoreCase);
 }
 [Fact] public void Full_sql_contains_sprint_13_and_no_includes()
 {
   var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
   var sql=File.ReadAllText(Path.Combine(root,"database/agro360-postgres-full.sql"));
   Assert.Contains("Sprint 13",sql); Assert.Contains("create schema if not exists agro360",sql);
   Assert.DoesNotContain("\\i ",sql,StringComparison.OrdinalIgnoreCase);
 }
}
