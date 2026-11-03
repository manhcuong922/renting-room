using renting_room.Application.Billing;
using renting_room.Application.Contracts;
using renting_room.Application.Meters;
using renting_room.Application.Payments;
using renting_room.Application.Renters;
using renting_room.Application.Rooms;
using renting_room.Domain.Billing;
using renting_room.Domain.Common;
using renting_room.Domain.Contracts;
using renting_room.Domain.Fees;
using renting_room.Domain.Meters;
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
        // MT-UC-09: chỉ số điện cuối tháng trước gõ dư 20 kWh, phiếu đã chốt + đã thu ⇒ điều chỉnh chỉ số đã chốt: khoản bù tiền điện
        // dư chờ vào phiếu tháng này, tháng này tính tiếp từ chỉ số đúng.
        var wrong = (await Send(new ListMeterReadingsQuery(r101.Electricity))).First(r => r.Kind == ReadingKind.Periodic);
        await Send(new AdjustMeterReadingCommand(wrong.Id, wrong.Value - 20, "Ghi nhầm chỉ số tháng trước (dư 20 kWh)", AdjustmentHandling.NextInvoice));
        _meterValues[r101.Electricity] -= 20;
        // Khoản phát sinh (BL-BR-30..33): hôm nay 101 làm hỏng khóa ⇒ phụ thu chưa thu, tháng này chưa lập phiếu ⇒ "Chờ vào phiếu".
        await Send(new CreateRoomChargeCommand(r101.Id, RoomChargeKind.Surcharge, "Thay khóa cửa", 250000, _today, "Người thuê làm hỏng khóa", null));

        // 102 — Nợ: tháng -2 chưa thu (quá hạn); tháng -1 lập hôm nay (còn trong hạn), mới thu một phần.
        // Người ở ghép chưa khai quan hệ ⇒ cảnh báo RELATIONSHIP_REQUIRED (không chặn).
        var r102 = await Room("102");
        var thao = await RenterAsync("Lưu Thị Thảo", new DateOnly(1996, 3, 3), Gender.Female, Phone(15));
        var c102 = await ContractAsync(r102.Id, await RenterAsync("Đỗ Thị Hoa", new DateOnly(1995, 1, 8), Gender.Female, Phone(3)),
            Month(-2), 3000000, r102.Meters,
            [new(thao, Month(-2), null, null, null)],
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
            "Phát hiện bỏ đi ngày 10, đồ để lại: 1 vali — biên bản có tổ trưởng làm chứng",
            FinalReadings: [FinalReading(r106.Electricity, 35), FinalReading(r106.Water!.Value, 2)]));
        await Send(new RecordAssetReturnCommand(c106, mirror, "Vỡ, phải thay mới"));
        var final106 = await Send(new CreateFinalInvoiceCommand(c106, null));
        await Send(new AddInvoiceManualLineCommand(final106.Summary.Id, InvoiceLineType.Surcharge, "Bồi thường gương nhà tắm", null, null, 300000,
            "Gương vỡ khi trả phòng", null));
        await FinalizeAsync(final106.Summary.Id, leftOn.AddDays(1));
        // Kết toán (BL-BR-41): nợ tháng −2 chuyển vào, cọc trừ hết; phần còn lại người thuê không trả ⇒ bỏ nợ.
        await Send(new CompleteLiquidationCommand(c106, DebtSettlement.WriteOff, null, null, "Người thuê bỏ đi, không liên lạc được"));

        // 201 — HĐ cũ đã kết thúc ~10 tháng trước (lịch sử). HĐ không được bắt đầu trước hôm nay quá 1 năm nên không seed được case
        // "quá 36 tháng" — ẩn danh tự động có integration test riêng (ngày giả lập); dữ liệu demo có hồ sơ ẩn danh bằng tay ở B02.
        var r201 = await Room("201");
        var oldStart = Month(-11);
        var c201 = await ContractAsync(r201.Id, await RenterAsync("Hoàng Thị Cúc", new DateOnly(1980, 12, 1), Gender.Female, Phone(9)),
            oldStart, 2500000, r201.Meters, fees: [new(trash, null, null)]);
        var oldEnd = oldStart.AddDays(19);
        await Send(new StartLiquidationCommand(c201, oldEnd, TerminationReason.MutualAgreement, null, "Về quê",
            FinalReadings: [FinalReading(r201.Electricity, 60), FinalReading(r201.Water!.Value, 3)]));
        var final201 = await Send(new CreateFinalInvoiceCommand(c201, null));
        await FinalizeAsync(final201.Summary.Id, oldEnd.AddDays(1));
        // Cọc 2,5tr trừ vào hóa đơn trả phòng (BL-BR-41), người thuê trả phần còn lại.
        await Send(new CompleteLiquidationCommand(c201, DebtSettlement.CollectAll, PaymentMethod.Cash, oldEnd.AddDays(1), null));

        // 202 — Hết hạn, chờ chủ trọ quyết định (gia hạn / ở tiếp / thanh lý). HĐ nhập từ sổ cũ (chưa có phiếu trong phần mềm).
        var r202 = await Room("202");
        // Người đứng tên không có SĐT ⇒ cảnh báo REPRESENTATIVE_PHONE_MISSING.
        var c202 = await ContractAsync(r202.Id, await RenterAsync("Trịnh Văn Long", new DateOnly(1987, 3, 9), Gender.Male, null),
            Month(-11), 3000000, r202.Meters, fees: standard, end: _today.AddDays(-5));

        // 203 — Hết hạn nhưng vẫn ở tiếp: chủ trọ bấm "Ở tiếp" ⇒ HĐ thành không thời hạn, hết nhãn (CT-BR-45); phiếu tháng -2 chưa thu ⇒ còn nợ.
        var r203 = await Room("203");
        var c203 = await ContractAsync(r203.Id, await RenterAsync("Lý Thị Ngọc", new DateOnly(1993, 10, 21), Gender.Female, Phone(11)),
            Month(-2), 3000000, r203.Meters, fees: standard, end: _today.AddDays(-20));
        await Send(new StartHoldoverCommand(c203, "Người thuê xin ở thêm 2 tháng, chưa ký phụ lục"));
        // CT-BR-37: quá hạn mà vẫn ở = không thời hạn ⇒ thêm người ở sau ngày hết hạn không cần gia hạn trước.
        await Send(new AddOccupantCommand(c203, await RenterAsync("Lý Văn Khôi", new DateOnly(1996, 5, 17), Gender.Male, Phone(16)),
            _today, null, null, null, OccupantRelationship.Sibling));
        await Close(r203, c203, Month(-2), Month(-2), 90);
        await Bill(r203, c203, Month(-2), full: false);

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
        // PR-BR-16: nợ đi theo HĐ ⇒ nhãn "Còn nợ" sang 206, phòng 203 hết nhãn.
        // CT-BR-47: điện 25 kWh + nước 2 m³ của 203 là sản lượng đã lưu (chỉ số cuối lúc chuyển) — phiếu tháng này cộng vào cùng dòng với 206.
        var r206 = await Room("206");
        // Kỳ đã qua phải có hóa đơn trước khi chuyển phòng (CT-BR-14, 28/10/2026): phiếu tháng M−1 cũng chưa thu.
        await Close(r203, c203, Month(-1), Month(-1), 80);
        await Bill(r203, c203, Month(-1), full: false);
        await Send(new TransferRoomCommand(c203, r206.Id, [FinalReading(r203.Electricity, 25), FinalReading(r203.Water!.Value, 2)], null,
            3200000, "Chuyển sang phòng rộng hơn"));
        // 207 ⇄ 208 — CT-BR-14: hôm nay chuyển nhầm sang 208 rồi chuyển lại về 207 ngay trong ngày ⇒ lần chuyển bị xóa như chưa từng chuyển
        // (chỉ số của lần chuyển bị hủy, giá thuê giữ nguyên), 208 vẫn trống.
        var r207 = await Room("207");
        var r208 = await Room("208");
        var c207 = await ContractAsync(r207.Id, await RenterAsync("Kiều Văn Sang", new DateOnly(1994, 8, 14), Gender.Male, Phone(17)),
            Month(-1), 3000000, r207.Meters, fees: standard);
        await Close(r207, c207, Month(-1), Month(-1), 20);
        await Bill(r207, c207, Month(-1));
        await Send(new TransferRoomCommand(c207, r208.Id,
            [FinalReading(r207.Electricity, 40), FinalReading(r207.Water!.Value, 3)], null, null, "Sửa trần phòng 207"));
        await Send(new TransferRoomCommand(c207, r207.Id, null, null, null, "Chọn nhầm phòng — chuyển lại trong ngày"));
        // 208 (trống lại sau khi 207 chuyển về) — HĐ mới 3 người: người đứng tên, em gái, bạn đồng thuê (CT-BR-49: có tên trên HĐ, chỉ là giấy tờ).
        // Hôm nay: bạn đồng thuê chuyển sang ở 207 (CT-BR-52 — người đi, HĐ ở lại); người đứng tên rời đi, em gái đứng tên thay trên cùng HĐ
        // (CT-BR-51 — nợ, cọc giữ nguyên; HĐ về "Thiếu tài liệu", quan hệ khai lại).
        var sister = await RenterAsync("Tô Thị Hạnh", new DateOnly(1998, 11, 2), Gender.Female, Phone(19));
        var coTenant = await RenterAsync("Ngô Văn Nam", new DateOnly(1997, 6, 23), Gender.Male, Phone(20));
        var start208 = _today.AddDays(-3);
        var c208 = await ContractAsync(r208.Id, await RenterAsync("Tô Văn Bình", new DateOnly(1995, 2, 9), Gender.Male, Phone(18)),
            start208, 3000000, r208.Meters,
            [
                new(sister, start208, null, null, null, OccupantRelationship.Sibling),
                new(coTenant, start208, null, null, null, OccupantRelationship.CoTenant, null, OccupantRole.CoTenant)
            ],
            fees: standard);
        var moving = (await Send(new GetContractQuery(c208))).Occupants.Single(o => o.RenterId == coTenant).Id;
        await Send(new MoveOccupantCommand(c208, moving, c207, _today, OccupantRole.CoTenant, OccupantRelationship.CoTenant, null, false, null));
        await Send(new ChangeRepresentativeCommand(c208, sister, _today, null, true));
        // 209 — HĐ nháp mà người đứng tên (Lưu Thị Thảo) đang là người ở thường của phòng 102 ⇒ chi tiết HĐ nháp cảnh báo
        // REPRESENTATIVE_IS_RESIDENT_ELSEWHERE; kích hoạt bị chặn cho tới khi đổi Thảo thành đồng thuê ở 102 hoặc ghi chuyển đi (CT-BR-50).
        var r209 = await Room("209");
        await ContractAsync(r209.Id, thao, _today.AddDays(5), 3000000, r209.Meters, fees: standard, activate: false);
        await RenterAsync("Phan Văn Rỗi", new DateOnly(2001, 4, 4), Gender.Male, Phone(14), occupation: "Sinh viên");

        // 210, 211 — MT-UC-01: hai phòng mới, lắp công tơ điện bằng 1 lệnh hàng loạt (chưa có công tơ nước).
        // 210 — MT-BR-14: thợ sơn dùng 14 kWh lúc phòng trống ⇒ HĐ nhận phòng ở số 14, phần 0 → 14 hiện ở báo cáo sản lượng phòng trống.
        async Task<Guid> BareRoom(string code) =>
            await Send(new CreateRoomCommand(propertyId, code, new RoomSpecInput(code[..1], 20, 3, 3000000, 3000000, ["air_con", "wifi"], null)));
        var r210 = await BareRoom("210");
        var r211 = await BareRoom("211");
        var bulk = await Send(new BulkInstallMetersCommand(propertyId, electricity, Month(-1),
            [new BulkInstallRow(r210, "E-210", 0), new BulkInstallRow(r211, "E-211", 0)]));
        const decimal vacantUsage = 14;
        var c210 = await ContractAsync(r210, await RenterAsync("Mạc Văn Khôi", new DateOnly(1996, 3, 30), Gender.Male, Phone(21)),
            _today.AddDays(-2), 3000000, [], fees: standard, activate: false);
        await Send(new ActivateContractCommand(c210, [new MeterReadingInput(bulk.MeterIds[0], vacantUsage)]));

        // 212–215 — điều chỉnh chỉ số đã chốt (M06 MT-BR-19, 20/10/2026): điện tháng trước gõ dư 20 kWh, mỗi phòng một cách xử lý.
        async Task<(DemoRoom Room, Guid Contract, InvoiceDetailDto Invoice)> BilledLastMonth(string code, string name, int phone, bool paid)
        {
            var room = await Room(code);
            var contract = await ContractAsync(room.Id, await RenterAsync(name, new DateOnly(1994, 5, 5), Gender.Male, Phone(phone)),
                Month(-1), 3000000, room.Meters, fees: standard);
            await Close(room, contract, Month(-1), Month(-1), 120);
            return (room, contract, await Bill(room, contract, Month(-1), full: paid));
        }
        async Task<MeterReadingDto> TypedTooHigh(DemoRoom room)
        {
            _meterValues[room.Electricity] -= 20;
            return (await Send(new ListMeterReadingsQuery(room.Electricity))).First(r => r.Kind == ReadingKind.Periodic);
        }
        // 212 — hóa đơn chưa thu ⇒ tính lại ngay trên hóa đơn (giữ số, hạn), nhãn "Cần gửi lại".
        var (r212, _, _) = await BilledLastMonth("212", "Âu Văn Lực", 43, paid: false);
        var typed212 = await TypedTooHigh(r212);
        await Send(new AdjustMeterReadingCommand(typed212.Id, typed212.Value - 20, "Ghi nhầm chỉ số tháng trước (dư 20 kWh)"));
        // 213 — đã thu đủ, chọn "Xử lý ngay" ⇒ hóa đơn tính lại, phiếu hoàn tiền PC trả lại phần thừa (tiền mặt hôm nay).
        var (r213, _, _) = await BilledLastMonth("213", "Âu Thị Ngân", 44, paid: true);
        var typed213 = await TypedTooHigh(r213);
        await Send(new AdjustMeterReadingCommand(typed213.Id, typed213.Value - 20, "Ghi nhầm chỉ số tháng trước (dư 20 kWh)",
            AdjustmentHandling.Immediate, new AdjustmentSettlement(_today, PaymentMethod.Cash)));
        // 214 — trả gần đủ, chủ trọ bỏ 100.000đ còn lại; "Xử lý ngay" ⇒ phần thừa trừ vào phần bỏ nợ trước, không có phiếu hoàn.
        var (r214, _, invoice214) = await BilledLastMonth("214", "Âu Văn Tài", 45, paid: false);
        await Send(new PayAndWriteOffCommand(invoice214.Summary.Id, invoice214.Summary.TotalAmount - 100000, PaymentMethod.Cash, _today, null, null,
            "Mất nước 2 ngày, bỏ phần còn lại"));
        var typed214 = await TypedTooHigh(r214);
        await Send(new AdjustMeterReadingCommand(typed214.Id, typed214.Value - 20, "Ghi nhầm chỉ số tháng trước (dư 20 kWh)",
            AdjustmentHandling.Immediate));
        // 215 — trả phòng đầu tháng này; giảm 1.000.000đ (cúp nước) lớn hơn hóa đơn trả phòng ⇒ hóa đơn trả phòng âm, Chờ hoàn
        // (hoàn tất thanh lý bị chặn cho tới khi xác nhận đã trả lại người thuê — BL-BR-10).
        var (r215, c215, _) = await BilledLastMonth("215", "Âu Thị Hằng", 46, paid: true);
        // Chủ trọ chọn "Tính theo ngày ở" cho kỳ cuối (mặc định là thu trọn kỳ — CT-BR-11).
        await Send(new StartLiquidationCommand(c215, Month(0), TerminationReason.MutualAgreement, null, "Về quê", FinalPeriodCharge.Daily,
            [FinalReading(r215.Electricity, 0), FinalReading(r215.Water!.Value, 0)]));
        var final215 = await Send(new CreateFinalInvoiceCommand(c215, null));
        await Send(new CreateRoomChargeCommand(r215.Id, RoomChargeKind.Credit, "Giảm do cúp nước", 1000000, Month(0), "Cúp nước 3 ngày cuối", null));
        await FinalizeAsync(final215.Summary.Id, _today);
        // Lượt 2 (BL-BR-32): mất chìa khóa phát hiện sau khi chốt ⇒ khoản chưa xử lý. Chưa hoàn tất ⇒ màn thanh lý hiện "Cần trả lại",
        // khoản chưa xử lý và cọc (CT-BR-12).
        await Send(new CreateRoomChargeCommand(r215.Id, RoomChargeKind.Surcharge, "Mất chìa khóa", 50000, Month(0), "Phát hiện lúc trả phòng", null));

        // 216 — Lượt 2 (MT-BR-19): tháng trước miễn tiền phòng + giảm gần hết hóa đơn, chưa thu; điện gõ dư 20 kWh ⇒ tính lại sẽ âm
        // (ADJUSTMENT_NEGATIVE_TOTAL) ⇒ chủ trọ chọn "Đẩy sang hóa đơn sau": hóa đơn giữ tổng, khoản bù tiền điện chờ vào tháng này.
        var r216 = await Room("216");
        var c216 = await ContractAsync(r216.Id, await RenterAsync("Âu Văn Khang", new DateOnly(1994, 5, 5), Gender.Male, Phone(47)),
            Month(-1), 3000000, r216.Meters, fees: standard);
        await Close(r216, c216, Month(-1), Month(-1), 120);
        var draft216 = await DraftAsync(propertyId, r216.Id, c216, Month(-1));
        var rent216 = (await Send(new GetInvoiceQuery(draft216))).Lines.Single(l => l.Type == InvoiceLineType.Rent).Id;
        var edited216 = await Send(new EditInvoiceLineCommand(draft216, rent216, null, null, 0, "Miễn tiền phòng tháng đầu (sửa nhà)"));
        await Send(new AddInvoiceManualLineCommand(draft216, InvoiceLineType.ManualDiscount, "Giảm do mất điện nhiều ngày", null, null,
            edited216.Subtotal + edited216.DiscountTotal - 30000, "Mất điện 10 ngày", null));
        await FinalizeAsync(draft216, Month(0));
        var typed216 = await TypedTooHigh(r216);
        await Send(new AdjustMeterReadingCommand(typed216.Id, typed216.Value - 20, "Ghi nhầm chỉ số tháng trước (dư 20 kWh)",
            AdjustmentHandling.NextInvoice));

        // 217 — báo trả phòng ngày 15 tháng này (CT-UC-07) ⇒ chỉ là nhãn: phòng, lưới chỉ số hiện "Trả phòng 15/MM", hóa đơn tháng vẫn tạo
        // bình thường. Đổi ý ở tiếp ⇒ rút báo trả phòng.
        var (_, c217, _) = await BilledLastMonth("217", "Âu Thị Mai", 48, paid: true);
        await Send(new GiveNoticeCommand(c217, Month(0), Month(0).AddDays(14)));

        // 218 — hoàn tất thanh lý một lần bấm (CT-BR-12): trả phòng ngày 1, kỳ cuối mặc định "Thu trọn kỳ" (CT-BR-11); hóa đơn trả
        // phòng đã thu; mất chìa khóa sau khi chốt ⇒ coi như đã thu tay khi hoàn tất; "Không trả cọc" kèm lý do (PM-BR-33).
        var (r218, c218, _) = await BilledLastMonth("218", "Âu Văn Phúc", 49, paid: true);
        await Send(new StartLiquidationCommand(c218, Month(0), TerminationReason.LesseeUnilateral, null, "Báo trả phòng muộn",
            FinalReadings: [FinalReading(r218.Electricity, 0), FinalReading(r218.Water!.Value, 0)]));
        var final218 = await Send(new CreateFinalInvoiceCommand(c218, null));
        // "Không trả cọc" trước khi chốt ⇒ hóa đơn trả phòng không trừ cọc (BL-BR-41).
        await Send(new ForfeitDepositCommand(c218, _today, "Báo trả phòng muộn, mất cọc theo điều khoản HĐ"));
        var done218 = await FinalizeAsync(final218.Summary.Id, _today);
        await PayAsync(c218, final218.Summary.Id, done218.Summary.TotalAmount, _today, PaymentMethod.Cash);
        await Send(new CreateRoomChargeCommand(r218.Id, RoomChargeKind.Surcharge, "Mất chìa khóa", 50000, Month(0), "Phát hiện lúc trả phòng", null));
        await Send(new CompleteLiquidationCommand(c218, PendingCharges: new LiquidationPendingCharges(PaymentMethod.Cash)));

        // 219, 220 — số cuối điện lúc trả phòng gõ dư 30 kWh, hóa đơn trả phòng đã thu, HĐ đã kết thúc.
        async Task<(DemoRoom Room, Guid Contract)> EndedWithHighFinal(string code, string name, int phone)
        {
            var (room, contract, _) = await BilledLastMonth(code, name, phone, paid: true);
            await Send(new StartLiquidationCommand(contract, Month(0), TerminationReason.MutualAgreement, null, "Chuyển chỗ làm",
                FinalReadings: [FinalReading(room.Electricity, 30), FinalReading(room.Water!.Value, 0)]));
            var final = await Send(new CreateFinalInvoiceCommand(contract, null));
            var done = await FinalizeAsync(final.Summary.Id, _today);
            // Kết toán (BL-BR-41): cọc trừ vào hóa đơn trả phòng — còn thiếu thì thu, dư thì trả lại.
            if (done.Summary.TotalAmount > 0)
                await PayAsync(contract, final.Summary.Id, done.Summary.TotalAmount, _today, PaymentMethod.Cash);
            await Send(new CompleteLiquidationCommand(contract,
                Refund: done.Summary.TotalAmount < 0 ? new LiquidationRefund(_today, PaymentMethod.Cash) : null));
            _meterValues[room.Electricity] -= 30;
            return (room, contract);
        }
        // 219 — phát hiện sau ⇒ "Điều chỉnh số cuối" (MT-BR-19 Sau thanh lý): hóa đơn trả phòng giữ nguyên, "Chênh lệch sau thanh lý"
        // 105.000đ đã trả lại (chuyển khoản hôm nay).
        var (r219, _) = await EndedWithHighFinal("219", "Âu Thị Quyên", 50);
        var final219 = (await Send(new ListMeterReadingsQuery(r219.Electricity))).First(r => r.Kind == ReadingKind.Final);
        await Send(new AdjustMeterReadingCommand(final219.Id, final219.Value - 30, "Số cuối lúc trả phòng gõ dư 30 kWh",
            Settlement: new AdjustmentSettlement(_today, PaymentMethod.BankTransfer)));
        // 220 — người mới vào ngày 2, nhập đúng số trên công tơ (nhỏ hơn số cuối người cũ), chọn chấp nhận (CT-BR-02, M-10 28/10/2026)
        // ⇒ mỗi HĐ có dải số riêng: số cuối và hóa đơn người cũ giữ nguyên, không sinh khoản chênh lệch.
        var (r220, _) = await EndedWithHighFinal("220", "Âu Văn Sơn", 51);
        var c220 = await ContractAsync(r220.Id, await RenterAsync("Lạc Văn Toàn", new DateOnly(1999, 7, 7), Gender.Male, Phone(52)),
            Month(0).AddDays(1), 3000000, r220.Meters, fees: standard, activate: false);
        await Send(new ActivateContractCommand(c220,
            [new MeterReadingInput(r220.Electricity, _meterValues[r220.Electricity], AcceptBelowPrevious: true), new MeterReadingInput(r220.Water!.Value, null)]));

        // 221 — kỳ ảo (CT-UC-08): tháng -2 đã chốt + thu, tháng trước chỉ có nháp nhưng người thuê thực ra đã đi ⇒ "Coi như trả phòng từ"
        // ngày cuối tháng -2: nháp tháng trước bị xóa, không cần hóa đơn trả phòng / chỉ số cuối, hoàn tất ngay.
        var r221 = await Room("221");
        var c221 = await ContractAsync(r221.Id, await RenterAsync("Âu Văn Tiến", new DateOnly(1993, 9, 9), Gender.Male, Phone(53)),
            Month(-2), 3000000, r221.Meters, fees: standard);
        await Close(r221, c221, Month(-2), Month(-2), 100);
        await Bill(r221, c221, Month(-2));
        await Close(r221, c221, Month(-1), Month(-1), 5);
        await DraftAsync(propertyId, r221.Id, c221, Month(-1));
        await Send(new StartLiquidationCommand(c221, Month(-1).AddDays(-1), TerminationReason.Abandoned, null,
            "Phát hiện đã bỏ đi từ đầu tháng trước — coi như trả phòng từ ngày chốt kỳ trước",
            FinalReadings: await ReadingsAtAsync(r221, Month(-1).AddDays(-1))));
        await Send(new CompleteLiquidationCommand(c221));

        // 222 — kỳ cuối "Không thu" (CT-BR-11): trả phòng ngày 1, chủ trọ miễn tiền phòng + dịch vụ kỳ cuối, vẫn tính điện; hóa đơn trả
        // phòng còn nháp ⇒ màn thanh lý xem 3 cách tính, đổi qua lại được.
        var (r222, c222, _) = await BilledLastMonth("222", "Âu Thị Vân", 54, paid: true);
        await Send(new StartLiquidationCommand(c222, Month(0), TerminationReason.MutualAgreement, null, "Về quê sớm", FinalPeriodCharge.None,
            [FinalReading(r222.Electricity, 5), FinalReading(r222.Water!.Value, 0)]));
        await Send(new CreateFinalInvoiceCommand(c222, null));

        // 223 — báo trả phòng ngày 15 tháng trước nhưng chủ trọ chưa bấm thanh lý (CT-UC-07) ⇒ nhãn đỏ "Quá ngày trả phòng" trên HĐ và thẻ
        // phòng (kỳ cuối chưa tính theo ngày ở) — bấm Thanh lý, hoặc rút báo trả phòng nếu người thuê ở tiếp.
        var r223 = await Room("223");
        var c223 = await ContractAsync(r223.Id, await RenterAsync("Âu Văn Đạt", new DateOnly(1995, 1, 15), Gender.Male, Phone(56)),
            Month(-2), 3000000, r223.Meters, fees: standard);
        await Close(r223, c223, Month(-2), Month(-2), 100);
        await Bill(r223, c223, Month(-2), full: false);
        await Send(new GiveNoticeCommand(c223, Month(-2).AddDays(20), Month(-1).AddDays(14)));

        // 224 — trả phòng trước "Tính tiền từ ngày" (HĐ miễn 2 tuần đầu, người thuê đi sau 10 ngày): không có kỳ nào để tính ⇒ không cần
        // hóa đơn trả phòng / chỉ số cuối, hoàn tất thanh lý ngay; cọc để sau.
        var r224 = await Room("224");
        var c224 = await ContractAsync(r224.Id, await RenterAsync("Âu Thị Lan", new DateOnly(1996, 2, 2), Gender.Female, Phone(57)),
            Month(-1), 3000000, r224.Meters, fees: standard, billingStart: Month(-1).AddDays(14));
        await Send(new StartLiquidationCommand(c224, Month(-1).AddDays(9), TerminationReason.MutualAgreement, null, "Đổi ý, đi sau 10 ngày"));
        // Không có hóa đơn trả phòng ⇒ cọc quyết ở màn hoàn tất; "Để sau" ⇒ nhắc "Chưa hoàn cọc" (PM-BR-35).
        await Send(new CompleteLiquidationCommand(c224));

        // 225 — hóa đơn trả phòng âm như 215, nhưng người thuê bỏ đi không liên lạc được: chủ trọ chọn "Không hoàn" (có lý do, chỉ chủ trọ)
        // và "Không trả cọc" ⇒ hóa đơn "Không hoàn" (RefundWaived), HĐ kết thúc.
        var (r225, c225, _) = await BilledLastMonth("225", "Âu Văn Bảo", 58, paid: true);
        await Send(new StartLiquidationCommand(c225, Month(0), TerminationReason.Abandoned, null, "Bỏ đi không báo, không liên lạc được",
            FinalPeriodCharge.Daily, [FinalReading(r225.Electricity, 0), FinalReading(r225.Water!.Value, 0)]));
        var final225 = await Send(new CreateFinalInvoiceCommand(c225, null));
        await Send(new CreateRoomChargeCommand(r225.Id, RoomChargeKind.Credit, "Giảm do cúp nước", 1000000, Month(0), "Cúp nước 3 ngày cuối", null));
        await Send(new ForfeitDepositCommand(c225, _today, "Bỏ đi không báo, mất cọc theo điều khoản HĐ"));
        await FinalizeAsync(final225.Summary.Id, _today);
        await Send(new CompleteLiquidationCommand(c225,
            Refund: new LiquidationRefund(_today, Waive: true, Reason: "Người thuê bỏ đi, không liên lạc được")));

        // 226 — báo trả phòng chỉ là nhãn (CT-UC-07): báo đi giữa tháng trước nhưng người thuê ở lại, hóa đơn tháng trước vẫn lập bình thường;
        // chưa thanh lý ⇒ nhãn đỏ "Quá ngày trả phòng" — thanh lý hoặc rút báo trả phòng.
        var r226 = await Room("226");
        var c226 = await ContractAsync(r226.Id, await RenterAsync("Âu Văn Quang", new DateOnly(1994, 8, 8), Gender.Male, Phone(59)),
            Month(-2), 3000000, r226.Meters, fees: standard);
        await Close(r226, c226, Month(-2), Month(-2), 100);
        await Bill(r226, c226, Month(-2));
        await Send(new GiveNoticeCommand(c226, Month(-2).AddDays(20), Month(-1).AddDays(9)));
        await Close(r226, c226, Month(-1), Month(-1), 90);
        await Bill(r226, c226, Month(-1), full: false);

        // 227, 228 — kết toán (BL-BR-41): tháng trước chưa thu, trả phòng ngày 1 (tính theo ngày ở) ⇒ nợ tháng trước chuyển vào hóa đơn trả
        // phòng, cọc trừ vào. Hóa đơn tháng trước đi theo hóa đơn trả phòng: 227 chưa thu ⇒ "Chờ thu ở hóa đơn trả phòng"; 228 thu đủ, HĐ kết
        // thúc ⇒ "Hoàn thành (qua hóa đơn trả phòng)".
        async Task<(Guid Contract, InvoiceDetailDto Final)> SettledAtMoveOut(string code, string name, int phone)
        {
            var (room, contract, _) = await BilledLastMonth(code, name, phone, paid: false);
            await Send(new StartLiquidationCommand(contract, Month(0), TerminationReason.MutualAgreement, null, "Chuyển chỗ làm", FinalPeriodCharge.Daily,
                [FinalReading(room.Electricity, 0), FinalReading(room.Water!.Value, 0)]));
            var final = await Send(new CreateFinalInvoiceCommand(contract, null));
            return (contract, await FinalizeAsync(final.Summary.Id, _today));
        }
        await SettledAtMoveOut("227", "Âu Thị Hoa", 60);
        var (c228, final228) = await SettledAtMoveOut("228", "Âu Văn Khánh", 61);
        var due228 = final228.Summary.TotalAmount;
        if (due228 > 0)
            await PayAsync(c228, final228.Summary.Id, due228, _today, PaymentMethod.Cash);
        await Send(new CompleteLiquidationCommand(c228, Refund: due228 < 0 ? new LiquidationRefund(_today, PaymentMethod.Cash) : null));

        // 229, 230 — thu tay ngoài hệ thống (M07 BL-UC-18, BL-BR-42..46, 27/10/2026): chủ trọ chỉ dùng phần mềm tháng M−5 rồi ngừng, tháng M−4..M−2
        // thu tay ngoài.
        // 229 — dùng lại: lập phiếu tháng M−1 với "Bỏ qua các kỳ chưa lập" ⇒ khoảng ngắt M−4..M−2 (3 kỳ); chỉ số đầu là chỉ số chốt tháng M−2
        // (điện dùng 3 tháng không tính trong phần mềm), tháng M−1 chỉ tính phần dùng của chính tháng đó.
        var r229 = await Room("229");
        var c229 = await ContractAsync(r229.Id, await RenterAsync("Cao Văn Tâm", new DateOnly(1991, 6, 12), Gender.Male, Phone(62)),
            Month(-5), 3000000, r229.Meters, fees: standard);
        await Close(r229, c229, Month(-5), Month(-5), 100);
        await Bill(r229, c229, Month(-5));
        await Close(r229, c229, Month(-2), Month(-2), 300);
        await Close(r229, c229, Month(-1), Month(-1), 95);
        var resumed = await Send(new GenerateInvoicesCommand(propertyId, $"{Month(-1):yyyy-MM}", [r229.Id], null, false, null, true));
        if (resumed is not { Created: 1, GapsCreated: 1 })
            throw new InvalidOperationException($"Demo seed: gap invoice 229 not created: {string.Join(", ", resumed.Skipped.Select(s => s.Reason))}");
        var invoice229 = await FinalizeAsync((await InvoiceAsync(c229, Month(-1))).Id, Month(0));
        await PayAsync(c229, invoice229.Summary.Id, invoice229.Summary.TotalAmount, Month(0).AddDays(2), PaymentMethod.Cash);

        // 230 — người thuê bỏ đi giữa tháng M−3 trong lúc thu tay: phiếu M−5 còn nợ, thanh lý "đã xử lý ngoài hệ thống" — không hóa đơn trả phòng,
        // khoảng ngắt M−4..hết kỳ trả phòng đóng bởi thanh lý (BL-BR-46); nợ phiếu M−5 bỏ nợ, cọc không trả.
        var r230 = await Room("230");
        var c230 = await ContractAsync(r230.Id, await RenterAsync("Cao Thị Thủy", new DateOnly(1993, 9, 9), Gender.Female, Phone(63)),
            Month(-5), 3000000, r230.Meters, fees: standard);
        await Close(r230, c230, Month(-5), Month(-5), 100);
        await Bill(r230, c230, Month(-5), full: false);
        await Send(new StartLiquidationCommand(c230, Month(-3).AddDays(14), TerminationReason.Abandoned, null,
            "Bỏ đi giữa tháng, thu tay đến ngày đi — ghi chú biên bản có tổ trưởng làm chứng",
            FinalReadings: await ReadingsAtAsync(r230, Month(-3).AddDays(14))));
        await Send(new CompleteLiquidationCommand(c230, DebtSettlement.WriteOff, null, null, "Phiếu tháng đầu chưa thu được, người thuê đã đi",
            Deposit: new LiquidationDeposit(DepositAction.Forfeit, null, null, "Bỏ đi không báo, mất cọc theo điều khoản HĐ"),
            ExternalSettlement: new LiquidationExternalSettlement("Đã thu tay các tháng M−4..M−3 đến ngày đi, sổ giấy khu A")));

        // 231 — chỉ số cuối nhập ngay khi bấm thanh lý (CT-BR-11, H-02 28/10/2026): người cũ trả phòng hôm nay, số cuối ghi luôn; người mới vào hôm
        // sau bấm "Dùng số mới nhất" ⇒ nhận phòng đúng số cuối người cũ (không phải số chốt kỳ trước).
        var (r231, c231, _) = await BilledLastMonth("231", "Quách Văn Hải", 64, paid: true);
        await Send(new StartLiquidationCommand(c231, Month(0), TerminationReason.MutualAgreement, null, "Chuyển chỗ làm",
            FinalReadings: [FinalReading(r231.Electricity, 12), FinalReading(r231.Water!.Value, 1)]));
        var final231 = await Send(new CreateFinalInvoiceCommand(c231, null));
        var done231 = await FinalizeAsync(final231.Summary.Id, _today);
        if (done231.Summary.TotalAmount > 0)
            await PayAsync(c231, final231.Summary.Id, done231.Summary.TotalAmount, _today, PaymentMethod.Cash);
        await Send(new CompleteLiquidationCommand(c231, Refund: done231.Summary.TotalAmount < 0 ? new LiquidationRefund(_today, PaymentMethod.Cash) : null));
        var c231Next = await ContractAsync(r231.Id, await RenterAsync("Tô Thị Mai", new DateOnly(1997, 3, 3), Gender.Female, Phone(65)),
            Month(0).AddDays(1), 3000000, r231.Meters, fees: standard, activate: false);
        await Send(new ActivateContractCommand(c231Next, [new MeterReadingInput(r231.Electricity, null), new MeterReadingInput(r231.Water!.Value, null)]));

        // 232 → 233 — chuyển phòng khi còn kỳ đã qua chưa lập hóa đơn (CT-BR-14, M-09 28/10/2026): chỉ lập tháng M−3, tháng M−2..M−1 thu tay ngoài hệ thống
        // ⇒ chuyển phòng với "Bỏ qua kỳ rỗng" ⇒ khoảng ngắt 2 kỳ; chỉ số cuối phòng cũ không bị kỳ nào mượn làm số cuối kỳ.
        var r232 = await Room("232");
        var r233 = await Room("233");
        var c232 = await ContractAsync(r232.Id, await RenterAsync("Từ Văn Khải", new DateOnly(1994, 4, 4), Gender.Male, Phone(66)),
            Month(-3), 3000000, r232.Meters, fees: standard);
        await Close(r232, c232, Month(-3), Month(-3), 100);
        await Bill(r232, c232, Month(-3));
        await Send(new TransferRoomCommand(c232, r233.Id, [FinalReading(r232.Electricity, 25), FinalReading(r232.Water!.Value, 2)], null, null,
            "Chuyển sang phòng 233, hai tháng trước thu tay", SkipUnbilledPeriods: true));

        // 234 — số đầu kỳ lấy từ lưới (H-03, 31/10/2026): nháp tháng M−2 và M−1 đã lập, sau đó hủy chỉ số điện chốt tháng M−2 (ghi nhầm) ⇒
        // cả 2 nháp thiếu chỉ số; nháp M−1 KHÔNG lùi về chỉ số nhận phòng để tính gộp 2 tháng. Nhập lại chỉ số chốt M−2 ở lưới ⇒ cả 2 tự tính lại.
        var r234 = await Room("234");
        var c234 = await ContractAsync(r234.Id, await RenterAsync("Lạc Văn Bình", new DateOnly(1995, 5, 5), Gender.Male, Phone(67)),
            Month(-2), 3000000, r234.Meters, fees: standard);
        await Close(r234, c234, Month(-2), Month(-2), 80);
        await Close(r234, c234, Month(-1), Month(-1), 80);
        await DraftAsync(propertyId, r234.Id, c234, Month(-2));
        await DraftAsync(propertyId, r234.Id, c234, Month(-1));
        var closing234 = (await Send(new ListMeterReadingsQuery(r234.Electricity)))
            .Where(r => r.Kind == ReadingKind.Periodic).OrderBy(r => r.ReadingDate).First();
        await Send(new VoidMeterReadingCommand(closing234.Id, "Ghi nhầm số phòng bên cạnh"));

        // RT-UC-05: 101 — chồng đồng ý theo điều khoản HĐ; vợ xác nhận miệng việc gửi thông báo rồi rút lại 10 ngày trước.
        await Send(new AddRenterConsentCommand(husband, "Quản lý hợp đồng thuê, đăng ký tạm trú", ConsentMethod.ContractClause, Month(-3), "Điều 9 hợp đồng"));
        var notify = await Send(new AddRenterConsentCommand(wife, "Nhận thông báo tiền phòng qua Zalo", ConsentMethod.Verbal, Month(-3), null));
        await Send(new WithdrawRenterConsentCommand(wife, notify.Id, _today.AddDays(-10)));
        // RT-BR-05: hồ sơ cũ đã ẩn khỏi danh sách (không xóa dữ liệu) — hiện lại bằng lọc "cả hồ sơ đã ẩn".
        var archived = await RenterAsync("Vũ Văn Cũ", new DateOnly(1979, 12, 1), Gender.Male, Phone(22), occupation: "Lái xe");
        await Send(new ArchiveRenterCommand(archived, true));
    }
}
