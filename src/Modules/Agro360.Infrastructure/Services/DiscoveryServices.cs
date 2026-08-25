using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class GlobalSearchService(DatabaseExecutor database, ITenantContext tenantContext) : IGlobalSearchService
{
    public Task<IReadOnlyCollection<GlobalSearchItem>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalized = Guard.Required(query, nameof(query), 120);
        limit = Math.Clamp(limit, 1, 30);
        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var items = await connection.QueryAsync<GlobalSearchItem>(new CommandDefinition(
                """
                select * from (
                    select 'FARM' as EntityType, id as EntityId, name as Title,
                           state as Subtitle, '/properties/' || id as Route,
                           similarity(name, @Query) as Rank
                    from geo.farms
                    where tenant_id = @TenantId and deleted_at is null and name % @Query

                    union all

                    select 'FIELD', id, name, area_ha || ' ha', '/fields/' || id,
                           similarity(name, @Query)
                    from geo.fields
                    where tenant_id = @TenantId and deleted_at is null and name % @Query

                    union all

                    select 'ANIMAL', id, tag, concat(species, ' · ', breed), '/livestock/animals/' || id,
                           greatest(similarity(tag, @Query), similarity(coalesce(rfid, ''), @Query))
                    from livestock.animals
                    where tenant_id = @TenantId and deleted_at is null
                      and (tag % @Query or coalesce(rfid, '') % @Query)

                    union all

                    select 'PRODUCT', id, name, concat(sku, ' · ', category), '/inventory/products/' || id,
                           greatest(similarity(name, @Query), similarity(sku, @Query))
                    from inventory.products
                    where tenant_id = @TenantId and deleted_at is null
                      and (name % @Query or sku % @Query)

                    union all

                    select 'SEASON', id, name, crop, '/agriculture/seasons/' || id,
                           greatest(similarity(name, @Query), similarity(crop, @Query))
                    from agriculture.seasons
                    where tenant_id = @TenantId and deleted_at is null
                      and (name % @Query or crop % @Query)
                ) results
                order by Rank desc, Title
                limit @Limit;
                """,
                new { tenantContext.TenantId, Query = normalized, Limit = limit },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return (IReadOnlyCollection<GlobalSearchItem>)items.ToArray();
        }, cancellationToken);
    }
}

public sealed class TraceabilityService(DatabaseExecutor database, ITenantContext tenantContext) : ITraceabilityService
{
    public Task<TraceabilityGraph> GetGraphAsync(
        string entityType,
        Guid entityId,
        int depth,
        CancellationToken cancellationToken)
    {
        var normalizedType = Guard.Required(entityType, nameof(entityType), 60).ToUpperInvariant();
        depth = Math.Clamp(depth, 1, 8);
        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var rootExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists(
                    select 1 from traceability.nodes
                    where tenant_id = @TenantId and entity_type = @EntityType and entity_id = @EntityId
                );
                """,
                new { tenantContext.TenantId, EntityType = normalizedType, EntityId = entityId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!rootExists)
            {
                throw new NotFoundException("Nó de rastreabilidade", entityId);
            }

            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                with recursive graph_nodes as (
                    select n.id, 0 as level, array[n.id] as path
                    from traceability.nodes n
                    where n.tenant_id = @TenantId and n.entity_type = @EntityType and n.entity_id = @EntityId

                    union all

                    select case when e.from_node_id = g.id then e.to_node_id else e.from_node_id end,
                           g.level + 1,
                           g.path || case when e.from_node_id = g.id then e.to_node_id else e.from_node_id end
                    from graph_nodes g
                    join traceability.edges e
                      on e.tenant_id = @TenantId
                     and (e.from_node_id = g.id or e.to_node_id = g.id)
                    where g.level < @Depth
                      and not (case when e.from_node_id = g.id then e.to_node_id else e.from_node_id end = any(g.path))
                ), distinct_nodes as (
                    select distinct id from graph_nodes
                )
                select n.id, n.entity_type as EntityType, n.entity_id as EntityId, n.label
                from traceability.nodes n
                join distinct_nodes d on d.id = n.id
                where n.tenant_id = @TenantId;

                with recursive graph_nodes as (
                    select n.id, 0 as level, array[n.id] as path
                    from traceability.nodes n
                    where n.tenant_id = @TenantId and n.entity_type = @EntityType and n.entity_id = @EntityId

                    union all

                    select case when e.from_node_id = g.id then e.to_node_id else e.from_node_id end,
                           g.level + 1,
                           g.path || case when e.from_node_id = g.id then e.to_node_id else e.from_node_id end
                    from graph_nodes g
                    join traceability.edges e
                      on e.tenant_id = @TenantId
                     and (e.from_node_id = g.id or e.to_node_id = g.id)
                    where g.level < @Depth
                      and not (case when e.from_node_id = g.id then e.to_node_id else e.from_node_id end = any(g.path))
                ), distinct_nodes as (
                    select distinct id from graph_nodes
                )
                select distinct e.id, e.from_node_id as FromNodeId, e.to_node_id as ToNodeId,
                       e.relation_type as RelationType, e.created_at as CreatedAt
                from traceability.edges e
                join distinct_nodes f on f.id = e.from_node_id
                join distinct_nodes t on t.id = e.to_node_id
                where e.tenant_id = @TenantId;
                """,
                new
                {
                    tenantContext.TenantId,
                    EntityType = normalizedType,
                    EntityId = entityId,
                    Depth = depth
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var nodes = (await grid.ReadAsync<TraceabilityNodeDto>().ConfigureAwait(false)).ToArray();
            var edges = (await grid.ReadAsync<TraceabilityEdgeDto>().ConfigureAwait(false)).ToArray();
            return new TraceabilityGraph(nodes, edges);
        }, cancellationToken);
    }
}
