using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Application.Properties;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

/// <summary>Tính lại 1 nháp trong transaction của người gọi — HĐ và hóa đơn đã khóa (C-07). Không phải nháp ⇒ null.</summary>
public sealed class DraftRecalculator(
    IContractRepository contracts, IInvoiceRepository invoices, IPropertyQueries properties, InvoiceInputLoader inputs, RoomChargeBilling roomCharges)
{
    public async Task<Invoice?> RecalculateAsync(Guid contractId, Guid invoiceId, bool keepManualEdits, CancellationToken ct)
    {
        var contract = (await contracts.GetForUpdateAsync(contractId, ct))!;
        var invoice = (await invoices.GetForUpdateAsync(invoiceId, ct))!;
        if (invoice.Status != InvoiceStatus.Draft)
            return null;

        var schedule = (await properties.GetBillingAsync(contract.PropertyId, ct)).Schedule;
        var period = InvoiceInputLoader.CurrentPeriod(contract, invoice, schedule)
            ?? new BillingPeriod(invoice.PeriodStart, invoice.PeriodEnd, invoice.BillingMonth);
        var calculation = InvoiceCalculator.Calculate(await inputs.LoadAsync(contract, period, invoice.Id, ct, invoice.Type));
        invoice.ApplyCalculation(calculation, keepManualEdits, period.End);
        await roomCharges.AttachPendingAsync(invoice, ct);
        return invoice;
    }
}

/// <summary>
/// M06 MT-BR-06: ghi / sửa / hủy chỉ số ⇒ nháp đang dùng các công tơ đó tự tính lại trong cùng lệnh (giữ ô sửa tay).
/// Thứ tự khóa: <see cref="LockContractsAsync"/> trước khi khóa công tơ, <see cref="RecalculateAsync"/> sau khi đổi chỉ số.
/// </summary>
public sealed class MeterDraftRefresher(IInvoiceQueries invoiceQueries, IUnitOfWork unitOfWork, DraftRecalculator recalculator)
{
    /// <param name="alsoContractIds">HĐ người gọi cũng cần khóa — khóa chung một lượt theo id (C-07).</param>
    public async Task<IReadOnlyList<DraftRef>> LockContractsAsync(
        IReadOnlyCollection<Guid> meterIds, CancellationToken ct, params IEnumerable<Guid> alsoContractIds)
    {
        var drafts = await invoiceQueries.ListDraftsUsingMetersAsync(meterIds, ct);
        foreach (var contractId in drafts.Select(d => d.ContractId).Concat(alsoContractIds).Distinct().Order())
            await unitOfWork.LockForUpdateAsync<Contract>(contractId, ct);
        return drafts;
    }

    /// <summary>Lưu chỉ số trước (bộ tính đọc chỉ số từ DB), rồi khóa từng nháp theo id và tính lại.</summary>
    public async Task RecalculateAsync(IReadOnlyList<DraftRef> drafts, CancellationToken ct)
    {
        if (drafts.Count == 0)
            return;
        await unitOfWork.SaveChangesAsync(ct);
        // Lần lưu vừa rồi đánh dấu "Cần tính lại" bằng SQL (BL-BR-20) ⇒ version nháp đang theo dõi đã cũ: nạp lại từ DB.
        unitOfWork.ClearTracking();
        foreach (var draft in drafts.OrderBy(d => d.InvoiceId))
            await unitOfWork.LockForUpdateAsync<Invoice>(draft.InvoiceId, ct);
        foreach (var draft in drafts)
            await recalculator.RecalculateAsync(draft.ContractId, draft.InvoiceId, keepManualEdits: true, ct);
    }
}
