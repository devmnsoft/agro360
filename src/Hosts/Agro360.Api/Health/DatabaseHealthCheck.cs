using Agro360.Application.Abstractions;
using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Agro360.Api.Health;

public sealed class DatabaseHealthCheck(IDbConnectionFactory connectionFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            // Connectivity alone is not readiness: a newly created database
            // accepts SELECT 1 but cannot serve login or the initial dashboard.
            var schemaReady = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                with required(table_name, column_name) as (values
                    ('tenancy_tenants', 'status'),
                    ('identity_users', 'password_hash'),
                    ('identity_users', 'normalized_document'),
                    ('identity_refresh_tokens', 'revoked_at'),
                    ('identity_user_roles', 'role_id'),
                    ('identity_roles', 'code'),
                    ('identity_role_permissions', 'permission_id'),
                    ('identity_permissions', 'code'),
                    ('saas_organizations', 'plan_id'),
                    ('saas_plans', 'modules'),
                    ('platform_tenant_module_entitlements', 'status'),
                    ('platform_module_catalog', 'code'),
                    ('platform_tenant_modules', 'trial_ends_at'),
                    ('platform_marketplace_modules', 'code'),
                    ('geo_farms', 'total_area_ha'),
                    ('agriculture_seasons', 'status'),
                    ('livestock_animals', 'status'),
                    ('inventory_stock_balances', 'available'),
                    ('inventory_warehouses', 'farm_id'),
                    ('finance_commercial_receivables', 'paid_amount'),
                    ('cost_entries', 'amount'),
                    ('notification_alerts', 'severity')
                )
                select not exists (
                    select 1 from required r
                    where not exists (
                        select 1 from information_schema.columns c
                        where c.table_schema = 'agro360'
                          and c.table_name = r.table_name
                          and c.column_name = r.column_name
                    )
                );
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return schemaReady
                ? HealthCheckResult.Healthy("PostgreSQL e schema mínimo de inicialização disponíveis.")
                : HealthCheckResult.Unhealthy("Schema Agro360 ausente, incompleto ou inacessível. Execute a instalação/migração antes de liberar tráfego.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL indisponível.", exception);
        }
    }
}
