namespace Agro360.ArchitectureTests;
public sealed class Sprint47CrmTests
{
 private static readonly string Root=FindRoot();
 [Fact] public void Full_database_contains_tenant_isolated_crm(){var sql=File.ReadAllText(Path.Combine(Root,"database/agro360-postgres-full.sql"));Assert.Contains("agro360.commercial_crm_leads",sql);Assert.Contains("enable row level security",sql);Assert.Contains("agro360.commercial_commercial_contracts",sql);Assert.Contains("agro360.support_customer_success_health_scores",sql);}
 [Fact] public void Main_crm_screen_has_contextual_help(){var razor=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Web/Pages/Crm/Index.cshtml"));Assert.Contains("Como usar esta tela",razor);Assert.Contains("data-action=\"new-lead\"",razor);}
 [Fact] public void Dapper_service_always_filters_tenant(){var code=File.ReadAllText(Path.Combine(Root,"src/Modules/Agro360.Infrastructure/Services/CrmSaasService.cs"));Assert.Contains("where tenant_id=@TenantId",code);Assert.Contains("SaasCommercialRules.ProposalTotal",code);}
 [Fact] public void Controller_protects_main_mutations(){var code=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Api/Controllers/CrmSaasController.cs"));Assert.Contains("Permissions.CrmRead",code);Assert.Contains("Permissions.CrmWrite",code);Assert.Contains("Permissions.CommercialSaasWrite",code);}
 private static string FindRoot(){var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"MNSOFT.Agro360.sln")))dir=dir.Parent;return dir?.FullName??throw new DirectoryNotFoundException();}
}
