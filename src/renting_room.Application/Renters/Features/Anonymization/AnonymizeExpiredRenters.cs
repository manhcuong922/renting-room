using FluentValidation;
using Mediator;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Application.Identity.Organizations;

namespace renting_room.Application.Renters;

public static class PersonalDataRetention
{
    public const int MaxRentersPerRun = 200;
}

public sealed record AnonymizeExpiredRentersCommand(DateOnly AsOf) : IRequest<int>;

/// <summary>
/// Đủ điều kiện: (1) không còn HĐ nháp / hiệu lực / thanh lý có người này (đứng tên hoặc ở cùng); (2) lần cuối gắn với HĐ (ngày trả phòng
/// của HĐ đã kết thúc, ngày hủy HĐ nháp, hoặc ngày tạo hồ sơ nếu chưa từng có HĐ) đã quá thời gian giữ của tổ chức; (3) các HĐ người này
/// đứng tên không còn phiếu nợ chưa thu / phiếu chờ hoàn. Mỗi hồ sơ 1 transaction.
/// </summary>
public sealed class AnonymizeExpiredRentersHandler(
    IRenterQueries renters, IOrganizationQueries organizations, IUnitOfWork unitOfWork, ICurrentUser currentUser, RenterAnonymizer anonymizer)
    : IRequestHandler<AnonymizeExpiredRentersCommand, int>
{
    public async ValueTask<int> Handle(AnonymizeExpiredRentersCommand request, CancellationToken cancellationToken)
    {
        var settings = currentUser.OrganizationId is { } organizationId ? await organizations.GetAsync(organizationId, cancellationToken) : null;
        if (settings is not { AutoAnonymizeEnabled: true })
            return 0;

        var cutoff = request.AsOf.AddMonths(-settings.PersonalDataRetentionMonths);
        var renterIds = await EligibleAsync(cutoff, cancellationToken);
        foreach (var renterId in renterIds)
        {
            await anonymizer.AnonymizeAsync(renterId, reason: null, cancellationToken);
            unitOfWork.ClearTracking();
        }
        return renterIds.Count;
    }

    private async Task<List<Guid>> EligibleAsync(DateOnly cutoff, CancellationToken ct)
    {
        var candidates = await renters.ListAnonymizationCandidatesAsync(ct);

        return candidates
            .Select(c => (c.Id, Last: new[] { c.CreatedAt.ToBusinessDate(), c.LastEnded ?? DateOnly.MinValue, c.LastCancelled?.ToBusinessDate() ?? DateOnly.MinValue }.Max()))
            .Where(c => c.Last <= cutoff)
            .OrderBy(c => c.Last)
            .Take(PersonalDataRetention.MaxRentersPerRun)
            .Select(c => c.Id)
            .ToList();
    }
}
