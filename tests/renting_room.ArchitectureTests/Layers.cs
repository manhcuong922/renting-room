using System.Reflection;
using NetArchTest.Rules;

namespace renting_room.ArchitectureTests;

/// <summary>Assembly của từng tầng — lấy qua một kiểu "mốc" để đổi tên project không làm test lệch.</summary>
internal static class Layers
{
    public static readonly Assembly Domain = typeof(renting_room.Domain.Common.Entity).Assembly;
    public static readonly Assembly Application = typeof(renting_room.Application.DependencyInjection).Assembly;
    public static readonly Assembly Infrastructure = typeof(renting_room.Infrastructure.DependencyInjection).Assembly;
    public static readonly Assembly Api = typeof(Program).Assembly;

    public const string DomainNamespace = "renting_room.Domain";
    public const string ApplicationNamespace = "renting_room.Application";
    public const string InfrastructureNamespace = "renting_room.Infrastructure";
    public const string ControllersNamespace = "renting_room.Controllers";

    /// <summary>Kiểu do code dự án viết (bỏ kiểu compiler sinh như lambda/closure và kiểu source generator của Mediator sinh).</summary>
    public static IEnumerable<Type> ProjectTypes(this Assembly assembly) =>
        assembly.GetTypes().Where(t => t.Namespace?.StartsWith("renting_room.", StringComparison.Ordinal) == true && !t.Name.Contains('<'));

    /// <summary>
    /// Không kiểu nào phụ thuộc vào bất kỳ <paramref name="dependencies"/> nào (namespace hoặc tên kiểu đầy đủ).
    /// Kiểm từng dependency một rồi gộp: <c>HaveDependencyOnAny</c> của NetArchTest 1.3.2 bỏ sót khi truyền nhiều tên kiểu đầy đủ.
    /// </summary>
    public static void ShouldNotDependOnAny(Func<string, ConditionList> rule, string because, params string[] dependencies)
    {
        var violations = dependencies
            .SelectMany(dependency => (rule(dependency).GetResult().FailingTypeNames ?? []).Select(type => $"{type} → {dependency}"))
            .ToList();

        violations.Should().BeEmpty("{0} — {1} vi phạm:\n{2}", because, violations.Count, string.Join("\n", violations));
    }
}
