using Agro360.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Middleware;

public sealed partial class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (status, type, title) = exception switch
        {
            AuthenticationException => (StatusCodes.Status401Unauthorized, "authentication_required", "Autenticação necessária"),
            ValidationException => (StatusCodes.Status400BadRequest, "validation_error", "Dados inválidos"),
            NotFoundException => (StatusCodes.Status404NotFound, "resource_not_found", "Recurso não encontrado"),
            ConflictException => (StatusCodes.Status409Conflict, "business_conflict", "Conflito de negócio"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "forbidden", "Acesso negado"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "business_rule", "Regra de negócio não atendida"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "invalid_request", "Requisição inválida"),
            _ => (StatusCodes.Status500InternalServerError, "internal_error", "Erro interno")
        };

        if (status >= 500)
        {
            LogUnhandledError(logger, context.TraceIdentifier, exception);
        }
        else
        {
            LogRejectedRequest(logger, context.TraceIdentifier, exception);
        }

        var problem = new ProblemDetails
        {
            Type = $"https://mnsoft.com.br/problems/{type}",
            Title = title,
            Status = status,
            Detail = status >= 500 ? "Não foi possível concluir a operação." : exception.Message,
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (exception is DomainException domainException)
        {
            problem.Extensions["code"] = domainException.Code;
        }

        if (exception is ValidationException validationException)
        {
            problem.Extensions["errors"] = validationException.Errors;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Erro não tratado. TraceId: {TraceId}")]
    private static partial void LogUnhandledError(ILogger logger, string traceId, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Requisição rejeitada. TraceId: {TraceId}")]
    private static partial void LogRejectedRequest(ILogger logger, string traceId, Exception exception);
}
