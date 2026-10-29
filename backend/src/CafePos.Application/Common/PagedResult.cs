namespace CafePos.Application.Common;

/// <summary>Offset pagination envelope.</summary>
public record PagedResult<T>(IReadOnlyList<T> Data, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
