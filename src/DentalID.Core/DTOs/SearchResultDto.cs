namespace DentalID.Core.DTOs;

/// <summary>
/// Generic search result DTO
/// </summary>
/// <typeparam name="T">Type of items in search results</typeparam>
public class SearchResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>
/// Search parameters DTO
/// </summary>
public class SearchParametersDto
{
    public string? SearchQuery { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;
}
