using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

internal static partial class InfrastructureLogMessages
{
    [LoggerMessage(1001, LogLevel.Error, "Falha na fronteira do ledger do tenant {TenantId}")]
    internal static partial void LedgerBoundaryFailed(ILogger logger, Guid tenantId, Exception exception);
    [LoggerMessage(1002, LogLevel.Error, "Falha na consulta pública de certificado {Code}")]
    internal static partial void PublicCertificateFailed(ILogger logger, string code, Exception exception);
    [LoggerMessage(1003, LogLevel.Error, "Falha na fronteira Sprint 10 do tenant {TenantId}")]
    internal static partial void SprintBoundaryFailed(ILogger logger, Guid tenantId, Exception exception);
    [LoggerMessage(1004, LogLevel.Error, "Falha na fronteira financeira do tenant {TenantId}")]
    internal static partial void FinanceBoundaryFailed(ILogger logger, Guid tenantId, Exception exception);
    [LoggerMessage(1005, LogLevel.Information, "Ordem {OrderId} mudou de {From} para {To}")]
    internal static partial void ProductionOrderStatusChanged(ILogger logger, Guid orderId, string from, string to);
    [LoggerMessage(1006, LogLevel.Error, "Falha na fronteira mobile do tenant {TenantId}")]
    internal static partial void MobileBoundaryFailed(ILogger logger, Guid tenantId, Exception exception);
    [LoggerMessage(1007, LogLevel.Error, "Falha em inteligência operacional {Operation} para tenant {TenantId}")]
    internal static partial void OperationalIntelligenceFailed(ILogger logger, string operation, Guid tenantId, Exception exception);
    [LoggerMessage(1008, LogLevel.Information, "Convite externo {InvitationId} registrado para o tenant {TenantId}; entrega delegada à outbox")]
    internal static partial void PortalInvitationRegistered(ILogger logger, Guid invitationId, Guid tenantId);
    [LoggerMessage(1009, LogLevel.Information, "Onboarding {OnboardingId} concluiu organização {TenantId} no segmento {Segment}")]
    internal static partial void OnboardingCompleted(ILogger logger, Guid onboardingId, Guid tenantId, string segment);
    [LoggerMessage(1010, LogLevel.Error, "Falha logística do tenant {TenantId}")]
    internal static partial void LogisticsFailed(ILogger logger, Guid tenantId, Exception exception);
    [LoggerMessage(1011, LogLevel.Error, "Falha em contrato operacional do tenant {TenantId}")]
    internal static partial void OperationalContractFailed(ILogger logger, Guid tenantId, Exception exception);
    [LoggerMessage(1012, LogLevel.Error, "Falha no ecossistema do tenant {TenantId}")]
    internal static partial void EcosystemFailed(ILogger logger, Guid tenantId, Exception exception);
    [LoggerMessage(1013, LogLevel.Error, "Falha na fronteira de inteligência {Operation} para tenant {TenantId}")]
    internal static partial void IntelligenceFailed(ILogger logger, string operation, Guid tenantId, Exception exception);
    [LoggerMessage(1014, LogLevel.Information, "Pedido de compra aprovado {PurchaseOrderId}")]
    internal static partial void PurchaseOrderApproved(ILogger logger, Guid purchaseOrderId);
    [LoggerMessage(1015, LogLevel.Error, "Falha na fronteira de armazenagem do tenant {TenantId}")]
    internal static partial void StorageBoundaryFailed(ILogger logger, Guid tenantId, Exception exception);
    [LoggerMessage(1016, LogLevel.Error, "Falha na comercialização do tenant {TenantId}")]
    internal static partial void SalesFailed(ILogger logger, Guid tenantId, Exception exception);
    [LoggerMessage(1017, LogLevel.Error, "Falha ao armazenar documento {DocumentId} do tenant {TenantId}")]
    internal static partial void DocumentStorageFailed(ILogger logger, Guid documentId, Guid tenantId, Exception exception);
    [LoggerMessage(1018, LogLevel.Warning, "Falha ao limpar arquivo de upload incompleto {DocumentId}")]
    internal static partial void IncompleteUploadCleanupFailed(ILogger logger, Guid documentId, Exception exception);
    [LoggerMessage(1019, LogLevel.Error, "Falha ao criar versão de {DocumentId}")]
    internal static partial void DocumentVersionFailed(ILogger logger, Guid documentId, Exception exception);
    [LoggerMessage(1020, LogLevel.Error, "Falha em inteligência executiva {Operation} para tenant {TenantId}")]
    internal static partial void ExecutiveIntelligenceFailed(ILogger logger, string operation, Guid tenantId, Exception exception);
    [LoggerMessage(1021, LogLevel.Warning, "Limite de usuários violado no tenant {TenantId}")]
    internal static partial void UserLimitExceeded(ILogger logger, Guid tenantId);
    [LoggerMessage(1022, LogLevel.Error, "Falha SaaS {Operation} no tenant {TenantId}")]
    internal static partial void SaasFailed(ILogger logger, string operation, Guid tenantId, Exception exception);
}
