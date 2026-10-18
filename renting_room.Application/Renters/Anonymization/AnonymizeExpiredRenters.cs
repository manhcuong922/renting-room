using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Renters;

public static class PersonalDataRetention
{
    /// <summary>Mỗi lượt job xử lý tối đa từng ấy hồ sơ / tổ chức — phần còn lại để lượt sau.</summary>
    public const int MaxRentersPerRun = 200;
}

// Ẩn danh tự động (job) và bằng tay
/// <summary>
/// RT-BR-06: ẩn danh tự động các hồ sơ của tổ chức hiện tại đủ điều kiện tại ngày <paramref name="AsOf"/> — job hằng ngày chạy dưới
/// danh nghĩa hệ thống của từng tổ chức. Tổ chức tắt ẩn danh tự động ⇒ bỏ qua. Trả số hồ sơ đã ẩn danh.
/// </summary>
public sealed record AnonymizeExpiredRentersCommand(DateOnly AsOf) : IRequest<int>;

/// <summary>
/// Đủ điều kiện: (1) không còn HĐ nháp / hiệu lực / thanh lý có người này (đứng tên hoặc ở cùng); (2) lần cuối gắn với HĐ (ngày trả phòng
/// của HĐ đã kết thúc, ngày hủy HĐ nháp, hoặc ngày tạo hồ sơ nếu chưa từng có HĐ) đã quá thời gian giữ của tổ chức; (3) các HĐ người này
/// đứng tên không còn phiếu nợ chưa thu / phiếu chờ hoàn. Mỗi hồ sơ 1 transaction.
/// </summary>
public sealed class AnonymizeExpiredRentersHandler(IAppDbContext db, ICurrentUser currentUser, RenterAnonymizer anonymizer)
    : IRequestHandler<AnonymizeExpiredRentersCommand, int>
{
    public async ValueTask<int> Handle(AnonymizeExpiredRentersCommand request, CancellationToken cancellationToken)
    {
        var settings = await db.Organizations.AsNoTracking().Where(o => o.Id == currentUser.OrganizationId)
            .Select(o => new { o.AutoAnonymizeEnabled, o.PersonalDataRetentionMonths }).FirstOrDefaultAsync(cancellationToken);
        if (settings is not { AutoAnonymizeEnabled: true })
            return 0;

        var cutoff = request.AsOf.AddMonths(-settings.PersonalDataRetentionMonths);
        var renterIds = await EligibleAsync(cutoff, cancellationToken);
        foreach (var renterId in renterIds)
        {
            await anonymizer.AnonymizeAsync(renterId, reason: null, cancellationToken);
            db.ChangeTracker.Clear();
        }
        return renterIds.Count;
    }

    private async Task<List<Guid>> EligibleAsync(DateOnly cutoff, CancellationToken ct)
    {
        var candidates = await RenterAnonymizer.WithoutBlockers(db, db.Renters.AsNoTracking().Where(r => r.AnonymizedAt == null))
            .Select(r => new
            {
                r.Id,
                r.CreatedAt,
                LastEnded = db.Contracts
                    .Where(c => (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id) || c.RepresentativeHistory.Any(h => h.RenterId == r.Id)) && c.Status == ContractStatus.Ended)
                    .Max(c => c.ActualEndDate),
                LastCancelled = db.Contracts
                    .Where(c => (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id) || c.RepresentativeHistory.Any(h => h.RenterId == r.Id)) && c.Status == ContractStatus.Cancelled)
                    .Max(c => c.CancelledAt)
            })
            .ToListAsync(ct);

        return candidates
            .Select(c => (c.Id, Last: new[] { c.CreatedAt.ToBusinessDate(), c.LastEnded ?? DateOnly.MinValue, c.LastCancelled?.ToBusinessDate() ?? DateOnly.MinValue }.Max()))
            .Where(c => c.Last <= cutoff)
            .OrderBy(c => c.Last)
            .Take(PersonalDataRetention.MaxRentersPerRun)
            .Select(c => c.Id)
            .ToList();
    }
}
