using FluentValidation;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Payments;
using renting_room.Domain.Common;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

/// <summary>Phần dùng chung của ẩn danh tự động và bằng tay: điều kiện chặn + thao tác xóa trên hồ sơ, HĐ, phiếu, phiếu thu, ô ghi chú, audit.</summary>
public sealed class RenterAnonymizer(
    IRenterRepository renters,
    IRenterQueries renterQueries,
    IContractRepository contractRepository,
    IContractQueries contractQueries,
    IInvoiceRepository invoiceRepository,
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IAuditLogEraser auditLogEraser,
    IPersonalNoteEraser noteEraser,
    IPersonalDataProtector protector,
    TimeProvider clock)
{
    private async Task<Error?> FindBlockerAsync(Guid renterId, CancellationToken ct)
    {
        if (await renterQueries.HasOpenContractAsync(renterId, ct))
            return RenterErrors.HasOpenContract;
        return await renterQueries.HasUnsettledInvoiceAsync(renterId, ct) ? RenterErrors.HasUnsettledInvoices : null;
    }

    /// <param name="reason">Ẩn danh bằng tay: lý do (ghi audit); null = job tự động.</param>
    /// <returns>
    /// Thất bại (<c>RENTER_HAS_ACTIVE_CONTRACT</c> / <c>RENTER_HAS_UNSETTLED_INVOICES</c>) khi, SAU KHI giữ khóa người thuê, người này lại có
    /// hợp đồng mở / còn nợ — quyết định "đủ điều kiện" của người gọi đã cũ (M-23). Không xóa gì.
    /// </returns>
    public async Task<Result> AnonymizeAsync(Guid renterId, string? reason, CancellationToken ct)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        // Cùng khóa với mọi lệnh làm đổi "người này ở đâu" (CT-BR-31): tạo HĐ cho người quay lại phải chờ, hoặc ta thấy HĐ của nó ở bước kiểm dưới.
        await unitOfWork.LockRentersAsync([renterId], ct);
        await unitOfWork.LockForUpdateAsync<Renter>(renterId, ct);
        var renter = (await renters.GetForUpdateAsync(renterId, ct))!;
        if (renter.IsAnonymized)
            return Result.Success();
        if (await FindBlockerAsync(renterId, ct) is { } blocker)
            return Result.Failure(blocker);

        var keepName = reason is null;
        var oldName = renter.FullName;
        var name = keepName ? oldName : Renter.AnonymizedName(renterId);
        var contracts = (await contractRepository.ListWithVehiclesByRenterAsync(renterId, ct)).ToList();
        var vehicleIds = contracts.SelectMany(c => c.EraseEndedVehicleIdentity(c.RepresentativeRenterId == renterId ? null : renterId)).ToList();
        var linkedContractIds = contracts.Select(c => c.Id).ToList();
        contracts = contracts.Where(c => c.RepresentativeRenterId == renterId).ToList();
        var contractIds = contracts.Select(c => c.Id).ToList();
        var invoices = (await invoiceRepository.ListByContractsAsync(contractIds, ct)).ToList();
        var payments = await paymentRepository.ListByContractsAsync(contractIds, ct);
        var formerContractIds = await contractQueries.ListIdsByFormerRepresentativeAsync(renterId, ct);
        invoices.AddRange(await invoiceRepository.ListByRepresentativeNameAsync(formerContractIds, contractIds, oldName, ct));
        // L-17: đọc trước khi xóa khỏi hồ sơ — để che trong ô ghi chú. Tự động giữ họ tên ⇒ không che tên.
        string?[] identifiers =
        [
            keepName ? null : oldName, renter.Phone, renter.Email,
            renter.IdNumberEncrypted is { Length: > 0 } encrypted ? protector.Decrypt(encrypted) : null
        ];
        var noteErasure = new PersonalNoteErasure(renterId,
            ClearContractIds: keepName ? contractIds : [],
            MaskContractIds: linkedContractIds.Concat(formerContractIds).Distinct().ToList(),
            Identifiers: identifiers.OfType<string>().ToList());

        renter.Anonymize(clock.GetUtcNow(), keepName);
        foreach (var contract in contracts)
            contract.ReplaceSigningSnapshot(SigningSnapshot.FromJson(contract.SigningSnapshot)?.WithAnonymizedRepresentative(name).ToJson());
        if (!keepName)
        {
            foreach (var invoice in invoices)
                invoice.AnonymizeRepresentative(name);
            foreach (var payment in payments)
                payment.ClearPayerName();
        }
        auditTrail.Record(AuditActions.Anonymized, nameof(Renter), renterId,
            new
            {
                mode = reason is null ? "Automatic" : "Manual", reason, contracts = contracts.Count, invoices = invoices.Count, payments = payments.Count,
                vehicles = vehicleIds.Count
            });

        await unitOfWork.SaveChangesAsync(ct);
        // Sau SaveChanges: chính lần lưu này cũng sinh audit "Updated" chứa giá trị cũ ⇒ xóa cùng lúc, trong cùng transaction.
        await auditLogEraser.EraseRenterAsync(renterId, invoices.Select(i => i.Id).ToList(), payments.Select(p => p.Id).ToList(), vehicleIds, ct);
        await noteEraser.EraseAsync(noteErasure, ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }
}
