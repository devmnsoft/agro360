using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record IntegrationItem(Guid Id,string Name,string Type,string Provider,string Status,DateTimeOffset? LastSync,string? LastError,int Attempts);
public sealed record IntegrationCommand([Required,MaxLength(120)]string Name,[Required,MaxLength(40)]string Type,[Required,MaxLength(60)]string Provider,Guid? CredentialReferenceId);
public sealed record ApiKeyCommand([Required,MaxLength(120)]string Name,[Required,MinLength(1)]string[] Scopes,DateTimeOffset? ExpiresAt,[Range(1,10000)]int RateLimitPerMinute=60);
public sealed record ApiKeyCreated(Guid Id,string Key,string Prefix,DateTimeOffset? ExpiresAt);
public sealed record WebhookCommand([Required,Url]string Url,[Required,MinLength(1)]string[] Events,Guid? SigningCredentialReferenceId);
public sealed record ImportCommand([Required,RegularExpression("^(PRODUCERS|PROPERTIES|FIELDS|SUPPLIERS|STOCK_ITEMS|ANIMALS|MACHINES|CUSTOMERS)$")]string EntityType,[Required,MaxLength(240)]string FileName,[Required]string CsvContent);
public sealed record FiscalDocumentCommand([Required,RegularExpression("^(NFE|CTE|MDFE|PRODUCER_INVOICE|GTA|SANITARY_CERTIFICATE|REPORT)$")]string Type,[Required,MaxLength(240)]string FileName,[Required]string ContentBase64,Guid? PurchaseId,Guid? SaleId,Guid? ShipmentId,Guid? LotId,Guid? ProducerId,Guid? SupplierId);
public sealed record DeviceCommand([Required,MaxLength(120)]string Name,[Required,MaxLength(40)]string Type,[Required,MaxLength(40)]string SensorType,decimal? Minimum,decimal? Maximum);
public sealed record ReadingCommand([Required]string DeviceToken,[Required,MaxLength(40)]string SensorType,decimal Value,[Required,MaxLength(20)]string Unit,DateTimeOffset? RecordedAt,decimal? Latitude,decimal? Longitude);
public sealed record SplitCommand([Required]Guid SaleId,[Range(0.01,double.MaxValue)]decimal GrossAmount,[Required,MinLength(1)]SplitParticipant[] Participants);
public sealed record SplitParticipant([Required]Guid PartyId,[Range(0.01,100)]decimal Percentage);
public sealed record MessageCommand([Required,RegularExpression("^(IN_APP|EMAIL|SMS|WHATSAPP)$")]string Channel,[Required]Guid RecipientId,[Required,MaxLength(160)]string Subject,[Required,MaxLength(2000)]string Body);
public sealed record IntegrationDashboard(int ActiveIntegrations,int FailedIntegrations,int PendingWebhooks,int FailedImports,int OfflineSensors,int ExpiringApiKeys,int SentEvents,decimal AverageProcessingMilliseconds,int CriticalAlerts);

public interface IIntegrationService
{
 Task<IReadOnlyList<IntegrationItem>> ListAsync(CancellationToken ct); Task<Guid> SaveAsync(Guid? id,IntegrationCommand command,CancellationToken ct); Task SetStatusAsync(Guid id,bool active,CancellationToken ct); Task<object> LogsAsync(Guid id,CancellationToken ct);
 Task<ApiKeyCreated> CreateApiKeyAsync(ApiKeyCommand command,CancellationToken ct); Task<object> ApiKeysAsync(CancellationToken ct); Task RevokeApiKeyAsync(Guid id,CancellationToken ct);
 Task<Guid> CreateWebhookAsync(WebhookCommand command,CancellationToken ct); Task<object> WebhooksAsync(CancellationToken ct); Task<object> WebhookEventsAsync(CancellationToken ct); Task RetryWebhookAsync(Guid eventId,CancellationToken ct); Task CancelWebhookAsync(Guid eventId,CancellationToken ct);
 Task<Guid> CreateImportAsync(ImportCommand command,CancellationToken ct); Task<object> ImportsAsync(CancellationToken ct); Task<object> ValidateImportAsync(Guid id,CancellationToken ct); Task ConfirmImportAsync(Guid id,CancellationToken ct); Task<byte[]> ExportAsync(string entity,CancellationToken ct);
 Task<Guid> AddFiscalDocumentAsync(FiscalDocumentCommand command,CancellationToken ct); Task<object> FiscalDocumentsAsync(CancellationToken ct);
 Task<(Guid Id,string Token)> AddDeviceAsync(DeviceCommand command,CancellationToken ct); Task<object> DevicesAsync(CancellationToken ct); Task<Guid> AddReadingAsync(ReadingCommand command,CancellationToken ct);
 Task<Guid> CalculateSplitAsync(SplitCommand command,CancellationToken ct); Task ApproveSplitAsync(Guid id,CancellationToken ct); Task<object> SplitsAsync(CancellationToken ct);
 Task<Guid> EnqueueMessageAsync(MessageCommand command,CancellationToken ct); Task<object> OutboxAsync(CancellationToken ct); Task<IntegrationDashboard> DashboardAsync(CancellationToken ct);
}
