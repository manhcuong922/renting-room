using System.Reflection;
using FluentValidation;
using renting_room.Domain.Common;

namespace renting_room.ArchitectureTests;

/// <summary>Quy ước code đang được tuân thủ — giữ nguyên khi refactor / thêm use case mới.</summary>
public sealed class ConventionTests
{
    [Fact]
    public void MediatorHandlersAndBehaviors_AreSealed()
    {
        var handlers = Layers.Application.ProjectTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(IsMediatorHandler))
            .ToList();

        handlers.Should().NotBeEmpty("phải tìm thấy handler — nếu rỗng là cách dò đã sai");
        handlers.Where(t => !t.IsSealed).Select(t => t.FullName).Should().BeEmpty("handler / pipeline behavior không dùng để kế thừa");
    }

    [Fact]
    public void Validators_AreSealed()
    {
        var validators = Layers.Application.ProjectTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IValidator).IsAssignableFrom(t))
            .ToList();

        validators.Should().NotBeEmpty("phải tìm thấy validator — nếu rỗng là cách dò đã sai");
        validators.Where(t => !t.IsSealed).Select(t => t.FullName).Should().BeEmpty("validator không dùng để kế thừa");
    }

    [Fact]
    public void DomainEntities_HaveNoPublicSetters()
    {
        var entities = Layers.Domain.ProjectTypes().Where(t => t.IsClass && typeof(Entity).IsAssignableFrom(t)).ToList();
        var violations = entities
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        entities.Should().NotBeEmpty("phải tìm thấy entity — nếu rỗng là cách dò đã sai");
        violations.Should().BeEmpty("trạng thái entity chỉ đổi qua method nghiệp vụ để giữ bất biến");
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static bool IsMediatorHandler(Type contract) =>
        contract.IsGenericType
        && contract.Namespace == "Mediator"
        && (contract.Name.Contains("Handler", StringComparison.Ordinal) || contract.Name.StartsWith("IPipelineBehavior", StringComparison.Ordinal));
}
