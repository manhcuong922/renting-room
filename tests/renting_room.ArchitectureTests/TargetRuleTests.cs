using NetArchTest.Rules;

namespace renting_room.ArchitectureTests;

/// <summary>
/// Application chỉ dùng repository / query / IUnitOfWork của chính nó — EF Core là chi tiết của Infrastructure (Application.csproj không còn
/// tham chiếu EF).
/// </summary>
public sealed class TargetRuleTests
{
    [Fact]
    public void Application_DoesNotUse_EfCore() =>
        Layers.ShouldNotDependOnAny(
            dependency => Types.InAssembly(Layers.Application).ShouldNot().HaveDependencyOn(dependency),
            "Application chỉ biết interface của chính nó; EF Core là chi tiết của Infrastructure",
            "Microsoft.EntityFrameworkCore");
}
