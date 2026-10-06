using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Agro360.Api.Middleware;

/// <summary>
/// Produz respostas canônicas nas falhas de autorização da API.
/// Token Bearer presente e rejeitado (expirado/inválido) vira 401 JSON `token_invalid`;
/// anônimo sem token mantém o desafio padrão 401; negação de perfil autenticado
/// sem exceção vira 403 JSON `forbidden_authorization`. As negações definitivas com
/// motivo específico já viram 403 canônico via ForbiddenException lançada pelo handler
/// de permissões e renderizada pelo ExceptionHandlingMiddleware.
/// </summary>
public sealed class JsonAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        var response = context.Response;
        if (response.HasStarted)
        {
            return;
        }

        if (authorizeResult.Challenged)
        {
            var tokenPresent = context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
            if (tokenPresent)
            {
                response.StatusCode = StatusCodes.Status401Unauthorized;
                response.ContentType = "application/json";
                await response.WriteAsJsonAsync(new
                {
                    type = "token_invalid",
                    title = "Sessão expirada ou inválida.",
                    detail = "Faça login novamente para continuar.",
                    status = 401,
                    traceId = context.TraceIdentifier
                });
                return;
            }

            await context.ChallengeAsync();
            return;
        }

        if (authorizeResult.Forbidden)
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            response.ContentType = "application/json";
            await response.WriteAsJsonAsync(new
            {
                type = "forbidden_authorization",
                title = "Acesso não autorizado.",
                detail = "Seu perfil não permite executar esta operação.",
                status = 403,
                traceId = context.TraceIdentifier
            });
        }
    }
}
