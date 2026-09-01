namespace Agro360.ArchitectureTests;

public sealed class Sprint49WorkflowTests
{
    [Fact] public void MigrationHasIdempotencyIndexesAndRls()
    {
        var sql=File.ReadAllText(Find("database/migrations/049_workflow_automation.sql"));
        Assert.Contains("unique(tenant_id,rule_id,idempotency_key)",sql);
        Assert.Contains("PENDING_NOT_CONFIGURED",sql);
        Assert.Contains("enable row level security",sql);
        Assert.Contains("ix_agenda_range",sql);
    }

    [Fact] public void FullInstallContainsSprint49()=>Assert.Contains("4.9.0",File.ReadAllText(Find("database/agro360-postgres-full.sql")));

    [Fact] public void ProcessCentralHasContextualHelpAndRealApi()
    {
        Assert.Contains("Como usar esta tela",File.ReadAllText(Find("src/Hosts/Agro360.Web/Pages/Work/Index.cshtml")));
        Assert.Contains("/api/tasks",File.ReadAllText(Find("src/Hosts/Agro360.Web/wwwroot/js/work.js")));
        Assert.Contains("/api/workflows",File.ReadAllText(Find("src/Hosts/Agro360.Api/Controllers/WorkManagementController.cs")));
    }

    private static string Find(string relative){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"MNSOFT.Agro360.sln")))d=d.Parent;return Path.Combine(d!.FullName,relative);}
}
