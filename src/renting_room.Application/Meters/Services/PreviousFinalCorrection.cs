using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Contracts;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Meters;

namespace renting_room.Application.Meters;

/// <summary>
/// M05 CT-BR-02 / M06 MT-BR-13: chỉ số nhận phòng nhỏ hơn số cuối của HĐ trước ⇒ hỏi nguyên nhân; "số cuối người cũ gõ nhầm" ⇒ điều chỉnh
/// số cuối HĐ cũ về số nhận phòng (<see cref="AfterEndAdjustment"/>, khoản chênh chờ xử lý). Gọi trước khi ghi chỉ số nhận phòng, trong
/// transaction đã khóa HĐ mới — khóa tiếp HĐ cũ → công tơ → hóa đơn trả phòng (C-07).
/// </summary>
public sealed class PreviousFinalCorrection(
    IMeterQueries meterQueries, IMeterRepository meters, IContractQueries contractQueries, IContractRepository contracts, IInvoiceQueries invoiceQueries,
    IInvoiceRepository invoices, AfterEndAdjustment afterEnd, IUnitOfWork unitOfWork)
{
    public async Task<Result> ApplyAsync(Contract contract, DateOnly date, IReadOnlyList<MeterReadingInput>? inputs, CancellationToken ct)
    {
        foreach (var input in inputs ?? [])
        {
            if (input.Value is not { } value || await meterQueries.GetAsync(input.MeterId, ct) is not { } meter
                || meter.LatestOnOrBefore(date) is not { ContractId: { } previousId } final
                || previousId == contract.Id || value >= final.Value)
                continue;

            var previous = (await contractQueries.GetAsync(previousId, ct))!;
            if (!input.CorrectPreviousFinal)
                return Result.Failure(MeterErrors.HandoverBelowPreviousFinal(meter.Id, final.Value, previous.ContractNo));
            if (previous.Status != ContractStatus.Ended)
                return Result.Failure(MeterErrors.PreviousContractNotEnded);
            var corrected = await CorrectAsync(previousId, meter.Id, final.Id, value, ct);
            if (corrected.IsFailure)
                return corrected;
        }
        return Result.Success();
    }

    private async Task<Result> CorrectAsync(Guid previousId, Guid meterId, Guid readingId, decimal value, CancellationToken ct)
    {
        await unitOfWork.LockForUpdateAsync<Contract>(previousId, ct);
        var previous = (await contracts.GetForUpdateAsync(previousId, ct))!;
        var meter = (await meters.LockAndLoadAsync([meterId], ct)).Single();
        var reading = meter.Find(readingId)!;
        var reason = $"Người thuê mới nhận phòng ở số {value:0.##} — số cuối gõ nhầm";
        if (await invoiceQueries.FindFinalizedSegmentEndingAtAsync(readingId, ct) is not { } segment)
        {
            var corrected = meter.Correct(readingId, value, reason);
            return corrected.IsFailure ? Result.Failure(corrected.Error!) : Result.Success();
        }

        await unitOfWork.LockForUpdateAsync<Invoice>(segment.InvoiceId, ct);
        var source = (await invoices.GetForUpdateAsync(segment.InvoiceId, ct))!;
        var adjusted = await afterEnd.ApplyAsync(previous, meter, source, reading, value, reason, null, false, ct);
        return adjusted.IsFailure ? Result.Failure(adjusted.Error!) : Result.Success();
    }
}
