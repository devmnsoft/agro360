namespace Agro360.ArchitectureTests;

public sealed class ComplianceFormTests
{
    [Fact]
    public void ComplianceFormUsesLookupsAndValidation()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var html = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Compliance/Index.cshtml"));
        var js = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/wwwroot/js/compliance.js"));
        Assert.DoesNotContain("ID técnico", html + js, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-resource", js);
        Assert.Contains("reportValidity", js);
        Assert.Contains("Carregando", js);
        Assert.Contains("Nenhum registro", js);
    }

    [Fact]
    public void PublicCertificateRouteIsConstrainedAndAnonymous()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var controller = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Api/Controllers/ComplianceControllers.cs"));
        Assert.Contains("certificate:regex(^[[A-Fa-f0-9]]{{20}}$)", controller);
        Assert.Contains("AllowAnonymous", controller);
    }

    [Fact]
    public void QualityEvolutionKeepsIndependentRestrictionsAndAuditableDecisions()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var sql = File.ReadAllText(Path.Combine(root, "database/migrations/090_quality_nonconformity_capa.sql"));
        Assert.Contains("compliance_lot_restrictions", sql);
        Assert.Contains("where released_at is null", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compliance_nc_action_due_history", sql);
        Assert.Contains("compliance_nc_verifications", sql);
        Assert.Contains("idempotency_key", sql);
        Assert.Contains("version bigint", sql);
        Assert.DoesNotContain("on delete cascade", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EffectivenessVerificationIsVersionedIdempotentAndTenantScoped()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/091_quality_effectiveness_verification.sql"));
        var service = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/ComplianceService.cs"));
        var rules = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Domain/Compliance/QualityComplianceRules.cs"));
        Assert.Contains("criterion_version", migration);
        Assert.Contains("idempotency_key", migration);
        Assert.Contains("deleted_at", migration);
        Assert.Contains("tenant_id=@TenantId", service);
        Assert.Contains("ExpectedCaseVersion", service);
        Assert.Contains("actionSnapshot", service);
        Assert.Contains("Dictionary<string, string[]> AllowedTransitions", rules);
        Assert.Contains("Array.AsReadOnly", rules);
    }

    [Fact]
    public void EffectivenessScreenExplainsDecisionAndSupportsAccessibleRecovery()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var html = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Compliance/Index.cshtml"));
        var js = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/wwwroot/js/compliance.js"));
        Assert.Contains("Verificações pendentes", html);
        Assert.Contains("executar uma ação não comprova sua eficácia", js, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reportValidity", js);
        Assert.Contains("role=\"alert\"", js);
        Assert.Contains("expectedCaseVersion", js);
        Assert.Contains("crypto.randomUUID", js);
    }
}
