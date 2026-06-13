namespace CommBank.Models;

/// <summary>Bound query parameters for paged endpoints. Page size is clamped to a safe maximum.</summary>
public sealed class PageQuery
{
    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    public int Page { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value is < 1 or > MaxPageSize ? 20 : value;
    }

    public int Skip => (Math.Max(1, Page) - 1) * PageSize;
}

/// <summary>A single page of results plus the metadata needed to navigate the rest.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    public long TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
