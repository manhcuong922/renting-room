using FluentValidation;
using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Payments;
using renting_room.Domain.Renters;

namespace renting_room.Application.Renters;

/// <summary>Phần dùng chung của ẩn danh tự động và bằng tay: điều kiện chặn + thao tác xóa trên hồ sơ, HĐ, phiếu, phiếu thu, audit.</summary>
public sealed class RenterAnonymizer(
    IRenterRepository renters,
    IContractRepository contractRepository,
    IContractQueries contractQueries,
    IInvoiceRepository invoiceRepository,
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IAuditLogEraser auditLogEraser,
    TimeProvider clock)
{
    /// <param name="reason">Ẩn danh bằng tay: lý do (ghi audit); null = job tự động.</param>
    public async Task AnonymizeAsync(Guid renterId, string? reason, CancellationToken ct)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await unitOfWork.LockForUpdateAsync<Renter>(renterId, ct);
        var renter = (await renters.GetForUpdateAsync(renterId, ct))!;
        if (renter.IsAnonymized)
            return;

        var keepName = reason is null;
        var oldName = renter.FullName;
        var name = keepName ? oldName : Renter.AnonymizedName(renterId);
        var contracts = (await contractRepository.ListWithVehiclesByRenterAsync(renterId, ct)).ToList();
        var vehicleIds = contracts.SelectMany(c => c.EraseEndedVehicleIdentity(c.RepresentativeRenterId == renterId ? null : renterId)).ToList();
        contracts = contracts.Where(c => c.RepresentativeRenterId == renterId).ToList();
        var contractIds = contracts.Select(c => c.Id).ToList();
        var invoices = (await invoiceRepository.ListByContractsAsync(contractIds, ct)).ToList();
        var payments = await paymentRepository.ListByContractsAsync(contractIds, ct);
        var formerContractIds = await contractQueries.ListIdsByFormerRepresentativeAsync(renterId, ct);
        invoices.AddRange(await invoiceRepository.ListByRepresentativeNameAsync(formerContractIds, contractIds, oldName, ct));

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
        await transaction.CommitAsync(ct);
    }
}
