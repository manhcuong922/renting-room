using NetArchTest.Rules;

namespace renting_room.ArchitectureTests;

/// <summary>
/// Luật MỤC TIÊU — code hiện tại chưa đạt nên đang Skip (không làm đỏ build). Bỏ Skip khi làm xong nhóm refactor tương ứng.
/// </summary>
public sealed class TargetRuleTests
{
    /// <summary>Hiện 50 vi phạm: IAppDbContext + 45 kiểu dùng IDbContextTransaction, ChangeTracker, DatabaseFacade.</summary>
    [Fact(Skip = "Nhóm 3 — thu hẹp IAppDbContext, thêm IUnitOfWork")]
    public void Application_DoesNotUse_EfInfrastructureTypes() =>
        Layers.ShouldNotDependOnAny(
            dependency => Types.InAssembly(Layers.Application).ShouldNot().HaveDependencyOn(dependency),
            "transaction / khóa / xóa theo dõi đi qua abstraction riêng (IUnitOfWork), không lộ kiểu hạ tầng của EF",
            "Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade",
            "Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker",
            "Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction");
}
