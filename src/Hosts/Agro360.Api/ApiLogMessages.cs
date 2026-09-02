using Microsoft.Extensions.Logging;

namespace Agro360.Api;

internal static partial class ApiLogMessages
{
    [LoggerMessage(2001, LogLevel.Error, "Critical Rural HR API boundary failed")]
    internal static partial void RuralHrBoundaryFailed(ILogger logger, Exception exception);
    [LoggerMessage(2002, LogLevel.Information, "Consultando dashboard financeiro")]
    internal static partial void FinanceDashboardRequested(ILogger logger);
    [LoggerMessage(2003, LogLevel.Error, "Falha ao cadastrar conformidade ambiental")]
    internal static partial void SustainabilityRegistrationFailed(ILogger logger, Exception exception);
    [LoggerMessage(2004, LogLevel.Error, "Cooperative API boundary failed")]
    internal static partial void CooperativeBoundaryFailed(ILogger logger, Exception exception);
    [LoggerMessage(2005, LogLevel.Error, "Geospatial boundary failed: {Operation}")]
    internal static partial void GeospatialBoundaryFailed(ILogger logger, string operation, Exception exception);
    [LoggerMessage(2006, LogLevel.Error, "Fleet operation failed: {Operation}")]
    internal static partial void FleetOperationFailed(ILogger logger, string operation, Exception exception);
    [LoggerMessage(2007, LogLevel.Error, "SST operation failed: {Operation}")]
    internal static partial void SstOperationFailed(ILogger logger, string operation, Exception exception);
    [LoggerMessage(2008, LogLevel.Error, "Support operation failed: {Operation}")]
    internal static partial void SupportOperationFailed(ILogger logger, string operation, Exception exception);
    [LoggerMessage(2009, LogLevel.Error, "Integration boundary failed: {Operation}")]
    internal static partial void IntegrationBoundaryFailed(ILogger logger, string operation, Exception exception);
    [LoggerMessage(2010, LogLevel.Error, "Compliance boundary failed: {Operation}")]
    internal static partial void ComplianceBoundaryFailed(ILogger logger, string operation, Exception exception);
}
