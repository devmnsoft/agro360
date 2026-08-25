namespace Agro360.Application.Contracts;

public sealed record GlobalSearchItem(
    string EntityType,
    Guid EntityId,
    string Title,
    string? Subtitle,
    string Route,
    decimal Rank);

public interface IGlobalSearchService
{
    Task<IReadOnlyCollection<GlobalSearchItem>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}

public sealed record TraceabilityNodeDto(Guid Id, string EntityType, Guid EntityId, string Label);

public sealed record TraceabilityEdgeDto(Guid Id, Guid FromNodeId, Guid ToNodeId, string RelationType, DateTimeOffset CreatedAt);

public sealed record TraceabilityGraph(
    IReadOnlyCollection<TraceabilityNodeDto> Nodes,
    IReadOnlyCollection<TraceabilityEdgeDto> Edges);

public interface ITraceabilityService
{
    Task<TraceabilityGraph> GetGraphAsync(string entityType, Guid entityId, int depth, CancellationToken cancellationToken);
}
