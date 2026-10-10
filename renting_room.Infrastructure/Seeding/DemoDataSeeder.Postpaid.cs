using renting_room.Application.Billing;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Application.Payments;
using renting_room.Application.Rooms;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Payments;
using renting_room.Domain.Properties;
using renting_room.Domain.Renters;

namespace renting_room.Infrastructure.Seeding;

/// <summary>
/// Khu A — thu sau (Postpaid), chốt ngày 1: phiếu tháng X = tiền phòng + điện nước của chính tháng X, lập đầu tháng X+1.
/// Mỗi phòng một tình huống (xem docs/guides/demo-data.md).
/// </summary>
public sealed partial class DemoDataSeeder
{
    private async Task SeedPostpaidPropertyAsync()
    {
        var month0 = new DateOnly(_today.Year, _today.Month, 1);
        DateOnly Month(int k) => month0.AddMonths(k);
        var longAgo = Month(-60);

        var propertyId = await PropertyAsync("A", "Khu A — Thu sau", "12 Tạ Quang Bửu", 1, ChargeMode.Postpaid, companyLessor: false);
        var electricity = await FeeIdAsync(propertyId, "Điện");
        var water = await FeeIdAsync(propertyId, "Nước");
        await AddPriceAsync(electricity, longAgo, 3500, "Giá điện một giá");
        await AddPriceAsync(water, longAgo, 15000);
        await AddPriceAsync(water, Month(0), 18000, "Tăng giá nước từ tháng này — báo trước tháng trước");
        var wifi = await ServiceFeeAsync(propertyId, "Wifi", ChargeBasis.PerRoom, "phòng", 100000, longAgo);
        // Giá theo phiên bản: tăng giữa tháng trước ⇒ cả tháng trước tính giá mới (FE-BR-10).
        await AddPriceAsync(wifi, Month(-1).AddDays(9), 120000, "Nâng gói mạng");
        var trash = await ServiceFeeAsync(propertyId, "Rác", ChargeBasis.PerRoom, "phòng", 20000, longAgo);
        var waterPerPerson = await ServiceFeeAsync(propertyId, "Nước theo người", ChargeBasis.PerOccupant, "người", 20000, longAgo);
        var parking = await ServiceFeeAsync(propertyId, "Giữ xe máy", ChargeBasis.PerUnit, "xe", 100000, longAgo, VehicleType.Motorbike);
        ContractFeeRequest[] standard = [new(wifi, null, null), new(trash, null, null)];

        async Task<DemoRoom> Room(string code, bool waterMeter = true) =>
            await RoomAsync(propertyId, electricity, waterMeter ? water : null, code, code[..1], 3000000, longAgo);

        // Ghi chỉ số cuối tháng X (đọc ngày 1 tháng sau) cho các công tơ của phòng.
        Task Close(DemoRoom room, Guid contract, DateOnly periodStart, DateOnly monthStart, decimal kWh, decimal m3 = 4) =>
            SaveReadings(propertyId, [.. new[] { Reading(room.Electricity, contract, periodStart, monthStart.AddMonths(1), kWh) }
                .Concat(room.Water is { } w ? [Reading(w, contract, periodStart, monthStart.AddMonths(1), m3)] : [])]);

        // Thu sau: phiếu tháng X lập ngày 1 tháng X+1.
        Task<InvoiceDetailDto> Bill(DemoRoom room, Guid contract, DateOnly monthStart, decimal? paid = null, bool full = true) =>
            BillAsync(propertyId, room.Id, contract, monthStart, monthStart.AddMonths(1), paid, full);

        // 101 — Lịch sử đầy đủ: vợ chồng + con nhỏ (có đồng ý của người giám hộ), 2 xe máy, 3 tháng đã thu đủ.
        var r101 = await Room("101");
        var husband = await RenterAsync("Phạm Văn Hùng", new DateOnly(1985, 4, 2), Gender.Male, Phone(1));
        var wife = await RenterAsync("Lê Thị Mai", new DateOnly(1988, 9, 20), Gender.Female, Phone(2));
        var child = await RenterAsync("Phạm Minh An", _today.AddYears(-9), Gender.Male, null, occupation: "Học sinh");
        var c101 = await ContractAsync(r101.Id, husband, Month(-3), 3500000, r101.Meters,
            [new(wife, Month(-3), null, null, null, OccupantRelationship.Wife), new(child, Month(-3), null, null, null, OccupantRelationship.Child, true)],
            [.. standard, new(parking, 2, null)]);
        await Send(new RegisterVehicleCommand(c101, husband, VehicleType.Motorbike, "29B112345", "Honda Wave đỏ", Month(-3), null));
        await Send(new RegisterVehicleCommand(c101, wife, VehicleType.Motorbike, "29B167890", "Yamaha Janus trắng", Month(-3), null));
        await Send(new SetSignedDocumentCommand(c101, true, "Bản giấy ký 2 bên, cất tủ hồ sơ khu A")); // HĐ duy nhất đã có bản ký
        for (var k = -3; k <= -1; k++)
        {
            await Close(r101, c101, Month(k), Month(k), 110 + 10 * k);
            await Bill(r101, c101, Month(k));
        }
        // Khoản phát sinh (BL-BR-30..33): hôm nay 101 làm hỏng khóa ⇒ phụ thu chưa thu, tháng này chưa lập phiếu ⇒ "Chờ vào phiếu".
        await Send(new CreateRoomChargeCommand(r101.Id, RoomChargeKind.Surcharge, "Thay khóa cửa", 250000, _today, "Người thuê làm hỏng khóa", null));

        // 102 — Nợ: tháng -2 chưa thu (quá hạn); tháng -1 lập hôm nay (còn trong hạn), mới thu một phần.
        // Người ở ghép chưa khai quan hệ ⇒ cảnh báo RELATIONSHIP_REQUIRED (không chặn).
        var r102 = await Room("102");
        var c102 = await ContractAsync(r102.Id, await RenterAsync("Đỗ Thị Hoa", new DateOnly(1995, 1, 8), Gender.Female, Phone(3)),
            Month(-2), 3000000, r102.Meters,
            [new(await RenterAsync("Lưu Thị Thảo", new DateOnly(1996, 3, 3), Gender.Female, Phone(15)), Month(-2), null, null, null)],
            fees: standard);
        await Close(r102, c102, Month(-2), Month(-2), 95);
        await Bill(r102, c102, Month(-2), full: false);
        await Close(r102, c102, Month(-1), Month(-1), 105);
        await BillAsync(propertyId, r102.Id, c102, Month(-1), _today, paid: 1000000, payInFull: false);
        // Khoản bù chờ vào phiếu tháng này (phiếu tháng trước đã chốt ⇒ đẩy sang kỳ kế tiếp).
        await Send(new CreateRoomChargeCommand(r102.Id, RoomChargeKind.Credit, "Bù mất nước 2 ngày", 40000, _today, "Sự cố bồn nước cả khu", null));

        // 103 — Phiếu nháp tháng trước: sửa tay tiền phòng, phụ thu, giảm trừ (chưa chốt); điện = 0 khi có người ở ⇒ cảnh báo UNUSUAL_USAGE (MT-BR-08).
        var r103 = await Room("103");
        var c103 = await ContractAsync(r103.Id, await RenterAsync("Vũ Văn Nam", new DateOnly(1992, 7, 30), Gender.Male, Phone(4)),
            Month(-1), 3000000, r103.Meters, fees: standard);
        await Close(r103, c103, Month(-1), Month(-1), 0);
        var draft = await DraftAsync(propertyId, r103.Id, c103, Month(-1));
        var rentLine = (await Send(new GetInvoiceQuery(draft))).Lines.Single(l => l.Type == InvoiceLineType.Rent);
        await Send(new EditInvoiceLineCommand(draft, rentLine.Id, null, null, 2800000, "Giảm do sửa nhà vệ sinh 3 ngày"));
        await Send(new AddInvoiceManualLineCommand(draft, InvoiceLineType.Surcharge, "Thay khóa cửa", null, null, 250000, "Người thuê làm hỏng khóa", null));
        await Send(new AddInvoiceManualLineCommand(draft, InvoiceLineType.ManualDiscount, "Mất nước 1 ngày", null, null, 50000, "Sự cố bồn nước", null));
        // Khoản đã thu ngay: vẫn hiện trên nháp nhưng không tính vào tổng (BL-BR-33).
        await Send(new CreateRoomChargeCommand(r103.Id, RoomChargeKind.Surcharge, "Làm thêm chìa khóa", 50000, Min(Month(-1).AddDays(20), _today),
            "Người thuê xin thêm 1 chìa", new ChargeSettlement(Min(Month(-1).AddDays(20), _today), PaymentMethod.Cash)));

        // 104 — Người nước ngoài (hộ chiếu) vào giữa tháng, ở ghép; phòng tính nước theo người (không công tơ nước).
        var r104 = await Room("104", waterMeter: false);
        var foreigner = await RenterAsync("Kim Min-jun", new DateOnly(1990, 11, 3), Gender.Male, Phone(5), IdDocumentType.Passport, "M12345678", "KR", "Kỹ sư");
        var midMonth = Month(-1).AddDays(14);
        var c104 = await ContractAsync(r104.Id, foreigner, midMonth, 2800000, r104.Meters,
            [new(await RenterAsync("Ngô Thị Lan", new DateOnly(1998, 2, 14), Gender.Female, Phone(6)), midMonth, null, null, null, OccupantRelationship.CoTenant)],
            [.. standard, new(waterPerPerson, null, null)]);
        await Close(r104, c104, midMonth, Month(-1), 60);
        await Bill(r104, c104, Month(-1));
        // Tăng giá niêm yết rồi chọn "áp cho người đang thuê" (CT-UC-05): tháng trước đã lập phiếu ⇒ giá mới từ kỳ tiếp theo (tháng này).
        var room104 = await Send(new GetRoomQuery(r104.Id));
        await Send(new UpdateRoomCommand(r104.Id, "104", new RoomSpecInput("1", 20, 3, 3100000, 3000000, ["air_con", "water_heater", "wifi"], null),
            uint.Parse(room104.Version)));
        await Send(new ApplyListedRentCommand(r104.Id));

        // FE-UC-08: lắp điều hòa cho 101, 102 ⇒ thêm "Phí điều hòa" cho 2 phòng một lúc (áp từ kỳ chưa chốt đầu tiên của từng phòng).
        var airCon = await ServiceFeeAsync(propertyId, "Phí điều hòa", ChargeBasis.PerRoom, "phòng", 150000, longAgo);
        await Send(new BulkContractFeeCommand(airCon, FeeUsageAction.Add, [c101, c102], null, null));

        // 105 — Đang thanh lý: đã báo trả phòng, ngày trả còn ở phía trước.
        var r105 = await Room("105");
        var c105 = await ContractAsync(r105.Id, await RenterAsync("Bùi Quang Huy", new DateOnly(1994, 5, 5), Gender.Male, Phone(7)),
            Month(-3), 3200000, r105.Meters, fees: standard);
        for (var k = -3; k <= -1; k++)
        {
            await Close(r105, c105, Month(k), Month(k), 90);
            var bill105 = await Bill(r105, c105, Month(k), full: k < -1);
            if (k == -1) // F2: trả gần đủ, chủ trọ bỏ phần còn lại do mất nước 2 ngày (PM-UC-16).
                await Send(new PayAndWriteOffCommand(bill105.Summary.Id, bill105.Summary.TotalAmount - 200000, PaymentMethod.Cash,
                    Min(Month(0).AddDays(2), _today), null, null, "Mất nước 2 ngày — chủ trọ bỏ 200.000đ"));
        }
        await Send(new StartLiquidationCommand(c105, _today.AddDays(15), TerminationReason.MutualAgreement, null, "Chuyển chỗ làm, báo trước 15 ngày"));

        // 106 — Trả phòng giữa tháng trước, còn nợ, người thuê bỏ đi, làm vỡ gương ⇒ tài sản ghi "Vỡ", bồi thường là phụ thu trên phiếu quyết
        // toán (CT-BR-23); hoàn tất thanh lý bằng bỏ nợ phần không đòi được.
        var r106 = await Room("106");
        var c106 = await ContractAsync(r106.Id, await RenterAsync("Đặng Văn Tú", new DateOnly(1990, 8, 18), Gender.Male, Phone(8)),
            Month(-3), 3000000, r106.Meters, fees: standard, activate: false);
        var mirror = await Send(new AddAssetCommand(c106, new AssetRequest("Gương nhà tắm", 1, "Nguyên vẹn", 300000, null)));
        await Send(new ActivateContractCommand(c106, r106.Meters.Select(m => new MeterReadingInput(m, null)).ToList()));
        await Close(r106, c106, Month(-3), Month(-3), 100);
        await Bill(r106, c106, Month(-3));
        await Close(r106, c106, Month(-2), Month(-2), 100);
        await Bill(r106, c106, Month(-2), full: false);
        var leftOn = Month(-1).AddDays(9);
        await Send(new StartLiquidationCommand(c106, leftOn, TerminationReason.Abandoned, null,
            "Phát hiện bỏ đi ngày 10, đồ để lại: 1 vali — biên bản có tổ trưởng làm chứng"));
        await Send(new RecordAssetReturnCommand(c106, mirror, "Vỡ, phải thay mới"));
        var final106 = await Send(new CreateFinalInvoiceCommand(c106, [FinalReading(r106.Electricity, 35), FinalReading(r106.Water!.Value, 2)]));
        await Send(new AddInvoiceManualLineCommand(final106.Summary.Id, InvoiceLineType.Surcharge, "Bồi thường gương nhà tắm", null, null, 300000,
            "Gương vỡ khi trả phòng", null));
        await FinalizeAsync(final106.Summary.Id, leftOn.AddDays(1));
        // Cọc chưa đánh dấu hoàn trả ⇒ HĐ đã kết thúc hiện "Chưa hoàn cọc" (lọc depositNotRefunded — PM-BR-35).
        await Send(new CompleteLiquidationCommand(c106, DebtSettlement.WriteOff, null, null, "Người thuê bỏ đi, không liên lạc được"));

        // 201 — HĐ cũ đã kết thúc ~10 tháng trước (lịch sử). HĐ không được bắt đầu trước hôm nay quá 1 năm nên không seed được case
        // "quá 36 tháng" — ẩn danh tự động có integration test riêng (ngày giả lập); dữ liệu demo có hồ sơ ẩn danh bằng tay ở B02.
        var r201 = await Room("201");
        var oldStart = Month(-11);
        var c201 = await ContractAsync(r201.Id, await RenterAsync("Hoàng Thị Cúc", new DateOnly(1980, 12, 1), Gender.Female, Phone(9)),
            oldStart, 2500000, r201.Meters, fees: [new(trash, null, null)]);
        var oldEnd = oldStart.AddDays(19);
        await Send(new StartLiquidationCommand(c201, oldEnd, TerminationReason.MutualAgreement, null, "Về quê"));
        var final201 = await Send(new CreateFinalInvoiceCommand(c201, [FinalReading(r201.Electricity, 60), FinalReading(r201.Water!.Value, 3)]));
        await FinalizeAsync(final201.Summary.Id, oldEnd.AddDays(1));
        await Send(new CompleteLiquidationCommand(c201, DebtSettlement.CollectAll, PaymentMethod.Cash, oldEnd.AddDays(1), null));
        await Send(new RefundDepositCommand(c201, oldEnd.AddDays(1), null, null)); // "Đã hoàn trả cọc" đủ 2,5tr

        // 202 — Hết hạn, chờ chủ trọ quyết định (gia hạn / ở tiếp / thanh lý). HĐ nhập từ sổ cũ (chưa có phiếu trong phần mềm).
        var r202 = await Room("202");
        // Người đứng tên không có SĐT ⇒ cảnh báo REPRESENTATIVE_PHONE_MISSING.
        var c202 = await ContractAsync(r202.Id, await RenterAsync("Trịnh Văn Long", new DateOnly(1987, 3, 9), Gender.Male, null),
            Month(-11), 3000000, r202.Meters, fees: standard, end: _today.AddDays(-5));

        // 203 — Hết hạn nhưng vẫn ở tiếp, chưa ký lại (holdover).
        var r203 = await Room("203");
        var c203 = await ContractAsync(r203.Id, await RenterAsync("Lý Thị Ngọc", new DateOnly(1993, 10, 21), Gender.Female, Phone(11)),
            Month(-11), 3000000, r203.Meters, fees: standard, end: _today.AddDays(-20));
        await Send(new StartHoldoverCommand(c203, "Người thuê xin ở thêm 2 tháng, chưa ký phụ lục"));

        // 204 — HĐ nháp sắp vào ở + 1 HĐ nháp đã hủy: khách đổi ý ⇒ ghi "Đã hoàn trả cọc" 0đ kèm lý do (mất cọc giữ chỗ — PM-BR-33).
        var r204 = await Room("204");
        var booked = await ContractAsync(r204.Id, await RenterAsync("Mai Văn Phúc", new DateOnly(1999, 6, 6), Gender.Male, Phone(12)),
            _today.AddDays(3), 3000000, r204.Meters, fees: standard, activate: false);
        var cancelled = await ContractAsync(r204.Id, await RenterAsync("Tạ Thị Yến", new DateOnly(2000, 1, 25), Gender.Female, Phone(13)),
            _today.AddDays(7), 3000000, r204.Meters, fees: standard, activate: false);
        await Send(new CancelContractCommand(cancelled, "Khách đổi ý không thuê"));
        await Send(new RefundDepositCommand(cancelled, _today, 0, "Khách đổi ý không thuê — mất cọc giữ chỗ theo thỏa thuận"));

        // 205 — Phòng bảo trì; 206 — phòng trống. Thêm 1 hồ sơ người thuê chưa từng thuê.
        var r205 = await Room("205");
        await Send(new ChangeRoomStateCommand(r205.Id, RoomAction.StartMaintenance, "Sơn lại tường, thay bình nóng lạnh"));
        // 206 — CT-UC-12: người ở 203 (đang ở tiếp sau hết hạn) chuyển sang 206 hôm nay, giá mới 3,2tr; HĐ, cọc, người ở giữ nguyên.
        var r206 = await Room("206");
        await Send(new TransferRoomCommand(c203, r206.Id, _today, [FinalReading(r203.Electricity, 25), FinalReading(r203.Water!.Value, 2)], null,
            3200000, "Chuyển sang phòng rộng hơn"));
        await RenterAsync("Phan Văn Rỗi", new DateOnly(2001, 4, 4), Gender.Male, Phone(14), occupation: "Sinh viên");
    }
}
