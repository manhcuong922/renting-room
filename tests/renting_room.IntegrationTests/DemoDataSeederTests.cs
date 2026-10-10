using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using renting_room.Infrastructure.Seeding;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>Seeder dữ liệu demo (dev / server test): chạy được, không nhân đôi, đủ các tình huống nghiệp vụ đã chốt.</summary>
[Collection(ApiCollection.Name)]
public sealed class DemoDataSeederTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Seed_CreatesAllScenarios_AndIsIdempotent()
    {
        var options = new DemoDataOptions
        {
            Enabled = true, OrganizationCode = TestApi.NewOrganizationCode(), OwnerPhone = TestApi.NewPhone(), OwnerPassword = "ChuTroDemo2026",
            ManagerPhone = TestApi.NewPhone(), ManagerPassword = "PhoQuanLy2026"
        };
        var seeder = ActivatorUtilities.CreateInstance<DemoDataSeeder>(factory.Services, Options.Create(options));

        (await seeder.SeedAsync(CancellationToken.None)).Should().BeTrue();
        (await ActivatorUtilities.CreateInstance<DemoDataSeeder>(factory.Services, Options.Create(options)).SeedAsync(CancellationToken.None))
            .Should().BeFalse("đã có tổ chức demo ⇒ không nhân đôi");

        var owner = await _client.LoginAsync(options.OwnerPhone, options.OwnerPassword);
        var manager = await _client.LoginAsync(options.ManagerPhone, options.ManagerPassword);
        manager.AccessToken.Should().NotBeNullOrEmpty();
        var token = owner.AccessToken;

        var contracts = await (await _client.GetAsync("/api/v1/contracts?pageSize=100", token)).ReadAsync<JsonElement>();
        contracts.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("status").GetString())
            .Should().Contain(["Draft", "Active", "Liquidating", "Ended", "Cancelled"]);

        var invoices = await (await _client.GetAsync("/api/v1/invoices?pageSize=100", token)).ReadAsync<JsonElement>();
        var items = invoices.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("status").GetString()).Should().Contain("Draft");
        items.Select(i => i.GetProperty("paymentStatus").GetString())
            .Should().Contain(["Paid", "PartiallyPaid", "Overdue", "WrittenOff", "RefundPending", "Refunded"]);

        var withDocument = await (await _client.GetAsync("/api/v1/contracts?missingSignedDocument=false&pageSize=100", token)).ReadAsync<JsonElement>();
        withDocument.GetProperty("totalCount").GetInt32().Should().Be(1, "chỉ phòng 101 đã có bản HĐ ký");
        var missing = await (await _client.GetAsync("/api/v1/contracts?missingSignedDocument=true&pageSize=100", token)).ReadAsync<JsonElement>();
        missing.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(5);
        contracts.GetProperty("items").EnumerateArray()
            .Single(c => c.GetProperty("roomCode").GetString() == "104" && c.GetProperty("status").GetString() == "Active")
            .GetProperty("currentRent").GetDecimal().Should().Be(3_100_000, "giá niêm yết mới đã áp cho người đang thuê từ kỳ tiếp theo");

        var renters = await (await _client.GetAsync("/api/v1/renters?pageSize=100", token)).ReadAsync<JsonElement>();
        renters.GetProperty("items").EnumerateArray().Should().NotContain(r => r.GetProperty("fullName").GetString() == "Đinh Thị Thu",
            "người thuê yêu cầu xóa dữ liệu đã được ẩn danh bằng tay");

        var all = contracts.GetProperty("items").EnumerateArray().ToList();
        async Task<JsonElement> DetailAsync(string room, string status) => await (await _client.GetAsync(
            $"/api/v1/contracts/{all.First(c => c.GetProperty("roomCode").GetString() == room && c.GetProperty("status").GetString() == status).GetProperty("id").GetGuid()}",
            token)).ReadAsync<JsonElement>();

        // Khu C — nhập từ Excel: HĐ cũ, tính tiền từ đầu tháng này (K5).
        var imported = await DetailAsync("C01", "Active");
        imported.GetProperty("billingStartDate").GetString().Should().Be($"{new DateOnly(TestData.Today(factory).Year, TestData.Today(factory).Month, 1):yyyy-MM-dd}");
        imported.GetProperty("occupants").GetArrayLength().Should().Be(3, "vợ chồng + con dưới 14 tuổi không giấy tờ");
        (await DetailAsync("C02", "Active")).GetProperty("hasSignedDocument").GetBoolean().Should().BeFalse("HĐ import mặc định thiếu tài liệu");
        (await DetailAsync("C03", "Active")).GetProperty("representativeRenterId").GetGuid()
            .Should().Be(imported.GetProperty("representativeRenterId").GetGuid(), "1 người đứng tên 2 phòng");
        all.Should().NotContain(c => c.GetProperty("roomCode").GetString() == "C05" || c.GetProperty("roomCode").GetString() == "C09",
            "dòng / phòng lỗi bị bỏ qua khi import");

        // Khu D — đổi ngày chốt 1 → 5 sau khi đã có phiếu: kỳ chuyển tiếp dư 4 ngày, tiền phòng 1 tháng + 4 ngày.
        var properties = await (await _client.GetAsync("/api/v1/properties?pageSize=50", token)).ReadAsync<JsonElement>();
        var khuD = properties.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("code").GetString() == "D").GetProperty("id").GetGuid();
        var billing = (await (await _client.GetAsync($"/api/v1/properties/{khuD}", token)).ReadAsync<JsonElement>()).GetProperty("billing");
        billing.GetProperty("anchorDay").GetInt32().Should().Be(5);
        billing.GetProperty("changes")[0].GetProperty("adjustDays").GetInt32().Should().Be(4);
        var transition = items.Single(i => i.GetProperty("roomCode").GetString() == "D01" && i.GetProperty("status").GetString() == "Draft");
        var transitionDetail = await (await _client.GetAsync($"/api/v1/invoices/{transition.GetProperty("id").GetGuid()}", token)).ReadAsync<JsonElement>();
        transitionDetail.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("type").GetString() == "Rent")
            .GetProperty("description").GetString().Should().Contain("+ 4 ngày");

        // PR-BR-16: nhãn đỏ "Quá hạn"; MT-BR-08: điện = 0 khi có người ở; RT-BR-06: biển số xe đã kết thúc bị xóa khi ẩn danh.
        var rooms = await (await _client.GetAsync("/api/v1/rooms?overdue=true&pageSize=100", token)).ReadAsync<JsonElement>();
        rooms.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Contain("102");
        var draft103 = items.Single(i => i.GetProperty("roomCode").GetString() == "103" && i.GetProperty("status").GetString() == "Draft");
        (await (await _client.GetAsync($"/api/v1/invoices/{draft103.GetProperty("id").GetGuid()}", token)).ReadAsync<JsonElement>())
            .GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().Contain("UNUSUAL_USAGE");
        (await DetailAsync("B02", "Ended")).GetProperty("vehicles")[0].GetProperty("plateNumber").ValueKind.Should().Be(JsonValueKind.Null);

        // FE-UC-08: "Phí điều hòa" thêm hàng loạt cho 101, 102.
        (await DetailAsync("101", "Active")).GetProperty("fees").EnumerateArray().Select(f => f.GetProperty("name").GetString())
            .Should().Contain("Phí điều hòa");

        // Cọc chỉ theo dõi (M08 PM-BR-32..35): đang giữ / đã hoàn trả / đã chuyển (ký lại); HĐ kết thúc chưa hoàn cọc được nhắc.
        static JsonElement Deposit(JsonElement detail) => detail.GetProperty("deposit");
        Deposit(await DetailAsync("101", "Active")).GetProperty("status").GetString().Should().Be("Holding");
        var moved203 = await DetailAsync("206", "Active");
        Deposit(moved203).GetProperty("status").GetString().Should().Be("Holding", "chuyển phòng ⇒ cọc giữ nguyên theo HĐ");
        moved203.GetProperty("roomMoves")[0].GetProperty("roomCode").GetString().Should().Be("203", "203 chuyển sang 206");
        var refunded201 = Deposit(await DetailAsync("201", "Ended"));
        refunded201.GetProperty("status").GetString().Should().Be("Refunded");
        refunded201.GetProperty("refundedAmount").GetDecimal().Should().Be(2_500_000);
        var forfeited = Deposit(await DetailAsync("204", "Cancelled"));
        forfeited.GetProperty("refundedAmount").GetDecimal().Should().Be(0, "khách đổi ý ⇒ giữ lại cọc giữ chỗ, có lý do");
        forfeited.GetProperty("note").GetString().Should().NotBeNullOrEmpty();
        Deposit(await DetailAsync("106", "Ended")).GetProperty("notRefundedReminder").GetBoolean().Should().BeTrue("chưa hoàn cọc");
        var notRefunded = await (await _client.GetAsync("/api/v1/contracts?depositNotRefunded=true&pageSize=100", token)).ReadAsync<JsonElement>();
        notRefunded.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("roomCode").GetString()).Should().Contain("106");
        Deposit(await DetailAsync("B06", "Liquidating")).GetProperty("status").GetString().Should().Be("Transferred", "ký lại ⇒ cọc chuyển sang HĐ mới");
        var final106 = items.Single(i => i.GetProperty("roomCode").GetString() == "106" && i.GetProperty("type").GetString() == "Final");
        (await (await _client.GetAsync($"/api/v1/invoices/{final106.GetProperty("id").GetGuid()}", token)).ReadAsync<JsonElement>())
            .GetProperty("lines").EnumerateArray().Should().Contain(l => l.GetProperty("type").GetString() == "Surcharge"
                && l.GetProperty("description").GetString()!.StartsWith("Bồi thường"));

        // Khoản phát sinh theo phòng (BL-BR-30..37): chờ vào phiếu / trên nháp / đã vào phiếu; khoản đã thanh toán không tính vào tổng.
        var charges = await (await _client.GetAsync("/api/v1/room-charges", token)).ReadAsync<JsonElement>();
        charges.EnumerateArray().Select(c => c.GetProperty("status").GetString()).Should().Contain(["Pending", "OnDraft", "Billed"]);
        charges.EnumerateArray().Where(c => c.GetProperty("status").GetString() == "Pending").Select(c => c.GetProperty("roomCode").GetString())
            .Should().BeEquivalentTo(["101", "102"]);
        var draft103Detail = await (await _client.GetAsync(
            $"/api/v1/invoices/{items.Single(i => i.GetProperty("roomCode").GetString() == "103" && i.GetProperty("status").GetString() == "Draft").GetProperty("id").GetGuid()}",
            token)).ReadAsync<JsonElement>();
        var settledLine = draft103Detail.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("isSettled").GetBoolean());
        settledLine.GetProperty("description").GetString().Should().Be("Làm thêm chìa khóa");
        draft103Detail.GetProperty("summary").GetProperty("totalAmount").GetDecimal()
            .Should().Be(draft103Detail.GetProperty("lines").EnumerateArray().Where(l => !l.GetProperty("isSettled").GetBoolean())
                .Sum(l => l.GetProperty("amount").GetDecimal()) + draft103Detail.GetProperty("summary").GetProperty("roundingAmount").GetDecimal());

        // I1 — nháp B04 "Cần tính lại" (người ở mới vào sau khi lập nháp); H1 — khu D tắt làm tròn.
        var staleDrafts = await (await _client.GetAsync("/api/v1/invoices?stale=true&pageSize=100", token)).ReadAsync<JsonElement>();
        staleDrafts.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("roomCode").GetString()).Should().Equal("B04");
        billing.GetProperty("roundInvoiceTotal").GetBoolean().Should().BeFalse();
        transition.GetProperty("roundingAmount").GetDecimal().Should().Be(0);

        // F1 — phiếu M−1 của 102 hiện nợ M−2 chưa thu.
        var bill102 = items.Where(i => i.GetProperty("roomCode").GetString() == "102").OrderByDescending(i => i.GetProperty("periodStart").GetString()).First();
        var detail102 = await (await _client.GetAsync($"/api/v1/invoices/{bill102.GetProperty("id").GetGuid()}", token)).ReadAsync<JsonElement>();
        detail102.GetProperty("previousDebts").GetArrayLength().Should().Be(1);
        detail102.GetProperty("totalDue").GetDecimal().Should().BeGreaterThan(detail102.GetProperty("summary").GetProperty("outstanding").GetDecimal());

        // M2 — khu C thay công tơ điện hàng loạt: lưới thay chỉ còn công tơ mới (EVN-…).
        var khuC = properties.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("code").GetString() == "C").GetProperty("id").GetGuid();
        var electricityC = await _client.FeeIdAsync(token, khuC, "Điện");
        var replaceSheet = await (await _client.GetAsync($"/api/v1/properties/{khuC}/meters/replace-sheet?feeTypeId={electricityC}", token))
            .ReadAsync<JsonElement>();
        replaceSheet.EnumerateArray().Should().OnlyContain(r => r.GetProperty("serialNo").GetString()!.StartsWith("EVN-"));

        // F2 — 105: thanh toán + bỏ phần còn lại.
        var payments105 = await (await _client.GetAsync(
            $"/api/v1/payments?contractId={all.First(c => c.GetProperty("roomCode").GetString() == "105").GetProperty("id").GetGuid()}", token)).ReadAsync<JsonElement>();
        payments105.EnumerateArray().Should().Contain(p => p.GetProperty("kind").GetString() == "WriteOff" && p.GetProperty("amount").GetDecimal() == 200000);
    }
}
