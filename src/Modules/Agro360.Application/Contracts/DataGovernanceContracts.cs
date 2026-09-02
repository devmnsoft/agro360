namespace Agro360.Application.Contracts;

public sealed record ImportBatchCommand(string Module, string FileName, string Csv, IReadOnlyDictionary<string,string>? Mapping, bool ConfirmCriticalErrors = false);
public sealed record ImportBatch(Guid Id, string Module, string FileName, string Status, int TotalRows, int ValidRows, int ErrorRows, DateTimeOffset CreatedAt);
public sealed record ImportError(int RowNumber, string ColumnName, string Code, string Message, string Severity);
public sealed record FindingAction(string Status, string Justification);
public sealed record ExportRequestCommand(IReadOnlyCollection<string> Modules, DateOnly? From, DateOnly? To, string Format, string? Justification);
public sealed record LgpdRequestCommand(string Type, string SubjectName, string SubjectDocument, string LegalBasis, string Purpose);
public sealed record LgpdTransition(string Status, string? Reason);

public interface IDataGovernanceService
{
    Task<ImportBatch> CreateImportAsync(ImportBatchCommand command, CancellationToken ct);
    Task<PagedResult<ImportBatch>> ImportsAsync(int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<ImportError>> ErrorsAsync(Guid batchId, CancellationToken ct);
    Task ReprocessAsync(Guid batchId, CancellationToken ct);
    Task CancelAsync(Guid batchId, string reason, CancellationToken ct);
    Task<Guid> CreateExportAsync(ExportRequestCommand command, CancellationToken ct);
    Task<Guid> CreateLgpdAsync(LgpdRequestCommand command, CancellationToken ct);
    Task TransitionLgpdAsync(Guid id, LgpdTransition command, CancellationToken ct);
    Task ActOnFindingAsync(Guid id, FindingAction command, CancellationToken ct);
}
