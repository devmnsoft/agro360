namespace Agro360.ArchitectureTests;
public sealed class Sprint48GovernanceTests
{
    [Fact] public void Migration_has_tenant_indexes_and_rls(){var sql=File.ReadAllText(Find("database/migrations/048_data_governance.sql"));Assert.Contains("tenant_id",sql);Assert.Contains("enable row level security",sql);Assert.Contains("ix_import_batches_tenant_status_date",sql);}
    [Fact] public void Governance_screen_has_contextual_help_and_real_endpoint(){var root=Find("src/Hosts/Agro360.Web/Pages/Governance/Index.cshtml");Assert.Contains("Como usar esta tela",File.ReadAllText(root));Assert.Contains("/api/governance/imports",File.ReadAllText(Find("src/Hosts/Agro360.Web/wwwroot/js/governance.js")));}
    private static string Find(string relative){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"MNSOFT.Agro360.sln")))d=d.Parent;return Path.Combine(d!.FullName,relative);}
}
