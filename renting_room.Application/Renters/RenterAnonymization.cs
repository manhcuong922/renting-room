using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Common.Validation;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Identity;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

public static class PersonalDataRetention
{
    /// <summary>Mỗi lượt job xử lý tối đa từng ấy hồ sơ / tổ chức — phần còn lại để lượt sau.</summary>
    public const int MaxRentersPerRun = 200;
}

// ============================================================ Cài đặt lưu giữ của tổ chức

/// <param name="RetentionMonths">Giữ dữ liệu cá nhân bao nhiêu tháng sau lần cuối gắn với HĐ (36–120).</param>
/// <param name="AutoAnonymize">Tắt ⇒ không tự ẩn danh; chủ trọ tự chịu trách nhiệm lưu giữ.</param>
public sealed record DataRetentionDto(int RetentionMonths, bool AutoAnonymize, IReadOnlyList<Warning> Warnings);

public sealed record GetDataRetentionQuery : IRequest<DataRetentionDto>;

public sealed class GetDataRetentionHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<GetDataRetentionQuery, DataRetentionDto>
{
    public async ValueTask<DataRetentionDto> Handle(GetDataRetentionQuery request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().FirstAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        return DataRetentionMapping.ToDto(organization);
    }
}

public sealed record UpdateDataRetentionCommand(int RetentionMonths, bool AutoAnonymize) : IRequest<DataRetentionDto>;

public sealed class UpdateDataRetentionCommandValidator : AbstractValidator<UpdateDataRetentionCommand>
{
    public UpdateDataRetentionCommandValidator() =>
        RuleFor(x => x.RetentionMonths).InclusiveBetween(Organization.MinRetentionMonths, Organization.MaxRetentionMonths)
            .WithErrorCode("OUT_OF_RANGE").WithMessage("Thời gian giữ dữ liệu từ 36 đến 120 tháng.");
}

/// <summary>RT-BR-06: chủ trọ đặt thời gian giữ / tắt ẩn danh tự động — thay đổi được audit (ai, lúc nào, giá trị cũ → mới).</summary>
public sealed class UpdateDataRetentionHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<UpdateDataRetentionCommand, DataRetentionDto>
{
    public async ValueTask<DataRetentionDto> Handle(UpdateDataRetentionCommand request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.FirstAsync(o => o.Id == currentUser.OrganizationId, cancellationToken);
        organization.UpdateDataRetention(request.RetentionMonths, request.AutoAnonymize);
        await db.SaveChangesAsync(cancellationToken);
        return DataRetentionMapping.ToDto(organization);
    }
}

internal static class DataRetentionMapping
{
    public static DataRetentionDto ToDto(Organization organization) => new(
        organization.PersonalDataRetentionMonths,
        organization.AutoAnonymizeEnabled,
        organization.AutoAnonymizeEnabled
            ? []
            : [new Warning("AUTO_ANONYMIZE_DISABLED",
                "Đã tắt ẩn danh tự động — dữ liệu cá nhân người thuê cũ được giữ vô thời hạn, chủ trọ tự chịu trách nhiệm lưu giữ theo luật bảo vệ dữ liệu cá nhân.")]);
}

// ============================================================ Ẩn danh tự động (job) và bằng tay

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
                    .Where(c => (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id)) && c.Status == ContractStatus.Ended)
                    .Max(c => c.ActualEndDate),
                LastCancelled = db.Contracts
                    .Where(c => (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id)) && c.Status == ContractStatus.Cancelled)
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

/// <summary>
/// Ẩn danh bằng tay — người thuê yêu cầu xóa dữ liệu, hoặc chủ trọ muốn xóa sớm hơn thời gian giữ. Chỉ chủ trọ; lý do bắt buộc (ghi audit);
/// không cần đủ thời gian giữ nhưng vẫn chặn khi còn HĐ đang chạy / còn nợ / chờ hoàn. Không đảo ngược.
/// </summary>
public sealed record AnonymizeRenterCommand(Guid Id, string Reason) : IRequest<Result>;

public sealed class AnonymizeRenterCommandValidator : AbstractValidator<AnonymizeRenterCommand>
{
    public AnonymizeRenterCommandValidator() => RuleFor(x => x.Reason).RequiredText(300, "Lý do");
}

public sealed class AnonymizeRenterHandler(IAppDbContext db, RenterAnonymizer anonymizer) : IRequestHandler<AnonymizeRenterCommand, Result>
{
    public async ValueTask<Result> Handle(AnonymizeRenterCommand request, CancellationToken cancellationToken)
    {
        var renter = await db.Renters.AsNoTracking().Where(r => r.Id == request.Id)
            .Select(r => new { r.AnonymizedAt }).FirstOrDefaultAsync(cancellationToken);
        if (renter is null)
            return Result.Failure(RenterErrors.RenterNotFound);
        if (renter.AnonymizedAt is not null)
            return Result.Failure(RenterErrors.Anonymized);
        if (await RenterAnonymizer.HasOpenContractAsync(db, request.Id, cancellationToken))
            return Result.Failure(RenterErrors.HasOpenContract);
        if (await RenterAnonymizer.HasUnsettledInvoiceAsync(db, request.Id, cancellationToken))
            return Result.Failure(RenterErrors.HasUnsettledInvoices);

        await anonymizer.AnonymizeAsync(request.Id, request.Reason.Trim(), cancellationToken);
        return Result.Success();
    }
}

/// <summary>Phần dùng chung của ẩn danh tự động và bằng tay: điều kiện chặn + thao tác xóa trên hồ sơ, HĐ, phiếu, phiếu thu, audit.</summary>
public sealed class RenterAnonymizer(IAppDbContext db, IAuditTrail auditTrail, IAuditLogEraser auditLogEraser, TimeProvider clock)
{
    private static readonly ContractStatus[] OpenStatuses = [ContractStatus.Draft, ContractStatus.Active, ContractStatus.Liquidating];

    public static Task<bool> HasOpenContractAsync(IAppDbContext db, Guid renterId, CancellationToken ct) =>
        db.Contracts.AnyAsync(c => (c.RepresentativeRenterId == renterId || c.Occupants.Any(o => o.RenterId == renterId))
            && OpenStatuses.Contains(c.Status), ct);

    public static Task<bool> HasUnsettledInvoiceAsync(IAppDbContext db, Guid renterId, CancellationToken ct) =>
        db.Invoices.AnyAsync(i => i.Status == InvoiceStatus.Finalized
            && db.Contracts.Any(c => c.Id == i.ContractId && c.RepresentativeRenterId == renterId)
            && (i.PaidAmount < i.TotalAmount || (i.TotalAmount < 0 && i.RefundedOn == null)), ct);

    /// <summary>Lọc bỏ hồ sơ còn HĐ đang chạy hoặc còn phiếu nợ / chờ hoàn (cùng điều kiện với 2 hàm trên, dạng truy vấn tập hợp).</summary>
    public static IQueryable<Renter> WithoutBlockers(IAppDbContext db, IQueryable<Renter> renters) => renters
        .Where(r => !db.Contracts.Any(c => (c.RepresentativeRenterId == r.Id || c.Occupants.Any(o => o.RenterId == r.Id))
            && OpenStatuses.Contains(c.Status)))
        .Where(r => !db.Invoices.Any(i => i.Status == InvoiceStatus.Finalized
            && db.Contracts.Any(c => c.Id == i.ContractId && c.RepresentativeRenterId == r.Id)
            && (i.PaidAmount < i.TotalAmount || (i.TotalAmount < 0 && i.RefundedOn == null))));

    /// <param name="reason">Ẩn danh bằng tay: lý do (ghi audit); null = job tự động.</param>
    public async Task AnonymizeAsync(Guid renterId, string? reason, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockForUpdateAsync<Renter>(renterId, ct);
        var renter = await db.Renters.FirstAsync(r => r.Id == renterId, ct);
        if (renter.IsAnonymized)
            return;

        var name = Renter.AnonymizedName(renterId);
        var contracts = await db.Contracts.Include(c => c.Vehicles)
            .Where(c => c.RepresentativeRenterId == renterId || c.Vehicles.Any(v => v.RenterId == renterId)).ToListAsync(ct);
        var vehicleIds = contracts.SelectMany(c => c.EraseEndedVehicleIdentity(c.RepresentativeRenterId == renterId ? null : renterId)).ToList();
        contracts = contracts.Where(c => c.RepresentativeRenterId == renterId).ToList();
        var contractIds = contracts.Select(c => c.Id).ToList();
        var invoices = await db.Invoices.Where(i => contractIds.Contains(i.ContractId)).ToListAsync(ct);
        var payments = await db.Payments.Where(p => contractIds.Contains(p.ContractId)).ToListAsync(ct);

        renter.Anonymize(clock.GetUtcNow());
        foreach (var contract in contracts)
            contract.ReplaceSigningSnapshot(SigningSnapshot.FromJson(contract.SigningSnapshot)?.WithAnonymizedRepresentative(name).ToJson());
        foreach (var invoice in invoices)
            invoice.AnonymizeRepresentative(name);
        foreach (var payment in payments)
            payment.ClearPayerName();
        auditTrail.Record(AuditActions.Anonymized, nameof(Renter), renterId,
            new
            {
                mode = reason is null ? "Automatic" : "Manual", reason, contracts = contracts.Count, invoices = invoices.Count, payments = payments.Count,
                vehicles = vehicleIds.Count
            });

        await db.SaveChangesAsync(ct);
        // Sau SaveChanges: chính lần lưu này cũng sinh audit "Updated" chứa giá trị cũ ⇒ xóa cùng lúc, trong cùng transaction.
        await auditLogEraser.EraseRenterAsync(renterId, invoices.Select(i => i.Id).ToList(), payments.Select(p => p.Id).ToList(), vehicleIds, ct);
        await transaction.CommitAsync(ct);
    }
}
