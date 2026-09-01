namespace Agro360.ArchitectureTests;
public sealed class Sprint40FiscalTests
{
 [Fact] public void FiscalDeliveryHasRequiredLayersAndTenantGuards()
 {
  var root=FindRoot();
  Required(root,"src/Modules/Agro360.Domain/Fiscal/FiscalRules.cs","src/Modules/Agro360.Application/Contracts/FiscalContracts.cs","src/Modules/Agro360.Infrastructure/Services/FiscalService.cs","src/Hosts/Agro360.Api/Controllers/FiscalController.cs","src/Hosts/Agro360.Web/Pages/Fiscal/Index.cshtml","database/migrations/040_sprint40_fiscal_billing.sql","docs/FISCAL-BILLING.md");
  var service=File.ReadAllText(Path.Combine(root,"src/Modules/Agro360.Infrastructure/Services/FiscalService.cs"));Assert.Contains("tenant_id=@TenantId",service);Assert.Contains("decimal",File.ReadAllText(Path.Combine(root,"src/Modules/Agro360.Application/Contracts/FiscalContracts.cs")));
 }
 private static void Required(string root,params string[] paths){foreach(var p in paths)Assert.True(File.Exists(Path.Combine(root,p)),p);}
 private static string FindRoot(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"MNSOFT.Agro360.sln")))d=d.Parent;return d!.FullName;}
}
