using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using renting_room.Application.Common.Interfaces;
using renting_room.Domain.Identity;

namespace renting_room.Infrastructure.Persistence;

/// <summary>
/// Chỉ dùng cho lệnh <c>dotnet ef</c> (tạo migration, update DB). Connection string lấy từ biến môi trường
/// <c>ConnectionStrings__DefaultConnection</c>; mặc định trỏ tới PostgreSQL dev trong docker-compose.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string LocalDockerConnection =
        "Host=localhost;Port=5432;Database=renting_room;Username=postgres;Password=postgres";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? LocalDockerConnection;

        var options = new DbContextOptionsBuilder<AppDbContext>();
        DependencyInjection.ConfigureDbContext(options, connectionString);

        return new AppDbContext(options.Options, new DesignTimeUser(), TimeProvider.System);
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public UserRole? Role => null;
    }
}
