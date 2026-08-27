using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record OfflineCommand(string IdempotencyKey, string TemporaryId, string Type, string Payload, DateTimeOffset CreatedAt);
public sealed record SyncCommand(Guid DeviceId, Guid SessionId, IReadOnlyList<OfflineCommand> Commands);
public sealed record QuickRecordCommand(string Kind, Guid EntityId, string EntityType, decimal? Quantity, DateTimeOffset OccurredAt, string? Notes, decimal? Latitude, decimal? Longitude);
public sealed record EvidenceCommand(string Type, string EntityType, Guid EntityId, string FileName, string ContentType, string ContentBase64, string? Notes, decimal? Latitude, decimal? Longitude, DateTimeOffset CapturedAt);
public sealed record GeolocationCommand(string EntityType, Guid EntityId, string EventType, string Origin, decimal? Latitude, decimal? Longitude, decimal? Accuracy, DateTimeOffset OccurredAt);
public sealed record QrGenerateCommand(string EntityType, Guid EntityId, bool Public);
public sealed record ChecklistQuestionCommand(string Text, string ResponseType, bool Required, IReadOnlyList<string>? Options);
public sealed record ChecklistTemplateCommand(string Name, string Usage, bool Required, IReadOnlyList<ChecklistQuestionCommand> Questions);
public sealed record ChecklistApplyCommand(Guid TemplateId, string EntityType, Guid EntityId, Guid ResponsibleId);
public sealed record ChecklistAnswerCommand(Guid QuestionId, string? Value, Guid? EvidenceId);
public sealed record ChecklistCompleteCommand(IReadOnlyList<ChecklistAnswerCommand> Answers);
public sealed record FieldOccurrenceCommand(
    [Required, MaxLength(40)] string OccurrenceType,
    [Required, RegularExpression("LOW|MEDIUM|HIGH|CRITICAL")] string Severity,
    [Required, StringLength(160, MinimumLength=3)] string Title,
    [Required, StringLength(2000, MinimumLength=5)] string Description,
    string? EntityType, Guid? EntityId, decimal? Latitude, decimal? Longitude, DateTimeOffset OccurredAt);
public sealed record FieldCheckinCommand(
    [Required, MaxLength(40)] string OperationType, string? EntityType, Guid? EntityId,
    decimal? Latitude, decimal? Longitude, decimal? Accuracy,
    [Required, RegularExpression("GPS|MANUAL")] string LocationSource,
    [MaxLength(500)] string? ManualReason, [MaxLength(1000)] string? Observation, DateTimeOffset OccurredAt);

public interface IMobileService
{
    Task<dynamic> BootstrapAsync(Guid deviceId, CancellationToken ct); Task<dynamic> SyncAsync(SyncCommand command, CancellationToken ct);
    Task<dynamic> SyncStatusAsync(Guid deviceId, CancellationToken ct); Task<dynamic> DashboardAsync(CancellationToken ct);
    Task<Guid> QuickRecordAsync(string area, QuickRecordCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> EvidencesAsync(CancellationToken ct); Task<Guid> AddEvidenceAsync(EvidenceCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> LocationsAsync(CancellationToken ct); Task<Guid> AddLocationAsync(GeolocationCommand command, CancellationToken ct);
    Task<dynamic> GenerateQrAsync(QrGenerateCommand command, CancellationToken ct); Task<dynamic?> ResolveQrAsync(string code, bool authenticated, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> TemplatesAsync(CancellationToken ct); Task<Guid> AddTemplateAsync(ChecklistTemplateCommand command, CancellationToken ct);
    Task<Guid> ApplyChecklistAsync(ChecklistApplyCommand command, CancellationToken ct); Task CompleteChecklistAsync(Guid id, ChecklistCompleteCommand command, CancellationToken ct);
    Task<Guid> AddOccurrenceAsync(FieldOccurrenceCommand command, CancellationToken ct);
    Task<Guid> AddCheckinAsync(FieldCheckinCommand command, CancellationToken ct);
}
