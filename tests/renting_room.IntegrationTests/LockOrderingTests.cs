using System.Net;
using Npgsql;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>
/// 04/11/2026 — các lệnh chạy song song (README C-07). Một kết nối DB riêng giữ khóa để giả lập lệnh kia đang chạy giữa chừng:
/// L-05 đổi cài đặt kỳ thu ↔ tạo phiếu, L-06 archive khu ↔ tạo HĐ nháp, L-18 khóa hàng không lọc tổ chức.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LockOrderingTests(ApiFactory factory)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(1);
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>Kết nối giữ khóa tới khi Commit / Rollback — như một lệnh khác đang chạy trong transaction.</summary>
    private sealed class Holder : IAsyncDisposable
    {
        private NpgsqlConnection _connection = null!;
        private NpgsqlTransaction _transaction = null!;

        public static async Task<Holder> BeginAsync(string connectionString)
        {
            var holder = new Holder { _connection = new NpgsqlConnection(connectionString) };
            await holder._connection.OpenAsync();
            holder._transaction = await holder._connection.BeginTransactionAsync();
            return holder;
        }

        public async Task RunAsync(string sql, Guid id)
        {
            await using var command = new NpgsqlCommand(sql, _connection, _transaction);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync();
        }

        public Task CommitAsync() => _transaction.CommitAsync();
        public Task RollbackAsync() => _transaction.RollbackAsync();

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private static async Task ShouldBeWaitingAsync(Task<HttpResponseMessage> request, string because) =>
        (await Task.WhenAny(request, Task.Delay(Wait))).Should().NotBeSameAs(request, because);

    /// <summary>
    /// L-05 (M07 §10, PR-BR-09): đổi cài đặt kỳ thu đang giữ khu (FOR UPDATE) ⇒ tạo phiếu phải chờ — nếu không, nháp theo lịch cũ
    /// lưu sau lúc lệnh đổi đã kiểm "không còn nháp" ⇒ lọt BILLING_SETTINGS_DRAFT_INVOICES.
    /// </summary>
    [Fact]
    public async Task GenerateInvoices_WaitsForBillingSettingsChange_OnSameProperty()
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var start = TestData.StartWithinOnePeriod(TestData.Today(factory), 5);
        var (propertyId, roomId, _, _) = await _client.CreateActiveContractAsync(token, start, anchorDay: start.Day);

        await using var holder = await Holder.BeginAsync(factory.ConnectionString);
        await holder.RunAsync("SELECT 1 FROM properties WHERE id = @id FOR UPDATE", propertyId);

        var generate = factory.CreateClient().PostJsonAsync("/api/v1/invoices/generate",
            new { propertyId, billingMonth = $"{start:yyyy-MM}", roomIds = new[] { roomId } }, token);
        await ShouldBeWaitingAsync(generate, "đổi cài đặt kỳ thu đang chạy ⇒ tạo phiếu chờ đọc lịch kỳ thu đã chốt");

        await holder.RollbackAsync();
        var generated = await generate;
        generated.StatusCode.Should().Be(HttpStatusCode.OK, await generated.Content.ReadAsStringAsync());
        (await generated.ReadAsync<System.Text.Json.JsonElement>()).GetProperty("created").GetInt32().Should().Be(1);
    }

    /// <summary>L-05: hóa đơn trả phòng cũng là nháp theo lịch kỳ thu ⇒ cùng chờ.</summary>
    [Fact]
    public async Task CreateFinalInvoice_WaitsForBillingSettingsChange_OnSameProperty()
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var today = TestData.Today(factory);
        var start = TestData.StartWithinOnePeriod(today, 5);
        var propertyId = await _client.CreatePropertyAsync(token, anchorDay: start.Day, chargeMode: "Postpaid");
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var contractId = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), start);
        (await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/activate", null, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var end = start > today ? start : today;
        var started = await _client.PostJsonAsync($"/api/v1/contracts/{contractId}/liquidation/start",
            new { actualEndDate = end, reason = "MutualAgreement" }, token);
        started.StatusCode.Should().Be(HttpStatusCode.NoContent, await started.Content.ReadAsStringAsync());

        await using var holder = await Holder.BeginAsync(factory.ConnectionString);
        await holder.RunAsync("SELECT 1 FROM properties WHERE id = @id FOR UPDATE", propertyId);

        var final = factory.CreateClient().PostJsonAsync($"/api/v1/contracts/{contractId}/final-invoice", new { finalReadings = (object?)null }, token);
        await ShouldBeWaitingAsync(final, "đổi cài đặt kỳ thu đang chạy ⇒ hóa đơn trả phòng chờ");

        await holder.RollbackAsync();
        var created = await final;
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// L-06 (M02 §10, PR-BR-05): tạo HĐ nháp đang giữ hàng phòng và sắp lưu nháp ⇒ archive khu phải chờ rồi mới kiểm HĐ ⇒ thấy nháp ⇒ 422.
    /// Trước đây kiểm HĐ trước, chỉ chờ lúc ghi phòng ⇒ 204, nháp nằm trên phòng đã archive.
    /// </summary>
    [Fact]
    public async Task ArchiveProperty_WaitsForDraftBeingCreated_ThenRefuses()
    {
        var token = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var propertyId = await _client.CreatePropertyAsync(token);
        var roomId = await _client.CreateRoomAsync(token, propertyId);
        var draft = await _client.CreateContractAsync(token, roomId, await _client.CreateRenterAsync(token), TestData.Today(factory));
        (await _client.PostJsonAsync($"/api/v1/contracts/{draft}/cancel", new { reason = "Giả lập" }, token)).IsSuccessStatusCode.Should().BeTrue();

        // Lệnh tạo nháp đang chạy: đã khóa phòng, nháp chưa commit (đưa HĐ đã hủy về Draft trong cùng transaction).
        await using var holder = await Holder.BeginAsync(factory.ConnectionString);
        await holder.RunAsync("SELECT 1 FROM rooms WHERE id = @id FOR UPDATE", roomId);
        await holder.RunAsync("UPDATE contracts SET status = 'Draft' WHERE id = @id", draft);

        var archive = factory.CreateClient().PostJsonAsync($"/api/v1/properties/{propertyId}/archive", null, token);
        await ShouldBeWaitingAsync(archive, "tạo nháp đang giữ phòng ⇒ archive khu chờ");

        await holder.CommitAsync();
        (await (await archive).ReadProblemCodeAsync()).Should().Be("PROPERTY_HAS_ACTIVE_CONTRACTS");
    }

    /// <summary>L-18 (README C-07): khóa hàng lọc theo tổ chức ⇒ tổ chức B biết UUID phòng của A cũng không phải chờ / không giữ được hàng của A.</summary>
    [Fact]
    public async Task OtherOrganization_RoomIdOfA_ReturnsNotFoundWithoutWaitingForLock()
    {
        var tokenA = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var roomOfA = await _client.CreateRoomAsync(tokenA, await _client.CreatePropertyAsync(tokenA));
        var tokenB = (await _client.CreateActiveOwnerAsync()).Tokens.AccessToken;
        var renterOfB = await _client.CreateRenterAsync(tokenB);

        await using var holder = await Holder.BeginAsync(factory.ConnectionString);
        await holder.RunAsync("SELECT 1 FROM rooms WHERE id = @id FOR UPDATE", roomOfA);

        var create = factory.CreateClient().PostContractAsync(tokenB, roomOfA, renterOfB, TestData.Today(factory));
        (await Task.WhenAny(create, Task.Delay(TimeSpan.FromSeconds(3)))).Should().BeSameAs(create, "không khóa hàng của tổ chức khác ⇒ không phải chờ");
        await holder.RollbackAsync();
        (await (await create).ReadProblemCodeAsync()).Should().Be("ROOM_NOT_FOUND");
    }
}
