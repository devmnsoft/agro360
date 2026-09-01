namespace Agro360.ArchitectureTests;

public sealed class Sprint22CommercialTests
{
 private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
 [Fact]public void Full_sql_contains_commercial_schema(){var sql=File.ReadAllText(Path.Combine(Root,"database/agro360-postgres-full.sql"));Assert.Contains("create table if not exists agro360.crm_customers",sql);Assert.Contains("create table if not exists agro360.sales_commissions",sql);Assert.Contains("create table if not exists agro360.sales_split_participants",sql);Assert.DoesNotContain("\\i ",sql);}
 [Fact]public void Commercial_form_never_requests_technical_ids(){var view=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Web/Pages/Commercial/Index.cshtml"));Assert.DoesNotContain("tenant_id",view,StringComparison.OrdinalIgnoreCase);Assert.Contains("data-lookup=\"segments\"",view);Assert.Contains("data-lookup=\"representatives\"",view);}
 [Fact]public void Dapper_queries_are_tenant_scoped(){var service=File.ReadAllText(Path.Combine(Root,"src/Modules/Agro360.Infrastructure/Services/Commercial360Service.cs"));Assert.Contains("tenant_id=@TenantId",service);Assert.DoesNotContain("select *",service,StringComparison.OrdinalIgnoreCase);}
}
