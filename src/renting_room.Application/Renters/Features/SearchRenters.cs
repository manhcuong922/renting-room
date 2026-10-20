using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Models;
using renting_room.Application.Common.Validation;
using renting_room.Domain.Common;

namespace renting_room.Application.Renters;

public sealed record SearchRentersQuery(
    string? Q, string? IdNumber, IdDocumentType? IdType, int Page = 1, int PageSize = Paging.DefaultPageSize, Guid? PropertyId = null,
    bool? Staying = null, bool IncludeArchived = false)
    : IRequest<PagedResult<RenterDto>>;

public sealed class SearchRentersQueryValidator : AbstractValidator<SearchRentersQuery>
{
    public SearchRentersQueryValidator()
    {
        RuleFor(x => x.Page).Page();
        RuleFor(x => x.PageSize).PageSize();
        RuleFor(x => x.Q).OptionalText(100);
        RuleFor(x => x.IdNumber).OptionalText(30);
    }
}

/// <summary>Tìm theo tên (không dấu) / SĐT; số giấy tờ tìm CHÍNH XÁC qua hash (không quét dữ liệu đã mã hóa).</summary>
public sealed class SearchRentersHandler(IRenterQueries renters, ICurrentUser currentUser, IPersonalDataProtector protector, TimeProvider clock)
    : IRequestHandler<SearchRentersQuery, PagedResult<RenterDto>>
{
    public async ValueTask<PagedResult<RenterDto>> Handle(SearchRentersQuery request, CancellationToken cancellationToken)
    {
        List<string?>? hashes = null;
        if (!string.IsNullOrWhiteSpace(request.IdNumber))
        {
            // Hash gồm cả loại giấy tờ ⇒ không chọn loại thì thử mọi loại (hộ chiếu người nước ngoài vẫn tìm được).
            var types = request.IdType is { } type ? [type] : Enum.GetValues<IdDocumentType>();
            hashes = types
                .Select(t => (string?)protector.ProtectIdNumber(currentUser.OrganizationId!.Value, t, request.IdNumber).Hash)
                .ToList();
        }

        string? text = null, phone = null;
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            text = TextNormalizer.ToSearchText(request.Q);
            phone = RenterProjection.NormalizePhone(request.Q);
        }

        var search = new RenterSearch(
            request.IncludeArchived, request.PropertyId, request.Staying, clock.GetUtcNow().ToBusinessDate(), hashes, text, phone,
            request.Page, request.PageSize);
        return await renters.SearchAsync(search, cancellationToken);
    }
}
