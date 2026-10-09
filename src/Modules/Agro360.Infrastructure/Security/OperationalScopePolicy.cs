using System;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Security;

/// <summary>
/// Política canônica de escopo operacional (unidade/fazenda) por usuário.
/// Organização, permissão, módulo contratado e unidade são dimensões distintas: esta classe resolve
/// somente a dimensão de unidade, sempre a partir de identity_user_unit_scopes.
/// Todo serviço que expõe ou grava dados vinculados a geo_farms deve usar estas mesmas expressões,
/// sem variações locais (requisições, cotações, pedidos, recebimentos, my-day).
/// As consultas exigem os parâmetros @TenantId, @UserId e @FarmId no mesmo comando.
/// @FarmId expressa o CONTEXTO SELECIONADO (X-Farm-Id), nunca amplia autorização: nulo significa
/// "todas as unidades autorizadas" e valor significa "somente a unidade selecionada", sempre
/// sobreposto ao conjunto concedido em identity_user_unit_scopes.
/// </summary>
public static class OperationalScopePolicy
{
    /// <summary>Filtro para tabelas/aliases que carregam property_id (entidades operacionais).</summary>
    public static string Sql(string alias) =>
        $"""
        and (@FarmId::uuid is null or {alias}.property_id=@FarmId)
        and (
            exists(select 1 from agro360.identity_user_unit_scopes scope_all where scope_all.tenant_id=@TenantId and scope_all.user_id=@UserId and scope_all.scope_type='ALL')
            or ({alias}.property_id is not null and exists(
                select 1
                from agro360.geo_farms scope_farm
                where scope_farm.tenant_id={alias}.tenant_id and scope_farm.id={alias}.property_id and scope_farm.deleted_at is null
                  and (
                      exists(select 1 from agro360.identity_user_unit_scopes scope_direct where scope_direct.tenant_id=@TenantId and scope_direct.user_id=@UserId and scope_direct.scope_type='FARM' and scope_direct.farm_id=scope_farm.id)
                      or exists(select 1 from agro360.identity_user_unit_scopes scope_org where scope_org.tenant_id=@TenantId and scope_org.user_id=@UserId and scope_org.scope_type='ORGANIZATION' and scope_org.organization_id=scope_farm.organization_id)
                  )
            ))
        )
        """;

    /// <summary>Filtro direto sobre o alias da própria fazenda (geo_farms).</summary>
    public static string FarmSql(string alias) =>
        $"""
        and (@FarmId::uuid is null or {alias}.id=@FarmId)
        and (
            exists(select 1 from agro360.identity_user_unit_scopes scope_all where scope_all.tenant_id=@TenantId and scope_all.user_id=@UserId and scope_all.scope_type='ALL')
            or exists(select 1 from agro360.identity_user_unit_scopes scope_direct where scope_direct.tenant_id=@TenantId and scope_direct.user_id=@UserId and scope_direct.scope_type='FARM' and scope_direct.farm_id={alias}.id)
            or exists(select 1 from agro360.identity_user_unit_scopes scope_org where scope_org.tenant_id=@TenantId and scope_org.user_id=@UserId and scope_org.scope_type='ORGANIZATION' and scope_org.organization_id={alias}.organization_id)
        )
        """;

    /// <summary>
    /// Resolve a unidade operacional efetiva de uma escrita: o contexto X-Farm-Id vence e é validado,
    /// ausência exige escopo ALL, e toda unidade informada precisa estar autorizada pelo escopo do usuário.
    /// </summary>
    public static async Task<Guid?> ResolveOperationalFarmAsync(
        ITenantContext tenant,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? requestedFarmId,
        CancellationToken ct)
    {
        if (tenant.FarmId is not null)
        {
            if (requestedFarmId is not null && requestedFarmId != tenant.FarmId)
                throw new ForbiddenException("A unidade informada não corresponde ao contexto operacional selecionado.");
            requestedFarmId = tenant.FarmId;
        }

        if (requestedFarmId is null)
        {
            var hasAll = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.identity_user_unit_scopes where tenant_id=@TenantId and user_id=@UserId and scope_type='ALL')",
                new { tenant.TenantId, tenant.UserId },
                transaction,
                cancellationToken: ct));
            if (hasAll) return null;
            throw new DomainException("Selecione uma unidade operacional autorizada para esta operação.", "agro360.procurement_property_required");
        }

        var allowed = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            $"select exists(select 1 from agro360.geo_farms f where f.tenant_id=@TenantId and f.id=@FarmId and f.deleted_at is null {FarmSql("f")})",
            new { tenant.TenantId, tenant.UserId, FarmId = requestedFarmId },
            transaction,
            cancellationToken: ct));
        if (!allowed) throw new ForbiddenException("Usuário não possui permissão para a unidade operacional informada.");
        return requestedFarmId;
    }
}
