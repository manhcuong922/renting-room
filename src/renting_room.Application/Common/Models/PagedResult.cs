namespace renting_room.Application.Common.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Trần số trang (L-08): <c>(Page - 1) * PageSize</c> không tràn int — 10 triệu dòng, dư cho mọi danh sách.</summary>
    public const int MaxPage = 100_000;
}
