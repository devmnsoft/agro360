namespace Agro360.ArchitectureTests;
public sealed class Sprint24WorkManagementTests
{
 [Fact] public void Full_sql_contains_sprint24_tables(){var sql=File.ReadAllText(Path.Combine(Root(),"database/agro360-postgres-full.sql"));foreach(var table in new[]{"operational_tasks","operational_alerts","operational_rules","workflow_instances","notifications","communication_outbox","calendar_events"})Assert.Contains(table,sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("\\i ",sql);}
 [Fact] public void Web_form_uses_user_lookup_not_manual_identifier(){var view=File.ReadAllText(Path.Combine(Root(),"src/Hosts/Agro360.Web/Pages/Work/Index.cshtml"));Assert.Contains("select name=\"responsibleId\"",view);Assert.DoesNotContain("type=\"text\" name=\"responsibleId\"",view);}
 private static string Root(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"MNSOFT.Agro360.sln")))d=d.Parent;return d?.FullName??throw new InvalidOperationException("Repository root not found.");}
}
