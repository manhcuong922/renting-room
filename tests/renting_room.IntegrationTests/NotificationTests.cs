using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Notifications;
using renting_room.Infrastructure.Jobs;
using renting_room.Infrastructure.Notifications;
using renting_room.Infrastructure.Persistence;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M11 (docs/plans/11-notification.md §12, test 7–24): chốt phiếu ⇒ tin vào hàng đợi ⇒ dispatcher gửi qua Zalo / email (bản giả), thử lại, khung giờ,
/// nhắc nợ, liên kết Zalo, cô lập tổ chức. Mỗi test dùng chat id / email riêng và chỉ xét tin của mình (hàng đợi dùng chung cả collection).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync()
    {
        factory.Zalo.Reset();
        factory.Emails.Reset();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Arranged(string Token, Guid OrganizationId, Guid PropertyId, Guid RenterId, Guid InvoiceId, string ChatId, string Email);

    /// <summary>Trong khung giờ gửi (12:00 giờ VN hôm nay) / ngoài khung (22:00) — truyền cho dispatcher thay vì nhảy đồng hồ dùng chung.</summary>
    private DateTimeOffset At(int hourVn) =>
        new(TestData.Today(factory).ToDateTime(new TimeOnly(hourVn, 0)), TimeSpan.FromHours(7));

    private Task<int> DispatchAsync(int hourVn = 12) =>
        factory.Services.GetRequiredService<NotificationDispatcher>().RunOnceAsync(CancellationToken.None, At(hourVn));

    private async Task<Arranged> ArrangeAsync(bool notify = true, bool withEmail = true, bool linkZalo = true, int[]? reminderDays = null)
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = new DateOnly(today.Year, today.Month, 1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1, chargeMode: "Prepaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId, code: "P101");
        var email = $"{Guid.NewGuid():N}@example.com";
        var renterId = await (await _client.PostJsonAsync("/api/v1/renters", new
        {
            fullName = "Trần Thị Lan", dateOfBirth = "2000-04-15", gender = "Female", phone = "0987654321", nationality = "VN",
            email = withEmail ? email : null, idType = "CitizenId", idNumber = TestData.NewCitizenId(), permanentAddress = "Nam Định"
        }, token)).ReadIdAsync();
        var contractId = await _client.CreateContractAsync(token, roomId, renterId, start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        if (notify || reminderDays is not null)
        {
            var settings = await _client.PutJsonAsync($"/api/v1/properties/{propertyId}/notification-settings",
                new { notifyOnFinalize = notify, overdueReminderDays = reminderDays ?? [] }, token);
            settings.StatusCode.Should().Be(HttpStatusCode.OK, await settings.Content.ReadAsStringAsync());
        }

        var chatId = $"chat-{Guid.NewGuid():N}";
        if (linkZalo)
            await LinkAsync(token, renterId, chatId);

        (await _client.PostJsonAsync("/api/v1/invoices/generate", new { propertyId, billingMonth = $"{start:yyyy-MM}" }, token))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var invoices = await (await _client.GetAsync($"/api/v1/invoices?contractId={contractId}", token)).ReadAsync<JsonElement>();
        return new Arranged(token, owner.Organization.OrganizationId, propertyId, renterId, invoices.GetProperty("items")[0].GetProperty("id").GetGuid(), chatId, email);
    }

    private async Task LinkAsync(string token, Guid renterId, string chatId)
    {
        var created = await _client.PostJsonAsync($"/api/v1/renters/{renterId}/zalo-link-code", null, token);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var code = (await created.ReadAsync<JsonElement>()).GetProperty("code").GetString()!;
        var reply = await factory.Services.GetRequiredService<ZaloBotMessageHandler>().HandleAsync(new ZaloUpdate(null, chatId, code), CancellationToken.None);
        reply.Should().Be(ZaloBotMessageHandler.LinkedReply);
    }

    private async Task<JsonElement> FinalizeAsync(Arranged a)
    {
        var response = await _client.FinalizeInvoiceAsync(a.InvoiceId, a.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<JsonElement>();
    }

    private async Task<List<JsonElement>> NotificationsAsync(Arranged a)
    {
        var response = await _client.GetAsync($"/api/v1/invoices/{a.InvoiceId}/notifications", a.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.ReadAsync<List<JsonElement>>());
    }

    private static string Of(JsonElement row, string name) => row.GetProperty(name).GetString()!;

    private IReadOnlyList<string> ZaloTexts(Arranged a) => factory.Zalo.Sent.Where(s => s.ChatId == a.ChatId).Select(s => s.Text).ToList();

    private async Task MakeDueAsync(Arranged a)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
            "UPDATE notification_outbox SET next_attempt_at = {0} WHERE invoice_id = {1}", factory.Clock.GetUtcNow().AddMinutes(-1), a.InvoiceId);
    }

    // ---- test 7, 8, 10

    [Fact]
    public async Task Finalize_WithAutoNotify_QueuesOneRowPerChannel_AndDispatcherSendsBoth()
    {
        var a = await ArrangeAsync();
        await FinalizeAsync(a);

        var rows = await NotificationsAsync(a);
        rows.Should().HaveCount(2);
        rows.Select(r => Of(r, "channel")).Should().BeEquivalentTo("Zalo", "Email");
        rows.Should().OnlyContain(r => Of(r, "status") == "Pending" && Of(r, "kind") == "InvoiceIssued");

        await DispatchAsync();

        (await NotificationsAsync(a)).Should().OnlyContain(r => Of(r, "status") == "Sent");
        var zalo = ZaloTexts(a).Should().ContainSingle().Subject;
        zalo.Should().Contain("P101").And.Contain("TỔNG:").And.Contain("Nhắn HUY");
        zalo.Should().NotContain("0987654321", "NT-BR-17: không có số điện thoại / giấy tờ trong tin");
        var mail = factory.Emails.Sent.Should().ContainSingle(m => m.To == a.Email).Subject;
        mail.Subject.Should().Contain("Phiếu tiền phòng P101");
        mail.Body.Should().Contain("TỔNG:").And.NotContain("Nhắn HUY");
    }

    [Fact]
    public async Task Finalize_WithAutoNotifyOff_QueuesNothing()
    {
        var a = await ArrangeAsync(notify: false);
        await FinalizeAsync(a);
        (await NotificationsAsync(a)).Should().BeEmpty();
    }

    [Fact]
    public async Task Finalize_RenterWithoutAnyChannel_QueuesNothing_AndResendIsRejected()
    {
        var a = await ArrangeAsync(withEmail: false, linkZalo: false);
        await FinalizeAsync(a);
        (await NotificationsAsync(a)).Should().BeEmpty();

        var resend = await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/notifications/resend", null, a.Token);
        resend.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await resend.ReadProblemCodeAsync()).Should().Be("NO_NOTIFICATION_CHANNEL");
    }

    [Fact]
    public async Task ChannelNotConfiguredOnServer_IsSkipped_WhenQueueing()
    {
        var a = await ArrangeAsync();
        factory.Emails.IsConfigured = false;
        try
        {
            await FinalizeAsync(a);
        }
        finally
        {
            factory.Emails.IsConfigured = true;
        }
        (await NotificationsAsync(a)).Should().ContainSingle().Which.GetProperty("channel").GetString().Should().Be("Zalo");
    }

    // ---- test 11, 12, 13

    [Fact]
    public async Task TransientError_KeepsPending_WithAttemptCount_ThenRetrySucceeds()
    {
        var a = await ArrangeAsync(withEmail: false);
        factory.Zalo.OnSend = chat => chat == a.ChatId ? SendResult.Transient("HTTP_500") : SendResult.Ok("x");
        await FinalizeAsync(a);

        await DispatchAsync();
        var row = (await NotificationsAsync(a)).Single();
        Of(row, "status").Should().Be("Pending");
        row.GetProperty("attempts").GetInt32().Should().Be(1);
        Of(row, "lastError").Should().Be("HTTP_500");

        factory.Zalo.OnSend = _ => SendResult.Ok("zalo-ok");
        await DispatchAsync();
        (await NotificationsAsync(a)).Single().GetProperty("status").GetString().Should().Be("Pending", "chưa tới giờ thử lại (+1 phút)");

        await MakeDueAsync(a);
        await DispatchAsync();
        row = (await NotificationsAsync(a)).Single();
        Of(row, "status").Should().Be("Sent");
        row.GetProperty("attempts").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task RecipientBlockedBot_FailsAndUnlinksZalo()
    {
        var a = await ArrangeAsync(withEmail: false);
        factory.Zalo.OnSend = chat => chat == a.ChatId ? SendResult.Unreachable("HTTP_403") : SendResult.Ok("x");
        await FinalizeAsync(a);

        await DispatchAsync();

        Of((await NotificationsAsync(a)).Single(), "status").Should().Be("Failed");
        var renter = await (await _client.GetAsync($"/api/v1/renters/{a.RenterId}", a.Token)).ReadAsync<JsonElement>();
        renter.GetProperty("zaloLinked").GetBoolean().Should().BeFalse("NT-BR-10: chặn bot ⇒ xóa liên kết");
    }

    [Fact]
    public async Task RateLimited_RetriesAfterProviderDelay()
    {
        var a = await ArrangeAsync(withEmail: false);
        factory.Zalo.OnSend = chat => chat == a.ChatId ? SendResult.Transient("RATE_LIMITED", TimeSpan.FromSeconds(1)) : SendResult.Ok("x");
        await FinalizeAsync(a);

        await DispatchAsync();
        var row = (await NotificationsAsync(a)).Single();
        Of(row, "status").Should().Be("Pending");
        Of(row, "lastError").Should().Be("RATE_LIMITED");

        factory.Zalo.OnSend = _ => SendResult.Ok("zalo-ok");
        factory.Clock.Advance(TimeSpan.FromSeconds(2)); // qua retry_after 1 giây (và hết thời gian dispatcher tạm dừng kênh)
        await DispatchAsync();
        Of((await NotificationsAsync(a)).Single(), "status").Should().Be("Sent");
    }

    // ---- test 14

    [Fact]
    public async Task OutsideSendWindow_AutoMessagesWait_ButManualResendGoes()
    {
        var a = await ArrangeAsync(withEmail: false);
        await FinalizeAsync(a);

        await DispatchAsync(hourVn: 22);
        ZaloTexts(a).Should().BeEmpty("NT-BR-08: ngoài 08:00–20:00 tin tự động nằm chờ");
        Of((await NotificationsAsync(a)).Single(), "status").Should().Be("Pending");

        var resend = await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/notifications/resend", new { channels = new[] { "Zalo" } }, a.Token);
        resend.StatusCode.Should().Be(HttpStatusCode.Accepted, await resend.Content.ReadAsStringAsync());
        await DispatchAsync(hourVn: 22);
        ZaloTexts(a).Should().ContainSingle("ManualResend không bị giới hạn khung giờ");

        await DispatchAsync(hourVn: 9);
        ZaloTexts(a).Should().HaveCount(2, "sáng hôm sau tin tự động đi");
    }

    [Fact]
    public async Task Resend_CreatesNewRowEachTime_AndHonoursChannelChoice()
    {
        var a = await ArrangeAsync();
        await FinalizeAsync(a);
        await DispatchAsync();

        var first = await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/notifications/resend", new { channels = new[] { "Email" } }, a.Token);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await first.ReadAsync<List<JsonElement>>()).Should().ContainSingle().Which.GetProperty("channel").GetString().Should().Be("Email");
        (await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/notifications/resend", null, a.Token)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await NotificationsAsync(a)).Where(r => Of(r, "kind") == "ManualResend").Should().HaveCount(3, "1 email + (zalo + email) — số thứ tự khác nhau, không đụng UNIQUE");
    }

    [Fact]
    public async Task Resend_RejectsDraftVoidedAndUnknownChannel()
    {
        var a = await ArrangeAsync();
        (await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/notifications/resend", null, a.Token))
            .ReadProblemCodeAsync().Result.Should().Be("INVOICE_NOT_FINALIZED");
        await FinalizeAsync(a);
        (await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/notifications/resend", new { channels = new[] { "Sms" } }, a.Token))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/void", new { reason = "Nhập sai" }, a.Token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/notifications/resend", null, a.Token))
            .ReadProblemCodeAsync().Result.Should().Be("INVOICE_VOIDED");
    }

    // ---- test 16

    [Fact]
    public async Task VoidInvoice_CancelsPendingMessages()
    {
        var a = await ArrangeAsync();
        await FinalizeAsync(a);
        (await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/void", new { reason = "Nhập sai" }, a.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await NotificationsAsync(a)).Should().OnlyContain(r => Of(r, "status") == "Cancelled");
        await DispatchAsync();
        ZaloTexts(a).Should().BeEmpty();
    }

    // ---- test 15

    [Fact]
    public async Task OverdueReminder_CreatedOncePerStep_AndMentionsDebt()
    {
        var a = await ArrangeAsync(withEmail: false, reminderDays: [1]);
        var finalized = await FinalizeAsync(a);
        var due = DateOnly.Parse(finalized.GetProperty("summary").GetProperty("dueDate").GetString()!);
        await DispatchAsync();
        var scheduler = factory.Services.GetRequiredService<OverdueReminderScheduler>();

        (await scheduler.RunAsync(due, [a.OrganizationId], CancellationToken.None)).Should().Be(0, "đúng hạn chưa phải quá hạn");
        (await scheduler.RunAsync(due.AddDays(1), [a.OrganizationId], CancellationToken.None)).Should().Be(1);
        (await scheduler.RunAsync(due.AddDays(1), [a.OrganizationId], CancellationToken.None)).Should().Be(0, "chạy lần hai không tạo tin trùng");
        (await scheduler.RunAsync(due.AddDays(3), [a.OrganizationId], CancellationToken.None)).Should().Be(0, "không có mốc 3 ngày");

        await DispatchAsync();
        ZaloTexts(a).Should().HaveCount(2);
        ZaloTexts(a).Last().Should().Contain("quá hạn").And.Contain("còn nợ");
        (await NotificationsAsync(a)).Count(r => Of(r, "kind") == "OverdueReminder").Should().Be(1);
    }

    [Fact]
    public async Task OverdueReminder_PaidBeforeSending_IsSkipped()
    {
        var a = await ArrangeAsync(withEmail: false, reminderDays: [1]);
        var finalized = await FinalizeAsync(a);
        var summary = finalized.GetProperty("summary");
        var due = DateOnly.Parse(summary.GetProperty("dueDate").GetString()!);
        await DispatchAsync();
        await factory.Services.GetRequiredService<OverdueReminderScheduler>().RunAsync(due.AddDays(1), [a.OrganizationId], CancellationToken.None);

        var contractId = summary.GetProperty("contractId").GetGuid();
        var paid = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/payments",
            new { amount = summary.GetProperty("totalAmount").GetDecimal(), method = "Cash", paidAt = TestData.Today(factory), invoiceId = a.InvoiceId }, a.Token);
        paid.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        await DispatchAsync();
        (await NotificationsAsync(a)).Single(r => Of(r, "kind") == "OverdueReminder").GetProperty("status").GetString().Should().Be("Skipped");
        ZaloTexts(a).Should().ContainSingle("tin nhắc nợ của phiếu đã trả đủ không gửi");
    }

    // ---- test 17, 18 (liên kết Zalo)

    [Fact]
    public async Task LinkCode_IsOneTimeUse_AndNewCodeRevokesOld()
    {
        var a = await ArrangeAsync(linkZalo: false);
        var bot = factory.Services.GetRequiredService<ZaloBotMessageHandler>();
        string Chat() => $"chat-{Guid.NewGuid():N}";
        async Task<string> NewCode() =>
            (await (await _client.PostJsonAsync($"/api/v1/renters/{a.RenterId}/zalo-link-code", null, a.Token)).ReadAsync<JsonElement>()).GetProperty("code").GetString()!;

        var old = await NewCode();
        var current = await NewCode();
        (await bot.HandleAsync(new ZaloUpdate(null, Chat(), old), CancellationToken.None)).Should().Be(ZaloBotMessageHandler.InvalidCodeReply, "mã cũ hết hiệu lực");

        var chat = Chat();
        (await bot.HandleAsync(new ZaloUpdate(null, chat, current.ToLowerInvariant()), CancellationToken.None)).Should().Be(ZaloBotMessageHandler.LinkedReply);
        (await bot.HandleAsync(new ZaloUpdate(null, Chat(), current), CancellationToken.None)).Should().Be(ZaloBotMessageHandler.InvalidCodeReply, "dùng một lần");
        var renter = await (await _client.GetAsync($"/api/v1/renters/{a.RenterId}", a.Token)).ReadAsync<JsonElement>();
        renter.GetProperty("zaloLinked").GetBoolean().Should().BeTrue();
        renter.TryGetProperty("zaloChatId", out _).Should().BeFalse("chat id là dữ liệu cá nhân — không trả ra API");
    }

    [Fact]
    public async Task WrongCodes_MoreThanFivePerChat_MakeBotSilent()
    {
        var bot = factory.Services.GetRequiredService<ZaloBotMessageHandler>();
        var chat = $"chat-{Guid.NewGuid():N}";
        for (var i = 0; i < ZaloLinkAttemptLimiter.PerChatLimit; i++)
            (await bot.HandleAsync(new ZaloUpdate(null, chat, "ZZZZZZZ2"), CancellationToken.None)).Should().Be(ZaloBotMessageHandler.InvalidCodeReply);
        (await bot.HandleAsync(new ZaloUpdate(null, chat, "ZZZZZZZ2"), CancellationToken.None)).Should().BeNull("sai quá 5 lần ⇒ im lặng");
        (await bot.HandleAsync(new ZaloUpdate(null, chat, "xin chao"), CancellationToken.None)).Should().Be(ZaloBotMessageHandler.HelpReply,
            "tin trò chuyện thường không tính vào số lần sai");
    }

    [Fact]
    public async Task Huy_UnlinksChat_AndCancelsPendingZaloMessages()
    {
        var a = await ArrangeAsync(withEmail: false);
        await FinalizeAsync(a);

        var reply = await factory.Services.GetRequiredService<ZaloBotMessageHandler>().HandleAsync(new ZaloUpdate(null, a.ChatId, "huy"), CancellationToken.None);

        reply.Should().Be(ZaloBotMessageHandler.UnlinkedReply);
        (await _client.GetAsync($"/api/v1/renters/{a.RenterId}", a.Token)).ReadAsync<JsonElement>().Result.GetProperty("zaloLinked").GetBoolean().Should().BeFalse();
        Of((await NotificationsAsync(a)).Single(), "status").Should().Be("Cancelled");
    }

    [Fact]
    public async Task OwnerUnlink_ClearsLink()
    {
        var a = await ArrangeAsync();
        (await _client.DeleteAsync($"/api/v1/renters/{a.RenterId}/zalo-link", a.Token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.GetAsync($"/api/v1/renters/{a.RenterId}", a.Token)).ReadAsync<JsonElement>().Result.GetProperty("zaloLinked").GetBoolean().Should().BeFalse();
    }

    // ---- test 22, 23, 24

    [Fact]
    public async Task OtherOrganization_GetsNotFound_OnEveryEndpoint()
    {
        var a = await ArrangeAsync();
        await FinalizeAsync(a);
        var other = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;

        (await _client.GetAsync($"/api/v1/invoices/{a.InvoiceId}/notifications", other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PostJsonAsync($"/api/v1/invoices/{a.InvoiceId}/notifications/resend", null, other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PostJsonAsync($"/api/v1/renters/{a.RenterId}/zalo-link-code", null, other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.DeleteAsync($"/api/v1/renters/{a.RenterId}/zalo-link", other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PutJsonAsync($"/api/v1/properties/{a.PropertyId}/notification-settings", new { notifyOnFinalize = true, overdueReminderDays = new int[0] }, other))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/v1/notifications/summary?propertyId={a.PropertyId}&billingMonth={DateTime.UtcNow:yyyy-MM}-01", other))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SystemAdmin_IsForbidden()
    {
        var admin = (await _client.LoginAdminAsync()).AccessToken;
        (await _client.GetAsync("/api/v1/notifications/channels", admin)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostJsonAsync($"/api/v1/renters/{Guid.NewGuid()}/zalo-link-code", null, admin)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ZaloNotConfigured_LinkCodeIsRefused_ChannelsReportIt()
    {
        var a = await ArrangeAsync(linkZalo: false);
        factory.Zalo.IsConfigured = false;

        var created = await _client.PostJsonAsync($"/api/v1/renters/{a.RenterId}/zalo-link-code", null, a.Token);
        created.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await created.ReadProblemCodeAsync()).Should().Be("ZALO_BOT_NOT_CONFIGURED");

        var channels = await (await _client.GetAsync("/api/v1/notifications/channels", a.Token)).ReadAsync<JsonElement>();
        channels.GetProperty("zalo").GetProperty("configured").GetBoolean().Should().BeFalse();
        channels.GetProperty("email").GetProperty("configured").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Channels_WhenConfigured_ShowBotName()
    {
        var a = await ArrangeAsync(notify: false, linkZalo: false);
        var channels = await (await _client.GetAsync("/api/v1/notifications/channels", a.Token)).ReadAsync<JsonElement>();
        channels.GetProperty("zalo").GetProperty("configured").GetBoolean().Should().BeTrue();
        channels.GetProperty("zalo").GetProperty("botName").GetString().Should().Be("Bot Nha Tro Test");
    }

    // ---- cài đặt khu (NT-BR-19) + tổng hợp

    [Theory]
    [InlineData(1, 2, 3, 4)]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(3, 3)]
    [InlineData(7, 3)]
    public async Task NotificationSettings_RejectInvalidReminderDays(params int[] days)
    {
        var a = await ArrangeAsync(notify: false, linkZalo: false);
        var response = await _client.PutJsonAsync($"/api/v1/properties/{a.PropertyId}/notification-settings", new { notifyOnFinalize = true, overdueReminderDays = days }, a.Token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync()).Should().Be("INVALID_REMINDER_DAYS");
    }

    [Fact]
    public async Task NotificationSettings_AreReturnedWithPropertyBilling()
    {
        var a = await ArrangeAsync(reminderDays: [1, 3]);
        var property = await (await _client.GetAsync($"/api/v1/properties/{a.PropertyId}", a.Token)).ReadAsync<JsonElement>();
        var billing = property.GetProperty("billing");
        billing.GetProperty("notifyOnFinalize").GetBoolean().Should().BeTrue();
        billing.GetProperty("overdueReminderDays").EnumerateArray().Select(d => d.GetInt32()).Should().Equal(1, 3);
    }

    [Fact]
    public async Task Summary_CountsByStatus_AndInvoicesWithoutChannel()
    {
        var a = await ArrangeAsync();
        await FinalizeAsync(a);
        await DispatchAsync();
        var month = TestData.Today(factory);

        var summary = await (await _client.GetAsync($"/api/v1/notifications/summary?propertyId={a.PropertyId}&billingMonth={month.Year}-{month.Month:00}-01", a.Token))
            .ReadAsync<JsonElement>();
        summary.GetProperty("total").GetInt32().Should().Be(2);
        summary.GetProperty("sent").GetInt32().Should().Be(2);
        summary.GetProperty("noChannel").GetInt32().Should().Be(0);

        var bad = await _client.GetAsync($"/api/v1/notifications/summary?propertyId={a.PropertyId}&billingMonth={month.Year}-{month.Month:00}-15", a.Token);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.ReadProblemCodeAsync()).Should().Be("INVALID_BILLING_MONTH");
    }
}
