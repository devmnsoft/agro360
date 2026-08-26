namespace Agro360.Application.Contracts;

public sealed record DocumentRow(Guid Id, string Name, string TypeName, string Status, string OriginalName, long SizeBytes, string Sha256, int CurrentVersion, DateTimeOffset UploadedAt);
public sealed record DocumentDetails(Guid Id, string Name, string? Description, Guid DocumentTypeId, string TypeName, string Status, IReadOnlyList<DocumentVersionRow> Versions, IReadOnlyList<DocumentLinkRow> Links, IReadOnlyList<string> Tags);
public sealed record DocumentVersionRow(Guid Id, int VersionNumber, string OriginalName, long SizeBytes, string MimeType, string Sha256, string? ChangeReason, DateTimeOffset CreatedAt);
public sealed record DocumentLinkRow(Guid Id, string EntityType, string EntityLabel);
public sealed record LookupOption(Guid Id, string Label);
public sealed record StoredDownload(Stream Content, string MimeType, string FileName);
public sealed record EvidenceRow(Guid Id, Guid DocumentId, string DocumentName, string Origin, string Description, string Status, DateTimeOffset EventAt, decimal? Latitude, decimal? Longitude);
public sealed record DossierRow(Guid Id, string Name, string Type, string Status, DateTimeOffset OpenedAt, int ItemCount, int PendingChecklist);
public sealed record CertificateRow(Guid Id, string PublicCode, string Type, string Status, string OrganizationName, string SubjectSummary, DateTimeOffset IssuedAt, DateTimeOffset? ValidUntil, string VerificationHash);
public sealed record PublicCertificate(string PublicCode, string Type, string Status, string OrganizationName, string SubjectSummary, string TraceabilitySummary, DateTimeOffset IssuedAt, DateTimeOffset? ValidUntil, string VerificationHash);
public sealed record DocumentDashboard(long TotalDocuments, long UnlinkedDocuments, long PendingEvidences, long ValidatedEvidences, long RejectedEvidences, long BuildingDossiers, long ReviewingDossiers, long ApprovedDossiers, long IssuedCertificates, long RevokedCertificates, IReadOnlyList<DocumentRow> LatestDocuments, IReadOnlyList<CertificateRow> LatestCertificates);
public sealed record UploadDocumentCommand(string Name, string? Description, Guid DocumentTypeId, string? Tags, string? EntityType, Guid? EntityId);
public sealed record CreateEvidenceCommand(Guid DocumentId, string Origin, string Description, DateTimeOffset EventAt, decimal? Latitude, decimal? Longitude, string? Tags);
public sealed record ValidateEvidenceCommand(string Status, string? Reason);
public sealed record CreateDossierCommand(string Name, string Type, string? EntityType, Guid? EntityId, string? Notes, IReadOnlyList<string> Checklist);
public sealed record DossierDecisionCommand(string Status, string? Reason);
public sealed record IssueCertificateCommand(string Type, Guid? DossierId, string EntityType, Guid EntityId, string SubjectSummary, string TraceabilitySummary, DateTimeOffset? ValidUntil);

public interface IDocumentService
{
    Task<DocumentDashboard> DashboardAsync(CancellationToken ct); Task<IReadOnlyList<DocumentRow>> DocumentsAsync(string? search, Guid? typeId, string? status, CancellationToken ct);
    Task<Guid> UploadAsync(UploadDocumentCommand command, Stream content, string originalName, string mimeType, long length, CancellationToken ct);
    Task AddVersionAsync(Guid documentId, Stream content, string originalName, string mimeType, long length, string reason, CancellationToken ct);
    Task<DocumentDetails?> DocumentAsync(Guid id, CancellationToken ct); Task<StoredDownload> DownloadAsync(Guid documentId, Guid? versionId, CancellationToken ct);
    Task ArchiveAsync(Guid id, CancellationToken ct); Task<IReadOnlyList<LookupOption>> DocumentTypesAsync(CancellationToken ct); Task<IReadOnlyList<LookupOption>> EntityLookupAsync(string entityType, string? search, CancellationToken ct);
    Task<IReadOnlyList<EvidenceRow>> EvidencesAsync(string? status, CancellationToken ct); Task<Guid> CreateEvidenceAsync(CreateEvidenceCommand command, CancellationToken ct); Task ValidateEvidenceAsync(Guid id, ValidateEvidenceCommand command, CancellationToken ct);
    Task<IReadOnlyList<DossierRow>> DossiersAsync(CancellationToken ct); Task<Guid> CreateDossierAsync(CreateDossierCommand command, CancellationToken ct); Task DecideDossierAsync(Guid id, DossierDecisionCommand command, CancellationToken ct);
    Task<IReadOnlyList<CertificateRow>> CertificatesAsync(CancellationToken ct); Task<CertificateRow> IssueCertificateAsync(IssueCertificateCommand command, CancellationToken ct); Task RevokeCertificateAsync(Guid id, string reason, CancellationToken ct);
    Task<PublicCertificate?> PublicCertificateAsync(string code, string? remoteAddress, CancellationToken ct); Task<byte[]> ExportCsvAsync(string resource, CancellationToken ct);
}
