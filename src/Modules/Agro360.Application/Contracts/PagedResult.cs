namespace Agro360.Application.Contracts;

/// <summary>
/// Canonical paginated response used by application service contracts.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, long Total)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)Total / PageSize);
}
