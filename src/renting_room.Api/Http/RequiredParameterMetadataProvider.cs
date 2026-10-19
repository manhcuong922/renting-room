using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace renting_room.Http;

/// <summary>
/// Tham số action không nullable là bắt buộc — giống Minimal API: thiếu ⇒ 400 INVALID_REQUEST, OpenAPI ghi <c>required</c>
/// (<see cref="OpenApi.RequiredFromMetadataFilter"/>). Chỉ áp cho tham số của action; thuộc tính bên trong request record
/// không bị ảnh hưởng (FluentValidation kiểm). Bỏ qua bộ lọc gom thành record <c>[FromQuery]</c> (mọi trường đều tùy chọn).
/// </summary>
internal sealed class RequiredParameterMetadataProvider : IBindingMetadataProvider, IValidationMetadataProvider
{
    public void CreateBindingMetadata(BindingMetadataProviderContext context)
    {
        if (IsRequired(context.Key, context.Attributes))
            context.BindingMetadata.IsBindingRequired = true;
    }

    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        if (IsRequired(context.Key, context.Attributes))
            context.ValidationMetadata.IsRequired = true;
    }

    private static bool IsRequired(ModelMetadataIdentity key, IReadOnlyList<object> attributes)
    {
        if (key.MetadataKind != ModelMetadataKind.Parameter || key.ParameterInfo is not { } parameter
            || parameter.ParameterType == typeof(CancellationToken) || attributes.OfType<FromQueryAttribute>().Any())
            return false;

        return parameter.ParameterType.IsValueType
            ? Nullable.GetUnderlyingType(parameter.ParameterType) is null
            : new NullabilityInfoContext().Create(parameter).WriteState == NullabilityState.NotNull;
    }
}
