namespace Agro360.ArchitectureTests;
public sealed class Sprint37ProcurementTests
{
 private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
 [Fact] public void Full_installer_contains_portable_procurement(){var sql=File.ReadAllText(Path.Combine(Root,"database/agro360-postgres-full.sql"));Assert.Contains("create schema if not exists procurement",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("\\i ",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("force row level security",sql,StringComparison.OrdinalIgnoreCase);}
 [Fact] public void Form_never_requests_technical_identifiers(){var view=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Web/Pages/Procurement/Index.cshtml"));Assert.DoesNotContain("type=\"text\" name=\"supplierId\"",view,StringComparison.OrdinalIgnoreCase);Assert.Contains("data-lookup=\"suppliers\"",view);Assert.Contains("data-lookup=\"catalog\"",view);}
 [Fact] public void Api_has_policies_and_no_controller_sql(){var controller=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Api/Controllers/ProcurementController.cs"));Assert.Contains("Authorize(Policy=Permissions.PurchasingReceive)",controller);Assert.DoesNotContain("select ",controller,StringComparison.OrdinalIgnoreCase);}
}
