using renting_room.Application.Billing;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Application.Renters;
using renting_room.Domain.Billing;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Seeding;

/// <summary>
/// Khu B — thu trước (Prepaid), chốt ngày 5, bên cho thuê là doanh nghiệp, điện giá bậc: phiếu kỳ X = tiền phòng kỳ X + điện nước kỳ X−1,
/// lập ngày đầu kỳ X.
/// </summary>
public sealed partial class DemoDataSeeder
{
    private const int PrepaidAnchorDay = 5;
    private const decimal PrepaidRent = 3000000;

    private async Task SeedPrepaidPropertyAsync()
    {
        var current = _today.Day >= PrepaidAnchorDay
            ? new DateOnly(_today.Year, _today.Month, PrepaidAnchorDay)
            : new DateOnly(_today.Year, _today.Month, PrepaidAnchorDay).AddMonths(-1);
        DateOnly Period(int k) => current.AddMonths(k);
        var longAgo = Period(-60);

        var propertyId = await PropertyAsync("B", "Khu B — Thu trước", "8 Lê Thanh Nghị", PrepaidAnchorDay, ChargeMode.Prepaid, companyLessor: true);
        var electricity = await FeeIdAsync(propertyId, "Điện");
        var water = await FeeIdAsync(propertyId, "Nước");
        await AddPriceAsync(electricity, longAgo, null, "Giá bậc theo biểu EVN sinh hoạt",
            [new PriceTier(50, 1984), new PriceTier(100, 2050), new PriceTier(200, 2380), new PriceTier(null, 2998)]);
        await AddPriceAsync(water, longAgo, 15000);
        var trash = await ServiceFeeAsync(propertyId, "Rác", ChargeBasis.PerRoom, "phòng", 25000, longAgo);
        var parking = await ServiceFeeAsync(propertyId, "Giữ xe máy", ChargeBasis.PerUnit, "xe", 120000, longAgo, VehicleType.Motorbike);
        ContractFeeRequest[] standard = [new(trash, null, null), new(parking, 1, null)];

        Task<DemoRoom> Room(string code) => RoomAsync(propertyId, electricity, water, code, "1", PrepaidRent, longAgo);

        // Chỉ số cuối kỳ X (đọc ngày chốt của kỳ X+1).
        Task Close(DemoRoom room, Guid contract, DateOnly periodStart, decimal kWh, decimal m3 = 5) =>
            SaveReadings(propertyId,
                Reading(room.Electricity, contract, periodStart, periodStart.AddMonths(1), kWh),
                Reading(room.Water!.Value, contract, periodStart, periodStart.AddMonths(1), m3));

        // Thu trước: phiếu kỳ X lập ngày đầu kỳ X; kỳ X > kỳ đầu cần chỉ số của kỳ X−1.
        async Task History(DemoRoom room, Guid contract, int from, int to)
        {
            for (var k = from; k <= to; k++)
            {
                if (k > from)
                    await Close(room, contract, Period(k - 1), 80 + 5 * k);
                await BillAsync(propertyId, room.Id, contract, Period(k), Period(k));
            }
        }

        // Trả phòng sớm trong kỳ đã thu trọn tiền phòng ⇒ phiếu quyết toán có cảnh báo RENT_OVERPAID + dòng hoàn trả ⇒ tổng âm.
        async Task<InvoiceDetailDto> MoveOutEarly(DemoRoom room, Guid contract, DateOnly periodStart, DateOnly leftOn)
        {
            await Send(new StartLiquidationCommand(contract, leftOn, TerminationReason.MutualAgreement, null, "Trả phòng sớm theo thỏa thuận"));
            var final = await Send(new CreateFinalInvoiceCommand(contract, [FinalReading(room.Electricity, 30), FinalReading(room.Water!.Value, 2)]));
            var daysStayed = leftOn.DayNumber - periodStart.DayNumber + 1;
            var periodDays = periodStart.AddMonths(1).DayNumber - periodStart.DayNumber;
            var overpaid = Invoice.Money(PrepaidRent * (periodDays - daysStayed) / periodDays);
            await Send(new AddInvoiceManualLineCommand(final.Summary.Id, InvoiceLineType.Refund, $"Hoàn tiền phòng {periodDays - daysStayed} ngày chưa ở",
                null, null, overpaid, "Trả phòng sớm — đã thu trọn kỳ", null));
            return await FinalizeAsync(final.Summary.Id, leftOn.AddDays(1));
        }

        // B01 — Trả phòng sớm kỳ này: phiếu quyết toán Chờ hoàn (chủ trọ chưa trả lại tiền) — HĐ còn đang thanh lý.
        var b01 = await Room("B01");
        var c01 = await ContractAsync(b01.Id, await RenterAsync("Cao Văn Bình", new DateOnly(1996, 2, 2), Gender.Male, Phone(21)),
            Period(-2), PrepaidRent, b01.Meters, fees: standard);
        await History(b01, c01, -2, 0);
        await MoveOutEarly(b01, c01, Period(0), Period(0).AddDays(Math.Min(9, _today.DayNumber - Period(0).DayNumber)));

        // B02 — Trả phòng sớm kỳ trước, đã hoàn tiền, đã kết thúc; người thuê yêu cầu xóa dữ liệu ⇒ ẩn danh bằng tay.
        var b02 = await Room("B02");
        var tenant02 = await RenterAsync("Đinh Thị Thu", new DateOnly(1997, 5, 19), Gender.Female, Phone(22));
        var c02 = await ContractAsync(b02.Id, tenant02, Period(-3), PrepaidRent, b02.Meters, fees: standard);
        await Send(new RegisterVehicleCommand(c02, tenant02, VehicleType.Motorbike, "29H155667", "Vision trắng", Period(-3), null)); // xóa biển số khi ẩn danh
        await History(b02, c02, -3, -1);
        var refunded = await MoveOutEarly(b02, c02, Period(-1), Period(-1).AddDays(10));
        await Send(new ConfirmInvoiceRefundCommand(refunded.Summary.Id, _today, PaymentMethod.Cash, "Trả tiền mặt khi bàn giao phòng"));
        await Send(new CompleteLiquidationCommand(c02));
        await Send(new AnonymizeRenterCommand(tenant02, "Người thuê yêu cầu xóa dữ liệu cá nhân sau khi trả phòng"));

        // B03 — Thay công tơ điện giữa kỳ trước ⇒ phiếu nháp kỳ này có 2 đoạn đo (công tơ cũ + mới).
        var b03 = await Room("B03");
        var c03 = await ContractAsync(b03.Id, await RenterAsync("Lâm Văn Đức", new DateOnly(1989, 12, 12), Gender.Male, Phone(23)),
            Period(-2), PrepaidRent, b03.Meters, fees: standard);
        await History(b03, c03, -2, -1);
        var oldFinal = _meterValues[b03.Electricity] += 40;
        var newMeter = await Send(new ReplaceMeterCommand(b03.Electricity, Period(-1).AddDays(10), oldFinal, "E-B03-2", 0, "Công tơ cũ cháy"));
        _meterValues[newMeter] = 0;
        await SaveReadings(propertyId,
            Reading(newMeter, c03, Period(-1), Period(0), 55),
            Reading(b03.Water!.Value, c03, Period(-1), Period(0), 5));
        await DraftAsync(propertyId, b03.Id, c03, Period(0));

        // B04 — Ở 3 người, dùng điện nhiều (450 kWh) ⇒ phiếu nháp tính giá bậc.
        var b04 = await Room("B04");
        var c04 = await ContractAsync(b04.Id, await RenterAsync("Hồ Thị Hạnh", new DateOnly(1991, 8, 8), Gender.Female, Phone(24)),
            Period(-1), PrepaidRent, b04.Meters,
            [new(await RenterAsync("Quách Văn Kiên", new DateOnly(1995, 3, 3), Gender.Male, Phone(25)), Period(-1), null, null, null, OccupantRelationship.CoTenant),
             new(await RenterAsync("Hồ Văn Nam", new DateOnly(1993, 6, 1), Gender.Male, Phone(26)), Period(-1), null, null, null, OccupantRelationship.Sibling)],
            fees: [new(trash, null, null), new(parking, 3, null)]);
        await History(b04, c04, -1, -1);
        await Close(b04, c04, Period(-1), 450, 12);
        await DraftAsync(propertyId, b04.Id, c04, Period(0));

        // B05 — phòng trống.
        await Room("B05");
    }
}
