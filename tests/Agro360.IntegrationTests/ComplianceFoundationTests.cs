namespace Agro360.IntegrationTests;
public sealed class ComplianceFoundationTests
{
 private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
 private static string Sql=>File.ReadAllText(Path.Combine(Root,"database/agro360-postgres-full.sql"));
 [Fact] public void Sprint15_schema_is_multitenant_and_rls_protected(){Assert.Contains("create schema if not exists compliance",Sql);Assert.Contains("force row level security",Sql);Assert.Contains("app.tenant_id",Sql);}
 [Fact] public void Permissions_are_registered(){Assert.Contains("compliance.approve",Sql);Assert.Contains("esg.write",Sql);}
 [Fact] public void Dossier_has_secure_public_verification(){Assert.Contains("security definer",Sql);Assert.Contains("public_token_hash char(64)",Sql);Assert.Contains("digest(",Sql);}
 [Fact] public void Full_sql_has_no_include_or_fixed_credentials(){Assert.DoesNotContain("\\i ",Sql);Assert.DoesNotContain("Password=",Sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("Host=",Sql,StringComparison.OrdinalIgnoreCase);}
}
