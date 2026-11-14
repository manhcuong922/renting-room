using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using renting_room.Infrastructure.Persistence;

namespace renting_room.Infrastructure.UnitTests;

/// <summary>
/// L-12 (05/11/2026): lỗi PostgreSQL không được mang dữ liệu cá nhân vào log — "Failing row contains (…, Trần Thị Lan, 0901234567, …)" nằm ở
/// <c>Detail</c>, Npgsql chỉ điền khi chuỗi kết nối bật <c>Include Error Detail</c> ⇒ app luôn tắt, kể cả khi người vận hành bật nhầm.
/// </summary>
public sealed class DatabaseConnectionTests
{
    [Fact]
    public void AppDbContext_NeverIncludesPostgresErrorDetail_EvenIfConnectionStringEnablesIt()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=renting_room;Username=app;Password=secret;Include Error Detail=true"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        var options = services.BuildServiceProvider().GetRequiredService<DbContextOptions<AppDbContext>>();
        var connectionString = RelationalOptionsExtension.Extract(options).ConnectionString!;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.IncludeErrorDetail.Should().BeFalse();
        builder.Host.Should().Be("localhost");
        builder.Password.Should().Be("secret");
    }
}
