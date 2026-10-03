namespace Agro360.Application.Contracts;

public sealed record DocumentRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string Status { get; init; } = "";
    public string OriginalName { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = "";
    public int CurrentVersion { get; init; }
    public DateTimeOffset UploadedAt { get; init; }

    public DocumentRow() { }
    public DocumentRow(Guid Id, string Name, string TypeName, string Status, string OriginalName, long SizeBytes, string Sha256, int CurrentVersion, DateTimeOffset UploadedAt)
    {
        this.Id = Id; this.Name = Name; this.TypeName = TypeName; this.Status = Status; this.OriginalName = OriginalName; this.SizeBytes = SizeBytes; this.Sha256 = Sha256; this.CurrentVersion = CurrentVersion; this.UploadedAt = UploadedAt;
    }
    public DocumentRow(Guid Id, string Name, string TypeName, string Status, string OriginalName, long SizeBytes, string Sha256, int CurrentVersion, DateTime UploadedAt)
        : this(Id, Name, TypeName, Status, OriginalName, SizeBytes, Sha256, CurrentVersion, new DateTimeOffset(UploadedAt)) { }
}

public sealed record DocumentDetails(Guid Id, string Name, string? Description, Guid DocumentTypeId, string TypeName, string Status, IReadOnlyList<DocumentVersionRow> Versions, IReadOnlyList<DocumentLinkRow> Links, IReadOnlyList<string> Tags);

public sealed record DocumentVersionRow
{
    public Guid Id { get; init; }
    public int VersionNumber { get; init; }
    public string OriginalName { get; init; } = "";
    public long SizeBytes { get; init; }
    public string MimeType { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string? ChangeReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public DocumentVersionRow() { }
    public DocumentVersionRow(Guid Id, int VersionNumber, string OriginalName, long SizeBytes, string MimeType, string Sha256, string? ChangeReason, DateTimeOffset CreatedAt)
    {
        this.Id = Id; this.VersionNumber = VersionNumber; this.OriginalName = OriginalName; this.SizeBytes = SizeBytes; this.MimeType = MimeType; this.Sha256 = Sha256; this.ChangeReason = ChangeReason; this.CreatedAt = CreatedAt;
    }
    public DocumentVersionRow(Guid Id, int VersionNumber, string OriginalName, long SizeBytes, string MimeType, string Sha256, string? ChangeReason, DateTime CreatedAt)
        : this(Id, VersionNumber, OriginalName, SizeBytes, MimeType, Sha256, ChangeReason, new DateTimeOffset(CreatedAt)) { }
}

public sealed record DocumentLinkRow(Guid Id, string EntityType, string EntityLabel);
public sealed record LookupOption(Guid Id, string Label);
public sealed record StoredDownload(Stream Content, string MimeType, string FileName);

public sealed record EvidenceRow
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentName { get; init; } = "";
    public string Origin { get; init; } = "";
    public string Description { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset EventAt { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }

    public EvidenceRow() { }
    public EvidenceRow(Guid Id, Guid DocumentId, string DocumentName, string Origin, string Description, string Status, DateTimeOffset EventAt, decimal? Latitude, decimal? Longitude)
    {
        this.Id = Id; this.DocumentId = DocumentId; this.DocumentName = DocumentName; this.Origin = Origin; this.Description = Description; this.Status = Status; this.EventAt = EventAt; this.Latitude = Latitude; this.Longitude = Longitude;
    }
    public EvidenceRow(Guid Id, Guid DocumentId, string DocumentName, string Origin, string Description, string Status, DateTime EventAt, decimal? Latitude, decimal? Longitude)
        : this(Id, DocumentId, DocumentName, Origin, Description, Status, new DateTimeOffset(EventAt), Latitude, Longitude) { }
}

public sealed record DossierRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset OpenedAt { get; init; }
    public int ItemCount { get; init; }
    public int PendingChecklist { get; init; }

    public DossierRow() { }
    public DossierRow(Guid Id, string Name, string Type, string Status, DateTimeOffset OpenedAt, int ItemCount, int PendingChecklist)
    {
        this.Id = Id; this.Name = Name; this.Type = Type; this.Status = Status; this.OpenedAt = OpenedAt; this.ItemCount = ItemCount; this.PendingChecklist = PendingChecklist;
    }
    public DossierRow(Guid Id, string Name, string Type, string Status, DateTime OpenedAt, int ItemCount, int PendingChecklist)
        : this(Id, Name, Type, Status, new DateTimeOffset(OpenedAt), ItemCount, PendingChecklist) { }
}

public sealed record CertificateRow
{
    public Guid Id { get; init; }
    public string PublicCode { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
    public string OrganizationName { get; init; } = "";
    public string SubjectSummary { get; init; } = "";
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ValidUntil { get; init; }
    public string VerificationHash { get; init; } = "";

    public CertificateRow() { }
    public CertificateRow(Guid Id, string PublicCode, string Type, string Status, string OrganizationName, string SubjectSummary, DateTimeOffset IssuedAt, DateTimeOffset? ValidUntil, string VerificationHash)
    {
        this.Id = Id; this.PublicCode = PublicCode; this.Type = Type; this.Status = Status; this.OrganizationName = OrganizationName; this.SubjectSummary = SubjectSummary; this.IssuedAt = IssuedAt; this.ValidUntil = ValidUntil; this.VerificationHash = VerificationHash;
    }
    public CertificateRow(Guid Id, string PublicCode, string Type, string Status, string OrganizationName, string SubjectSummary, DateTime IssuedAt, DateTime? ValidUntil, string VerificationHash)
        : this(Id, PublicCode, Type, Status, OrganizationName, SubjectSummary, new DateTimeOffset(IssuedAt), ValidUntil.HasValue ? new DateTimeOffset(ValidUntil.Value) : null, VerificationHash) { }
}

public sealed record PublicCertificate
{
    public string PublicCode { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
    public string OrganizationName { get; init; } = "";
    public string SubjectSummary { get; init; } = "";
    public string TraceabilitySummary { get; init; } = "";
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ValidUntil { get; init; }
    public string VerificationHash { get; init; } = "";

    public PublicCertificate() { }
    public PublicCertificate(string PublicCode, string Type, string Status, string OrganizationName, string SubjectSummary, string TraceabilitySummary, DateTimeOffset IssuedAt, DateTimeOffset? ValidUntil, string VerificationHash)
    {
        this.PublicCode = PublicCode; this.Type = Type; this.Status = Status; this.OrganizationName = OrganizationName; this.SubjectSummary = SubjectSummary; this.TraceabilitySummary = TraceabilitySummary; this.IssuedAt = IssuedAt; this.ValidUntil = ValidUntil; this.VerificationHash = VerificationHash;
    }
    public PublicCertificate(string PublicCode, string Type, string Status, string OrganizationName, string SubjectSummary, string TraceabilitySummary, DateTime IssuedAt, DateTime? ValidUntil, string VerificationHash)
        : this(PublicCode, Type, Status, OrganizationName, SubjectSummary, TraceabilitySummary, new DateTimeOffset(IssuedAt), ValidUntil.HasValue ? new DateTimeOffset(ValidUntil.Value) : null, VerificationHash) { }
}
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
