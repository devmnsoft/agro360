namespace Agro360.ArchitectureTests;
public sealed class Sprint39ExportTradingTests
{
 private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
 [Fact] public void RequiredLayersAndScreensArePresent(){foreach(var path in new[]{"src/Modules/Agro360.Domain/Export/ExportTradingRules.cs","src/Modules/Agro360.Application/Contracts/ExportTradingContracts.cs","src/Modules/Agro360.Infrastructure/Services/ExportTradingService.cs","src/Hosts/Agro360.Api/Controllers/ExportTradingController.cs","src/Hosts/Agro360.Web/Pages/Export/Index.cshtml","database/migrations/039_sprint39_export_trading.sql","docs/EXPORT-TRADING.md"})Assert.True(File.Exists(Path.Combine(Root,path)),path);}
 [Fact] public void SqlHasTenantConstraintsAndAllIncoterms(){var sql=File.ReadAllText(Path.Combine(Root,"database/migrations/039_sprint39_export_trading.sql"));Assert.Contains("tenant_id",sql);foreach(var term in new[]{"EXW","FCA","FAS","FOB","CFR","CIF","CPT","CIP","DAP","DPU","DDP"})Assert.Contains($"('{term}'",sql);}
}
