using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using renting_room.Application.Notifications;
using renting_room.Infrastructure.Notifications;

namespace renting_room.Infrastructure.UnitTests;

/// <summary>M11 NT-T07 / NT-BR-09 / NT-BR-10 / NT-BR-14 / L-19: đọc phản hồi Zalo, phân loại lỗi, bộ giới hạn dò mã, token.</summary>
public sealed class ZaloBotTests
{
    private const string Token = "123456:secret-token";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return reply(request);
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (ZaloBotClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> reply, string? token = Token)
    {
        var handler = new StubHandler(reply);
        var client = new ZaloBotClient(new StubFactory(handler), Options.Create(new ZaloBotOptions { Token = token }), NullLogger<ZaloBotClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task SendMessage_PostsToBotTokenPath_AndReturnsMessageId()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":"m-7"}}"""));

        var result = await client.SendMessageAsync("chat-1", "Xin chào", CancellationToken.None);

        result.Outcome.Should().Be(SendOutcome.Ok);
        result.ProviderMessageId.Should().Be("m-7");
        handler.Urls.Should().ContainSingle().Which.Should().EndWith($"/bot{Token}/sendMessage");
        handler.Bodies.Single().Should().Contain("chat-1").And.Contain("Xin ch");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, SendOutcome.Transient)]
    [InlineData(HttpStatusCode.BadGateway, SendOutcome.Transient)]
    [InlineData(HttpStatusCode.Forbidden, SendOutcome.RecipientUnreachable)]
    [InlineData(HttpStatusCode.Unauthorized, SendOutcome.Permanent)]
    [InlineData(HttpStatusCode.BadRequest, SendOutcome.Permanent)]
    public async Task SendMessage_ClassifiesHttpErrors(HttpStatusCode status, SendOutcome expected)
    {
        var (client, _) = Create(_ => Json(status, """{"ok":false,"description":"x"}"""));
        (await client.SendMessageAsync("c", "t", CancellationToken.None)).Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task SendMessage_ChatNotFound_IsUnreachable()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}"""));
        (await client.SendMessageAsync("c", "t", CancellationToken.None)).Outcome.Should().Be(SendOutcome.RecipientUnreachable);
    }

    [Fact]
    public async Task SendMessage_RateLimited_UsesRetryAfterHeader()
    {
        var (client, _) = Create(_ =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, """{"ok":false}""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            return response;
        });

        var result = await client.SendMessageAsync("c", "t", CancellationToken.None);

        result.Outcome.Should().Be(SendOutcome.Transient);
        result.ErrorCode.Should().Be("RATE_LIMITED");
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(17));
    }

    [Fact]
    public async Task SendMessage_OkFalseInBodyWith200_IsNotSuccess()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.OK, """{"ok":false,"description":"nope"}"""));
        (await client.SendMessageAsync("c", "t", CancellationToken.None)).Outcome.Should().NotBe(SendOutcome.Ok);
    }

    [Fact]
    public async Task SendMessage_NetworkError_IsTransient_AndDoesNotLeakTokenInErrorCode()
    {
        var (client, _) = Create(_ => throw new HttpRequestException($"connect failed https://x/bot{Token}/sendMessage"));

        var result = await client.SendMessageAsync("c", "t", CancellationToken.None);

        result.Outcome.Should().Be(SendOutcome.Transient);
        result.ErrorCode.Should().Be("NETWORK").And.NotContain("secret");
    }

    [Fact]
    public async Task NoToken_NothingIsSent()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, "{}"), token: null);

        client.IsConfigured.Should().BeFalse();
        (await client.SendMessageAsync("c", "t", CancellationToken.None)).Outcome.Should().Be(SendOutcome.Permanent);
        handler.Urls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUpdates_ParsesChatIdTextAndOffset_FromArrayOrSingleObject()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.OK,
            """{"ok":true,"result":[{"update_id":10,"message":{"chat":{"id":"c-1"},"text":"K7XM2QPD"}},{"update_id":11,"message":{"chat":{"id":987},"text":"HUY"}},{"update_id":12}]}"""));

        var result = await client.GetUpdatesAsync(null, 5, CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.Updates.Should().BeEquivalentTo([new ZaloUpdate(10, "c-1", "K7XM2QPD"), new ZaloUpdate(11, "987", "HUY")]);

        var (single, _) = Create(_ => Json(HttpStatusCode.OK, """{"ok":true,"result":{"update_id":3,"message":{"chat":{"id":"c-2"},"text":"hi"}}}"""));
        (await single.GetUpdatesAsync(null, 5, CancellationToken.None)).Updates.Should().ContainSingle().Which.ChatId.Should().Be("c-2");
    }

    [Fact]
    public async Task GetBotName_UsesConfigOrAsksGetMeOnce()
    {
        var calls = 0;
        var (client, _) = Create(_ =>
        {
            calls++;
            return Json(HttpStatusCode.OK, """{"ok":true,"result":{"display_name":"Bot Hoa Mai"}}""");
        });

        (await client.GetBotNameAsync(CancellationToken.None)).Should().Be("Bot Hoa Mai");
        (await client.GetBotNameAsync(CancellationToken.None)).Should().Be("Bot Hoa Mai");
        calls.Should().Be(1, "tên bot được nhớ lại");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("1234567890:abcDEF_ghi", true)]
    [InlineData("khong-co-dau-hai-cham", false)]
    [InlineData("abc:def", false)]
    [InlineData("123:có space", false)]
    public void Token_Format(string? token, bool valid) => ZaloBotOptions.IsValidToken(token).Should().Be(valid);

    // ---- ZaloLinkAttemptLimiter (NT-BR-14, L-19)

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Limiter_BlocksChatAfterFiveFailures_UntilWindowPasses()
    {
        var limiter = new ZaloLinkAttemptLimiter();
        for (var i = 0; i < ZaloLinkAttemptLimiter.PerChatLimit; i++)
        {
            limiter.IsBlocked("c", Now).Should().BeFalse();
            limiter.RecordFailure("c", Now);
        }

        limiter.IsBlocked("c", Now).Should().BeTrue();
        limiter.IsBlocked("other", Now).Should().BeFalse("chat khác không bị ảnh hưởng");
        limiter.IsBlocked("c", Now + ZaloLinkAttemptLimiter.Window).Should().BeFalse("hết giờ thì mở lại");
    }

    [Fact]
    public void Limiter_GlobalCeiling_BlocksEveryoneEvenWithFreshChats()
    {
        var limiter = new ZaloLinkAttemptLimiter();
        for (var i = 0; i < ZaloLinkAttemptLimiter.GlobalLimit; i++)
            limiter.RecordFailure($"chat-{i}", Now);

        limiter.IsBlocked("brand-new-chat", Now).Should().BeTrue("kẻ tấn công đổi chat liên tục cũng không dò được");
        limiter.IsBlocked("brand-new-chat", Now + ZaloLinkAttemptLimiter.Window).Should().BeFalse();
    }
}
