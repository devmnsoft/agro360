using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record ComplianceDocument(Guid Id, string Type, string Number, string SubjectName, DateOnly IssuedOn, DateOnly ExpiresOn, string ResponsibleName, string Status, int EvidenceCount);
public sealed record DocumentCommand([Required, MaxLength(40)] string Type, [Required, MaxLength(80)] string Number, [Required] Guid SubjectId, [Required] DateOnly IssuedOn, [Required] DateOnly ExpiresOn, [Required] Guid ResponsibleId, [MaxLength(500)] string? Notes);
public sealed record ProductRule(Guid Id, string ProductName, string Market, string Name, bool Mandatory, bool BlocksSale, bool BlocksExport, string[] RequiredDocuments, string Status);
public sealed record ProductRuleCommand([Required] Guid ProductId, [Required, MaxLength(80)] string Market, [Required, MaxLength(160)] string Name, bool Mandatory, bool BlocksSale, bool BlocksExport, string[] RequiredDocuments);
public sealed record Certification(Guid Id, string Type, string HolderName, string Scope, DateOnly ValidUntil, string Status, int EvidenceCount);
public sealed record CertificationCommand([Required, MaxLength(100)] string Type, [Required] Guid HolderId, [Required, MaxLength(300)] string Scope, [Required] DateOnly ValidFrom, [Required] DateOnly ValidUntil, string[] Requirements);
public sealed record ChainAudit(Guid Id, string Scope, string EntityName, DateOnly ScheduledOn, string Status, decimal Score, int NonConformities);
public sealed record AuditCommand([Required, MaxLength(40)] string Scope, [Required] Guid EntityId, [Required] Guid TemplateId, [Required] DateOnly ScheduledOn, [Required] Guid ResponsibleId);
public sealed record NonConformity(Guid Id, string Title, string Classification, string Severity, string Origin, string EntityName, string Status, DateOnly DueOn, bool Overdue);
public sealed record NonConformityCommand([Required, MaxLength(180)] string Title, [Required, MaxLength(30)] string Classification, [Required, MaxLength(12)] string Severity, [Required, MaxLength(40)] string Origin, [Required, MaxLength(40)] string EntityType, [Required] Guid EntityId, [Required, MaxLength(1000)] string RootCause, [Required, MaxLength(1000)] string CorrectiveAction, [MaxLength(1000)] string? PreventiveAction, [Required] Guid ResponsibleId, [Required] DateOnly DueOn);
public sealed record EsgIndicator(Guid Id, string Pillar, string Name, decimal Value, string Unit, DateOnly Period, string Methodology);
public sealed record EsgIndicatorCommand([Required, MaxLength(20)] string Pillar, [Required, MaxLength(160)] string Name, [Range(0, double.MaxValue)] decimal Value, [Required, MaxLength(30)] string Unit, [Required] DateOnly Period, [Required, MaxLength(1000)] string Methodology);
public sealed record CarbonEntry(Guid Id, string Source, string Practice, decimal ActivityAmount, string Unit, decimal FactorKgCo2e, decimal SequestrationKgCo2e, decimal NetTonnesCo2e, DateOnly Period, string Methodology);
public sealed record CarbonCommand([Required, MaxLength(100)] string Source, [Required, MaxLength(100)] string Practice, [Range(0, double.MaxValue)] decimal ActivityAmount, [Required, MaxLength(30)] string Unit, [Range(0, double.MaxValue)] decimal FactorKgCo2e, [Range(0, double.MaxValue)] decimal SequestrationKgCo2e, [Required] DateOnly Period, [Required, MaxLength(1000)] string Methodology);
public sealed record ExportDossier(Guid Id, string CertificateCode, string LotName, string ProductName, string BuyerName, string Market, string Status, DateTimeOffset GeneratedAt);
public sealed record DossierCommand([Required] Guid LotId, [Required] Guid BuyerId, [Required, MaxLength(80)] string Market);
public sealed record ComplianceDashboard(int PendingInspections, int OpenNonConformities, int BlockedLots, int OverdueActions, int AwaitingVerification, int ReopenedCases, int PendingDocuments, int ExpiredDocuments, int ExpiringDocuments, int EligibleLots, int PendingAudits, decimal EsgScore, int ValidCertifications, int ExpiredCertifications, int ExportEligibleProducts, int CriticalRisks);
public sealed record EvidenceCommand([Required, MaxLength(240)] string FileName, [Required, Url] string StorageUrl, [Required, MaxLength(64)] string Sha256);
public sealed record ActionCommand([Required, MaxLength(500)] string Reason);
public sealed record NonConformityTransitionCommand([Required, MaxLength(500)] string Reason, long ExpectedVersion);
public sealed record NonConformityDetail(Guid Id, long Number, string Title, string Classification, string Severity, string Origin, string EntityName, string Status, Guid ResponsibleId, DateOnly DueOn, long Version, string? RootCause, IReadOnlyList<string> AvailableTransitions, int OpenActions, int CompletedActions, int VerificationCount, int ActiveRestrictions, string? LatestVerificationResult);
public sealed record EffectivenessVerification(Guid Id, Guid NonConformityId, string CriterionType, string Criterion, int CriterionVersion, string Method, Guid ResponsibleId, DateOnly DueOn, DateOnly? ObservationStartedOn, DateOnly? ObservationEndedOn, string EvidenceRequirements, decimal? ExpectedValue, decimal? ObservedValue, string? Unit, string? ComparisonOperator, decimal? PercentageBasis, string ResultObserved, string Conclusion, string Result, DateTimeOffset DecidedAt, Guid DecidedBy);
public sealed record EffectivenessVerificationCommand(
    [Required, MaxLength(16)] string CriterionType,
    [Required, MaxLength(1000)] string Criterion,
    [Required, MaxLength(500)] string Method,
    [Required] Guid ResponsibleId,
    [Required] DateOnly DueOn,
    DateOnly? ObservationStartedOn,
    DateOnly? ObservationEndedOn,
    [Required, MaxLength(1000)] string EvidenceRequirements,
    decimal? ExpectedValue,
    decimal? ObservedValue,
    [MaxLength(30)] string? Unit,
    [MaxLength(8)] string? ComparisonOperator,
    decimal? PercentageBasis,
    [Required, MaxLength(1000)] string ResultObserved,
    [Required, MaxLength(1000)] string Conclusion,
    [Required, MaxLength(16)] string Result,
    [Required, MaxLength(120)] string IdempotencyKey,
    long ExpectedCaseVersion);
public sealed record RelatedCase(Guid Id, long Number, string Title, string Status, string MatchReason, DateTimeOffset IdentifiedAt);
public sealed record RelatedCaseQuery(Guid? ProductId, Guid? LotId, Guid? UnitId, string? Classification, string? Origin, string? RootCause, DateOnly? From, DateOnly? To);
public sealed record RelatedCaseLinkCommand([Required] Guid RelatedNonConformityId, [Required, MaxLength(500)] string Justification, long ExpectedCaseVersion);
public sealed record PublicDossier(string CertificateCode, string Product, string Lot, string Origin, string Status, DateTimeOffset IssuedAt, string VerificationHash);

public interface IComplianceService
{
    Task<IReadOnlyList<ComplianceDocument>> DocumentsAsync(string? status, CancellationToken ct); Task<Guid> SaveDocumentAsync(Guid? id, DocumentCommand command, CancellationToken ct);
    Task<IReadOnlyList<ProductRule>> RulesAsync(Guid? productId, string? market, CancellationToken ct); Task<Guid> SaveRuleAsync(Guid? id, ProductRuleCommand command, CancellationToken ct); Task SetLotBlockAsync(Guid lotId, bool blocked, string reason, CancellationToken ct);
    Task<IReadOnlyList<Certification>> CertificationsAsync(string? status, CancellationToken ct); Task<Guid> SaveCertificationAsync(Guid? id, CertificationCommand command, CancellationToken ct); Task DecideCertificationAsync(Guid id, bool approve, string reason, CancellationToken ct);
    Task<IReadOnlyList<ChainAudit>> AuditsAsync(string? status, CancellationToken ct); Task<Guid> SaveAuditAsync(Guid? id, AuditCommand command, CancellationToken ct); Task CompleteAuditAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<NonConformity>> NonConformitiesAsync(string? status, CancellationToken ct); Task<Guid> SaveNonConformityAsync(Guid? id, NonConformityCommand command, CancellationToken ct); Task CloseNonConformityAsync(Guid id, string reason, long expectedVersion, CancellationToken ct);
    Task<NonConformityDetail> NonConformityAsync(Guid id, CancellationToken ct); Task<IReadOnlyList<EffectivenessVerification>> VerificationsAsync(Guid id, CancellationToken ct); Task<Guid> AddVerificationAsync(Guid id, EffectivenessVerificationCommand command, CancellationToken ct); Task<IReadOnlyList<RelatedCase>> RelatedCasesAsync(Guid id, RelatedCaseQuery query, CancellationToken ct); Task LinkRelatedCaseAsync(Guid id, RelatedCaseLinkCommand command, CancellationToken ct);
    Task AttachEvidenceAsync(string entityType, Guid entityId, EvidenceCommand command, CancellationToken ct);
    Task<IReadOnlyList<ExportDossier>> DossiersAsync(CancellationToken ct); Task<Guid> CreateDossierAsync(DossierCommand command, CancellationToken ct); Task<byte[]> ExportDossierAsync(Guid id, CancellationToken ct); Task<PublicDossier?> PublicDossierAsync(string certificate, CancellationToken ct);
    Task<ComplianceDashboard> DashboardAsync(CancellationToken ct);
}
public interface IEsgService { Task<IReadOnlyList<EsgIndicator>> IndicatorsAsync(CancellationToken ct); Task<Guid> AddIndicatorAsync(EsgIndicatorCommand command, CancellationToken ct); Task<IReadOnlyList<CarbonEntry>> CarbonAsync(CancellationToken ct); Task<Guid> AddCarbonAsync(CarbonCommand command, CancellationToken ct); }
