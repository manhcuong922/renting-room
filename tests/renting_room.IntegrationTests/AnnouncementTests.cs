using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using renting_room.Application.Notifications;
using renting_room.Infrastructure.Notifications;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// M11 §17 (docs/plans/11-notification.md §12, test 25–30): thông báo chung của khu — gửi người đứng tên các phòng đang thuê qua Zalo,
/// gửi ngay mọi giờ, gộp theo chat, báo phòng chưa liên kết. Mỗi test dùng chat id riêng và chỉ xét tin của mình.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnnouncementTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync()
    {
        factory.Zalo.Reset();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        factory.Zalo.Reset();
        return Task.CompletedTask;
    }

    private sealed record Arranged(string Token, Guid PropertyId, Guid Room1, Guid Room2, Guid Room3, Guid Renter1, Guid Renter2, string ChatId);

    /// <summary>Khu 3 phòng: P101 có HĐ (người đứng tên đã liên kết Zalo), P102 có HĐ (chưa liên kết), P103 trống.</summary>
    private async Task<Arranged> ArrangeAsync()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var token = owner.Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = new DateOnly(today.Year, today.Month, 1);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: 1);
        var room1 = await _client.CreateRoomAsync(token, propertyId, code: "P101");
        var room2 = await _client.CreateRoomAsync(token, propertyId, code: "P102");
        var room3 = await _client.CreateRoomAsync(token, propertyId, code: "P103");
        var renter1 = await _client.CreateRenterAsync(token);
        var renter2 = await _client.CreateRenterAsync(token);
        foreach (var (room, renter) in new[] { (room1, renter1), (room2, renter2) })
        {
            var contractId = await _client.CreateContractAsync(token, room, renter, start);
            (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var chatId = $"chat-{Guid.NewGuid():N}";
        await LinkAsync(token, renter1, chatId);
        return new Arranged(token, propertyId, room1, room2, room3, renter1, renter2, chatId);
    }

    private async Task LinkAsync(string token, Guid renterId, string chatId)
    {
        var created = await _client.PostJsonAsync($"/api/v1/renters/{renterId}/zalo-link-code", null, token);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var code = (await created.ReadAsync<JsonElement>()).GetProperty("code").GetString()!;
        var reply = await factory.Services.GetRequiredService<ZaloBotMessageHandler>().HandleAsync(new ZaloUpdate(null, chatId, code), CancellationToken.None);
        reply.Should().Be(ZaloBotMessageHandler.LinkedReply);
    }

    private Task<HttpResponseMessage> SendAsync(Arranged a, Guid[]? roomIds = null, string title = "Tăng giá điện từ 01/12/2026",
        string body = "Giá điện tăng lên 3.800đ/kWh, áp dụng từ kỳ thu tháng 12/2026.", string? token = null) =>
        _client.PostJsonAsync($"/api/v1/properties/{a.PropertyId}/announcements", new { title, body, roomIds }, token ?? a.Token);

    /// <summary>22:00 giờ VN — ngoài khung 08–20: thông báo chung vẫn phải đi (NT-BR-23).</summary>
    private Task<int> DispatchAtNightAsync() =>
        factory.Services.GetRequiredService<NotificationDispatcher>().RunOnceAsync(CancellationToken.None,
            new DateTimeOffset(TestData.Today(factory).ToDateTime(new TimeOnly(22, 0)), TimeSpan.FromHours(7)));

    private IReadOnlyList<string> ZaloTexts(string chatId) => factory.Zalo.Sent.Where(s => s.ChatId == chatId).Select(s => s.Text).ToList();

    private async Task<JsonElement> ListAsync(Arranged a, string? token = null)
    {
        var response = await _client.GetAsync($"/api/v1/properties/{a.PropertyId}/announcements", token ?? a.Token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<JsonElement>();
    }

    // ---- test 25, 30

    [Fact]
    public async Task SendToAllRooms_QueuesLinkedRepresentatives_ReportsUnlinkedRooms_AndSendsEvenAtNight()
    {
        var a = await ArrangeAsync();

        var response = await SendAsync(a);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var result = await response.ReadAsync<JsonElement>();
        result.GetProperty("roomCount").GetInt32().Should().Be(2, "P103 trống không tính");
        result.GetProperty("queued").GetInt32().Should().Be(1);
        var noChannel = result.GetProperty("noChannelRooms").EnumerateArray().ToList();
        noChannel.Should().ContainSingle();
        noChannel[0].GetProperty("roomCode").GetString().Should().Be("P102");
        noChannel[0].GetProperty("roomId").GetGuid().Should().Be(a.Room2);

        await DispatchAtNightAsync();

        var text = ZaloTexts(a.ChatId).Should().ContainSingle().Subject;
        text.Should().StartWith("[Khu trọ test] Tăng giá điện từ 01/12/2026");
        text.Should().Contain("Giá điện tăng lên 3.800đ/kWh").And.Contain("Nhắn HUY");

        var list = await ListAsync(a);
        list.GetProperty("totalCount").GetInt32().Should().Be(1);
        var item = list.GetProperty("items")[0];
        item.GetProperty("title").GetString().Should().Be("Tăng giá điện từ 01/12/2026");
        item.GetProperty("allRooms").GetBoolean().Should().BeTrue();
        item.GetProperty("roomCount").GetInt32().Should().Be(2);
        item.GetProperty("recipientCount").GetInt32().Should().Be(1);
        item.GetProperty("sent").GetInt32().Should().Be(1);
        item.GetProperty("pending").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task SendToSelectedRoom_OnlyThatRoomIsTargeted()
    {
        var a = await ArrangeAsync();

        var response = await SendAsync(a, [a.Room1]);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var result = await response.ReadAsync<JsonElement>();
        result.GetProperty("roomCount").GetInt32().Should().Be(1);
        result.GetProperty("queued").GetInt32().Should().Be(1);
        result.GetProperty("noChannelRooms").GetArrayLength().Should().Be(0);
        (await ListAsync(a)).GetProperty("items")[0].GetProperty("allRooms").GetBoolean().Should().BeFalse();
    }

    // ---- test 26

    [Fact]
    public async Task OnlyUnlinkedRooms_Returns422_AndSavesNothing()
    {
        var a = await ArrangeAsync();

        var response = await SendAsync(a, [a.Room2, a.Room3]);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemCodeAsync()).Should().Be("NO_NOTIFICATION_CHANNEL");
        (await ListAsync(a)).GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    // ---- test 27

    [Fact]
    public async Task RoomOfAnotherProperty_Returns404()
    {
        var a = await ArrangeAsync();
        var otherProperty = await _client.CreatePropertyAsync(a.Token);
        var otherRoom = await _client.CreateRoomAsync(a.Token, otherProperty);

        var response = await SendAsync(a, [a.Room1, otherRoom]);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ReadProblemCodeAsync()).Should().Be("ROOM_NOT_FOUND");
    }

    [Fact]
    public async Task InvalidInput_Returns400()
    {
        var a = await ArrangeAsync();

        foreach (var bad in new[]
                 {
                     await SendAsync(a, title: "  "),
                     await SendAsync(a, body: new string('x', 1501)),
                     await SendAsync(a, [a.Room1, a.Room1])
                 })
        {
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await bad.ReadProblemCodeAsync()).Should().Be("VALIDATION_FAILED");
        }
    }

    // ---- test 28

    [Fact]
    public async Task TwoRepresentativesOnSameChat_GetOneMessage()
    {
        var a = await ArrangeAsync();
        await LinkAsync(a.Token, a.Renter2, a.ChatId);

        var response = await SendAsync(a);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var result = await response.ReadAsync<JsonElement>();
        result.GetProperty("roomCount").GetInt32().Should().Be(2);
        result.GetProperty("queued").GetInt32().Should().Be(1);
        result.GetProperty("noChannelRooms").GetArrayLength().Should().Be(0);

        await DispatchAtNightAsync();
        ZaloTexts(a.ChatId).Should().ContainSingle();
    }

    // ---- test 29

    [Fact]
    public async Task ZaloNotConfigured_Returns409()
    {
        var a = await ArrangeAsync();
        factory.Zalo.IsConfigured = false;

        var response = await SendAsync(a);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadProblemCodeAsync()).Should().Be("ZALO_BOT_NOT_CONFIGURED");
    }

    // ---- test 30 (cô lập tổ chức)

    [Fact]
    public async Task OtherOrganization_CannotSendOrList()
    {
        var a = await ArrangeAsync();
        var other = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;

        var send = await SendAsync(a, token: other);
        send.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await send.ReadProblemCodeAsync()).Should().Be("PROPERTY_NOT_FOUND");

        var list = await _client.GetAsync($"/api/v1/properties/{a.PropertyId}/announcements", other);
        list.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ZaloTexts(a.ChatId).Should().BeEmpty();
    }
}
