using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;

namespace renting_room.Application.Billing;

/// <summary>
/// M07 BL-BR-41: hóa đơn trả phòng là bản kết toán. Chốt ⇒ nợ của các hóa đơn nguồn chuyển vào (hóa đơn nguồn "Đã chuyển", còn nợ 0) và cọc
/// "Đã trừ"; hủy ⇒ hoàn nguyên. Gọi trong transaction của lệnh chốt / hủy, HĐ và hóa đơn trả phòng đã khóa — khóa tiếp hóa đơn nguồn theo id (C-07).
/// </summary>
public sealed class FinalInvoiceSettlement(IUnitOfWork unitOfWork, IInvoiceRepository invoices)
{
    public async Task<Result> ApplyAsync(Contract contract, Invoice final, DateOnly today, CancellationToken ct)
    {
        foreach (var (sourceId, amount) in Debts(final))
        {
            var source = await LockSourceAsync(sourceId, ct);
            var carried = source.CarryOverTo(final.Id, amount);
            if (carried.IsFailure)
                return carried;
        }
        return final.Lines.Any(l => l.Type == InvoiceLineType.Deposit)
            ? contract.DeductDepositOnFinal(today, final.InvoiceNo!)
            : Result.Success();
    }

    public async Task RevertAsync(Contract contract, Invoice final, CancellationToken ct)
    {
        foreach (var (sourceId, _) in Debts(final))
            (await LockSourceAsync(sourceId, ct)).UndoCarryOver();
        if (final.Lines.Any(l => l.Type == InvoiceLineType.Deposit))
            contract.UndoDepositDeduction();
    }

    private static List<(Guid SourceId, decimal Amount)> Debts(Invoice final) =>
        final.Lines.Where(l => l is { Type: InvoiceLineType.PreviousDebt, SourceInvoiceId: not null })
            .Select(l => (l.SourceInvoiceId!.Value, l.Amount)).OrderBy(d => d.Item1).ToList();

    private async Task<Invoice> LockSourceAsync(Guid sourceId, CancellationToken ct)
    {
        await unitOfWork.LockForUpdateAsync<Invoice>(sourceId, ct);
        return (await invoices.GetForUpdateAsync(sourceId, ct))!;
    }
}
