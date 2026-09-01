namespace Agro360.IntegrationTests;
public sealed class ComplianceFoundationTests
{
 private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
 private static string Sql=>File.ReadAllText(Path.Combine(Root,"database/agro360-postgres-full.sql"));
 }
