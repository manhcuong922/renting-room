using NetArchTest.Rules;

namespace renting_room.ArchitectureTests;

/// <summary>
/// Hướng phụ thuộc giữa các project đã do csproj chặn (Domain không tham chiếu project nào, Application chỉ tham chiếu Domain…).
/// Ở đây chỉ kiểm phần csproj không chặn được: thư viện framework lọt vào Domain, và namespace trong cùng một project.
/// </summary>
public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_DoesNotDependOn_OtherLayersOrFrameworks() =>
        Layers.ShouldNotDependOnAny(
            dependency => Types.InAssembly(Layers.Domain).ShouldNot().HaveDependencyOn(dependency),
            "Domain là lõi nghiệp vụ thuần C# — không biết tới DB, web, DI hay thư viện validate",
            Layers.ApplicationNamespace, Layers.InfrastructureNamespace, Layers.ControllersNamespace,
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "FluentValidation", "Mediator");

    [Fact]
    public void Controllers_DoNotTouch_PersistenceDirectly()
    {
        Types.InAssembly(Layers.Api).That().ResideInNamespace(Layers.ControllersNamespace).GetTypes()
            .Should().NotBeEmpty("phải tìm thấy controller — nếu rỗng là cách dò đã sai");

        Layers.ShouldNotDependOnAny(
            dependency => Types.InAssembly(Layers.Api).That().ResideInNamespace(Layers.ControllersNamespace).ShouldNot().HaveDependencyOn(dependency),
            "controller chỉ map HTTP ↔ command/query rồi gửi qua Mediator; truy cập dữ liệu thuộc về handler",
            Layers.InfrastructureNamespace, "Microsoft.EntityFrameworkCore");
    }
}
