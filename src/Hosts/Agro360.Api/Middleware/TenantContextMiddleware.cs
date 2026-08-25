using System.Security.Claims;
using Agro360.Multitenancy;
using Microsoft.AspNetCore.Authorization;

namespace Agro360.Api.Middleware;

public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IMutableTenantContext tenantContext)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(context.User.FindFirstValue("tenant_id"), out var tenantId)
            || !Guid.TryParse(context.User.FindFirstValue("sub"), out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "invalid_token_context",
                title = "Token sem contexto de tenant",
                status = 401,
                traceId = context.TraceIdentifier
            }).ConfigureAwait(false);
            return;
        }

        var organizationId = ReadOptionalGuid(context, "X-Organization-ID");
        var farmId = ReadOptionalGuid(context, "X-Farm-ID");
        var timeZone = context.Request.Headers["X-Timezone"].FirstOrDefault() ?? "America/Belem";
        tenantContext.SetScope(new TenantScope(tenantId, userId, organizationId, farmId, timeZone));

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            tenantContext.Clear();
        }
    }

    private static Guid? ReadOptionalGuid(HttpContext context, string headerName)
    {
        var value = context.Request.Headers[headerName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Guid.TryParse(value, out var parsed))
        {
            throw new BadHttpRequestException($"O header {headerName} não possui UUID válido.");
        }

        return parsed;
    }
}
