using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace renting_room.OpenApi;

/// <summary>
/// Với controller, Swashbuckle chỉ ghi <c>required</c> khi có attribute <c>[Required]</c>. Filter này ghi theo metadata MVC
/// (<see cref="Http.RequiredParameterMetadataProvider"/>) để tài liệu khớp đúng hành vi khi chạy.
/// </summary>
internal sealed class RequiredFromMetadataFilter : IParameterFilter, IRequestBodyFilter
{
    public void Apply(IOpenApiParameter parameter, ParameterFilterContext context)
    {
        if (parameter is OpenApiParameter concrete && context.ApiParameterDescription?.ModelMetadata is { } metadata
            && (metadata.IsBindingRequired || metadata.IsRequired))
            concrete.Required = true;
    }

    public void Apply(IOpenApiRequestBody requestBody, RequestBodyFilterContext context)
    {
        if (requestBody is OpenApiRequestBody concrete && context.BodyParameterDescription?.ModelMetadata?.IsRequired == true)
            concrete.Required = true;

        // Trường form (upload file): ghi vào danh sách required của schema form.
        var requiredFields = context.FormParameterDescriptions?
            .Where(p => p.ModelMetadata is { IsRequired: true } or { IsBindingRequired: true })
            .Select(p => p.Name)
            .ToList() ?? [];
        if (requiredFields.Count == 0 || requestBody.Content is null)
            return;
        foreach (var schema in requestBody.Content.Values.Select(c => c.Schema).OfType<OpenApiSchema>())
        {
            schema.Required ??= new HashSet<string>();
            foreach (var field in requiredFields)
                schema.Required.Add(field);
        }
    }
}
