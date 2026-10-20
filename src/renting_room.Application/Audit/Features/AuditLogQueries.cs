using System.Text.Json;
using FluentValidation;
using Mediator;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;

namespace renting_room.Application.Audit;

/// <param name="Changes">Sửa: <c>{"field":{"old":…,"new":…}}</c>; tạo / xóa: giá trị các trường; sự kiện: chi tiết. Trường nhạy cảm là <c>"[redacted]"</c>.</param>
public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? UserId,
    string? UserName,
    string Action,
    string EntityType,
    Guid? EntityId,
    JsonElement? Changes,
    string? IpAddress);

/// <summary>Bộ lọc nhật ký; <paramref name="From"/> / <paramref name="To"/> là ngày nghiệp vụ (giờ Việt Nam), tính cả 2 đầu.</summary>
public sealed record AuditLogFilter(
    string? EntityType, Guid? EntityId, Guid? UserId, string? Action, DateOnly? From, DateOnly? To, int Page, int PageSize);

public interface IAuditLogReader
{
    Task<PagedResult<AuditLogDto>> ListAsync(AuditLogFilter filter, CancellationToken cancellationToken);
}

public sealed record ListAuditLogsQuery(
    string? EntityType, Guid? EntityId, Guid? UserId, string? Action, DateOnly? From, DateOnly? To,
    int Page = 1, int PageSize = Paging.DefaultPageSize) : IRequest<PagedResult<AuditLogDto>>;

public sealed class ListAuditLogsQueryValidator : AbstractValidator<ListAuditLogsQuery>
{
    public ListAuditLogsQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.EntityType).OptionalText(64);
        RuleFor(x => x.Action).OptionalText(64);
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.From <= x.To)
            .WithErrorCode("INVALID_DATE_RANGE").WithMessage("Từ ngày phải trước hoặc bằng đến ngày.");
    }
}

public sealed class ListAuditLogsHandler(IAuditLogReader reader) : IRequestHandler<ListAuditLogsQuery, PagedResult<AuditLogDto>>
{
    public async ValueTask<PagedResult<AuditLogDto>> Handle(ListAuditLogsQuery request, CancellationToken cancellationToken) =>
        await reader.ListAsync(new AuditLogFilter(request.EntityType?.Trim(), request.EntityId, request.UserId, request.Action?.Trim(),
            request.From, request.To, request.Page, request.PageSize), cancellationToken);
}
