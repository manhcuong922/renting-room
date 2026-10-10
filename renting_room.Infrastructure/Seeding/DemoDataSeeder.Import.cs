using ClosedXML.Excel;
using renting_room.Application.Billing;
using renting_room.Application.Imports;
using renting_room.Application.Meters;
using renting_room.Application.Properties;
using renting_room.Domain.Properties;

namespace renting_room.Infrastructure.Seeding;

public sealed partial class DemoDataSeeder
{
    /// <summary>
    /// Khu C — chuyển từ sổ ghi chép sang phần mềm bằng Excel: import phòng (PR-UC-11) rồi import người thuê đang ở (RT-UC-10) — xem trước
    /// rồi lưu phần hợp lệ. Có: dòng phòng lỗi bị bỏ qua; gia đình có trẻ dưới 14 tuổi không giấy tờ; hồ sơ đã có được dùng lại; 1 người đứng
    /// tên 2 phòng (ở 1 phòng); phòng không tồn tại bị bỏ qua. HĐ tính tiền từ đầu kỳ hiện tại, mặc định "Thiếu tài liệu".
    /// </summary>
    private async Task SeedImportedPropertyAsync()
    {
        var month0 = new DateOnly(_today.Year, _today.Month, 1);
        var propertyId = await PropertyAsync("C", "Khu C — Nhập từ Excel", "5 Đại Cồ Việt", 1, ChargeMode.Postpaid, companyLessor: false);
        var longAgo = month0.AddYears(-5);
        await AddPriceAsync(await FeeIdAsync(propertyId, "Điện"), longAgo, 3500);
        await AddPriceAsync(await FeeIdAsync(propertyId, "Nước"), longAgo, 15000);

        // Chỉ số đầu kỳ hiện tại ⇒ công tơ tính là lắp từ ngày chốt kỳ này. Dòng C05 sai số người ⇒ bỏ qua.
        var rooms = Workbook(("Phòng",
        [
            ["Mã phòng*", "Tầng", "Diện tích (m²)", "Số người (loại phòng)", "Giá niêm yết", "Tiền cọc mặc định", "Tiện nghi", "Ghi chú",
                "Số seri công tơ điện", "Chỉ số điện đầu kỳ", "Số seri công tơ nước", "Chỉ số nước đầu kỳ"],
            ["C01", "1", 22, 2, 3_200_000, 3_200_000, "air_con, wifi", "Nhập từ sổ", "E-C01", 4210, "W-C01", 520],
            ["C02", "1", 18, 2, 2_600_000, null, "wifi", null, "E-C02", 812, null, null],
            ["C03", "2", 18, 2, 2_600_000, null, null, null, null, null, null, null],
            ["C04", "2", 18, 2, 2_600_000, null, null, "Đang trống", null, null, null, null],
            ["C05", "2", 18, "hai", 2_600_000, null, null, "Gõ nhầm số người", null, null, null, null]
        ]));
        var roomPreview = await Send(new PreviewRoomImportCommand(propertyId, rooms));
        await Send(new SaveRoomImportCommand(propertyId, roomPreview.Rows.Select(r => r.Row).ToList()));

        // Hồ sơ đã có trước ⇒ import dùng lại (không tạo trùng, không sửa).
        await RenterAsync("Phan Văn Rỗi Cũ", new DateOnly(1999, 1, 1), Domain.Renters.Gender.Male, Phone(30), idNumber: CitizenId(300), occupation: "Thợ điện");
        var tenancies = Workbook(("Người thuê",
        [
            ["Mã phòng*", "Vai trò*", "Họ tên*", "Ngày sinh*", "Giới tính*", "SĐT", "Loại giấy tờ", "Số giấy tờ", "Quốc tịch", "Quê quán / thường trú",
                "Nghề nghiệp", "Quan hệ với người đứng tên", "Ngày vào ở*", "Giá thuê", "Tiền cọc", "Đã nhận cọc (Có/Không)"],
            ["C01", "Đứng tên", "Hà Văn Toàn", new DateOnly(1986, 2, 14), "Nam", Phone(31), "CCCD", CitizenId(301), "VN", "Thái Bình", "Lái xe", null,
                month0.AddYears(-2), 3_000_000, 3_000_000],
            ["C01", "Ở cùng", "Nguyễn Thị Hằng", new DateOnly(1990, 8, 8), "Nữ", null, "CCCD", CitizenId(302), "VN", "Thái Bình", null, "Vợ",
                month0.AddYears(-2), null, null],
            ["C01", "Ở cùng", "Hà Minh Khôi", _today.AddYears(-7), "Nam", null, null, null, "VN", null, "Học sinh", "Con", month0.AddYears(-2), null, null],
            ["C02", "Đứng tên", "Phan Văn Rỗi Cũ", new DateOnly(1999, 1, 1), "Nam", Phone(30), "CCCD", CitizenId(300), "VN", null, "Thợ điện", null,
                month0.AddMonths(-8), 2_500_000, 2_500_000, "Không"], // chưa nhận cọc ⇒ cờ "Chưa nhận đủ cọc"
            ["C03", "Đứng tên (không ở)", "Hà Văn Toàn", new DateOnly(1986, 2, 14), "Nam", Phone(31), "CCCD", CitizenId(301), "VN", "Thái Bình", "Lái xe",
                null, month0.AddMonths(-3), 2_400_000, 0],
            ["C03", "Ở cùng", "Đào Thị Nga", new DateOnly(2004, 5, 5), "Nữ", Phone(33), "CCCD", CitizenId(303), "VN", "Nam Định", "Sinh viên", "Cùng ở thuê",
                month0.AddMonths(-3), null, null],
            ["C09", "Đứng tên", "Lý Văn Mạnh", new DateOnly(1995, 3, 3), "Nam", Phone(34), "CCCD", CitizenId(304), "VN", null, null, null,
                month0.AddMonths(-1), 2_000_000, 0]
        ]));
        var tenancyPreview = await Send(new PreviewTenancyImportCommand(propertyId, tenancies));
        await Send(new SaveTenancyImportCommand(propertyId, tenancyPreview.Rows.Select(r => r.Row).ToList()));

        // M2 (MT-UC-08): điện lực thay đồng loạt công tơ điện C01, C02 hôm nay ⇒ phiếu tháng này cộng 2 công tơ (MT-BR-15).
        var electricity = await FeeIdAsync(propertyId, "Điện");
        var sheet = await Send(new GetReplaceSheetQuery(propertyId, electricity));
        await Send(new BulkReplaceMetersCommand(propertyId, electricity, _today,
            sheet.Select(r => new BulkReplaceRow(r.MeterId, (r.LastValue ?? 0) + 15, $"EVN-{r.RoomCode}", 0)).ToList(),
            "Điện lực thay công tơ điện tử đồng loạt"));
    }

    /// <summary>
    /// Khu D — đổi ngày chốt khi đã có phiếu (K4, BL-BR-28): chốt ngày 1 đã lập 2 tháng, đổi sang ngày 5 từ tháng này ⇒ kỳ chuyển tiếp dư 4 ngày,
    /// tiền phòng 1 tháng + 4 ngày (gợi ý), phiếu nháp kỳ chuyển tiếp đã có (chờ chỉ số cuối kỳ).
    /// </summary>
    private async Task SeedBillingChangePropertyAsync()
    {
        var month0 = new DateOnly(_today.Year, _today.Month, 1);
        DateOnly Month(int k) => month0.AddMonths(k);
        var propertyId = await PropertyAsync("D", "Khu D — Đổi ngày chốt", "9 Trần Đại Nghĩa", 1, ChargeMode.Postpaid, companyLessor: false);
        var electricity = await FeeIdAsync(propertyId, "Điện");
        var water = await FeeIdAsync(propertyId, "Nước");
        await AddPriceAsync(electricity, Month(-60), 3500);
        await AddPriceAsync(water, Month(-60), 15000);
        var room = await RoomAsync(propertyId, electricity, water, "D01", "1", 3_000_000, Month(-60));
        var contract = await ContractAsync(room.Id, await RenterAsync("Tô Văn Định", new DateOnly(1991, 6, 6), Domain.Renters.Gender.Male, Phone(32)),
            Month(-2), 3_000_000, room.Meters);
        foreach (var k in new[] { -2, -1 })
        {
            await SaveReadings(propertyId,
                Reading(room.Electricity, contract, Month(k), Month(k + 1), 120),
                Reading(room.Water!.Value, contract, Month(k), Month(k + 1), 5));
            await BillAsync(propertyId, room.Id, contract, Month(k), Month(k + 1));
        }

        var settings = BillingSettings.Standard;
        await Send(new UpdatePropertyBillingCommand(propertyId,
            new PropertyBillingInput(5, ChargeMode.Postpaid, settings.PaymentDueDays, settings.ProrationMode, settings.NoticeDays,
                RoundInvoiceTotal: false), null)); // H1: khu D tắt làm tròn tổng phiếu
        await DraftAsync(propertyId, room.Id, contract, Month(0));
    }

    private static byte[] Workbook(params (string Sheet, object?[][] Rows)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, rows) in sheets)
        {
            var ws = workbook.Worksheets.Add(name);
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    ws.Cell(r + 1, c + 1).Value = rows[r][c] switch
                    {
                        null => Blank.Value,
                        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                        int i => i,
                        decimal m => m,
                        var v => v.ToString()
                    };
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
