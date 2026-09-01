namespace Agro360.ArchitectureTests;
public sealed class Sprint27PortalTests
{
 private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
 [Fact] public void Portal_is_separate_and_has_real_actions(){var js=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Web/wwwroot/js/agro360.portal_js"));foreach(var route in new[]{"/api/portal/access/login","/api/portal/dashboard","/api/portal/marketplace","/api/portal/requests"})Assert.Contains(route,js);}
 [Fact] public void Full_database_contains_sprint_tables(){var sql=File.ReadAllText(Path.Combine(Root,"database/agro360-postgres-full.sql"));foreach(var table in new[]{"portal_external_users","portal_invitations","portal_requests","marketplace_listings","marketplace_quote_requests","transporter_delivery_updates","external_audit_events"})Assert.Contains(table,sql);Assert.Contains("enable row level security",sql);}
 [Fact] public void External_token_has_no_internal_permission(){var service=File.ReadAllText(Path.Combine(Root,"src/Modules/Agro360.Infrastructure/Services/PortalService.cs"));Assert.Contains("Permissions.PortalAccess",service);Assert.DoesNotContain("Permissions.Administrator",service);}
 [Fact] public void Relationships_are_selected_without_id_fields(){var pages=Directory.GetFiles(Path.Combine(Root,"src/Hosts/Agro360.Web/Pages/Portal"),"*.cshtml");foreach(var page in pages){var html=File.ReadAllText(page);Assert.DoesNotContain("name=\"tenantId\"",html,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("name=\"entityId\"",html,StringComparison.OrdinalIgnoreCase);}}
}
