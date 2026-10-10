using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using renting_room.Infrastructure.Jobs;
using renting_room.IntegrationTests.Infrastructure;

namespace renting_room.IntegrationTests;

/// <summary>S2 (README C-10): job hằng ngày xóa nhật ký cũ hơn 5 năm, giữ nguyên nhật ký mới.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuditRetentionTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Purge_DeletesRowsOlderThanFiveYears_KeepsRecentOnes()
    {
        var owner = await _client.CreateActiveOwnerAsync();
        var organization = owner.Organization.OrganizationId;
        var now = factory.Clock.GetUtcNow();
        var old = Guid.NewGuid();
        var recent = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        foreach (var (id, occurredAt) in new[] { (old, now.AddYears(-5).AddDays(-1)), (recent, now.AddYears(-5).AddDays(1)) })
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO audit_logs (id, organization_id, action, entity_type, entity_id, occurred_at) VALUES (@id, @org, 'Updated', 'Room', @id, @at)",
                connection);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("org", organization);
            insert.Parameters.AddWithValue("at", occurredAt);
            await insert.ExecuteNonQueryAsync();
        }

        var deleted = await factory.Services.GetRequiredService<AuditRetentionService>().RunAsync(now, CancellationToken.None);

        deleted.Should().BeGreaterThanOrEqualTo(1);
        await using var count = new NpgsqlCommand("SELECT id FROM audit_logs WHERE id = ANY(@ids)", connection);
        count.Parameters.AddWithValue("ids", new[] { old, recent });
        var left = new List<Guid>();
        await using (var reader = await count.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                left.Add(reader.GetGuid(0));
        left.Should().Equal(recent);
    }
}
