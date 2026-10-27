using renting_room.Application.Billing;
using renting_room.Application.Common.Interfaces;
using renting_room.Application.Properties;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Properties;

namespace renting_room.Application.Contracts;

/// <summary>
/// M05 CT-UC-21 (27/10/2026): ký HĐ mới nối tiếp HĐ cũ. Chỉ lúc này — không phải lúc soạn nháp — HĐ cũ mới thanh lý tại ngày bàn giao,
/// xe của người ở lại chuyển sang HĐ mới và cọc chuyển (nếu chủ trọ chọn). HĐ cũ ở "Đang thanh lý" xử lý nốt hóa đơn cuối / nợ / cọc như
/// thanh lý thường. Gọi trong transaction của lệnh ký, HĐ mới và HĐ cũ đã khóa.
/// </summary>
public sealed class ResignLiquidation(
    IContractRepository contracts, IInvoiceQueries invoices, LiquidationDrafts drafts, IPropertyQueries properties, IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<Result> StartAsync(Contract next, CancellationToken ct)
    {
        var old = await contracts.GetForUpdateAsync(next.PreviousContractId!.Value, ct);
        if (old is not { Status: ContractStatus.Active })
            return Result.Failure(ContractErrors.ResignPreviousNotActive);

        var handover = next.StartDate.AddDays(-1);
        await drafts.DeleteAfterAsync(old.Id, handover, ct);
        if (await invoices.HasInvoiceAfterAsync(old.Id, handover, ct))
            return Result.Failure(ContractErrors.InvoiceAfterEndDate);

        var liquidation = old.StartLiquidation(handover, TerminationReason.MutualAgreement, null,
            $"Ký lại cho người còn ở — hợp đồng mới {next.ContractNo}", clock.GetUtcNow().ToBusinessDate(),
            await FinalPeriodChargeAsync(old, handover, ct));
        if (liquidation.IsFailure)
            return liquidation;

        var moving = old.ResignVehicles(next.Occupants.Select(o => o.RenterId).ToList(), handover, next.StartDate, next.Id);
        // Lưu việc kết thúc xe ở HĐ cũ trước khi đăng ký lại ở HĐ mới — unique "biển số đang đăng ký" kiểm từng câu lệnh.
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var vehicle in moving)
        {
            var registered = next.RegisterVehicle(vehicle);
            if (registered.IsFailure)
                return Result.Failure(registered.Error!);
        }

        if (next.ResignTransfersDeposit)
            old.MarkDepositTransferred(next.ContractNo);
        return Result.Success();
    }

    /// <summary>
    /// Người thuê không rời đi: HĐ mới tính tiếp từ X+1 theo "Tính kỳ lẻ" của khu ⇒ kỳ cuối HĐ cũ bù cho đúng 1 kỳ (CT-BR-11);
    /// X là ngày cuối kỳ ⇒ HĐ mới bắt đầu kỳ sau, không chồng ngày ⇒ thu trọn kỳ.
    /// </summary>
    private async Task<FinalPeriodCharge> FinalPeriodChargeAsync(Contract old, DateOnly handover, CancellationToken ct)
    {
        var billing = await properties.GetBillingAsync(old.PropertyId, ct);
        if (billing.Schedule.StandardPeriodContaining(handover).End == handover)
            return FinalPeriodCharge.FullPeriod;
        return billing.ProrationMode == ProrationMode.Daily ? FinalPeriodCharge.Daily : FinalPeriodCharge.None;
    }
}
