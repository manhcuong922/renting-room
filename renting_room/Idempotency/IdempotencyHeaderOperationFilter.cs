using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace renting_room.Idempotency;

/// <summary>Hiển thị ô nhập header Idempotency-Key trên Swagger cho endpoint có áp dụng.</summary>
internal sealed class IdempotencyHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IdempotentMetadata>().FirstOrDefault();
        if (metadata is null)
            return;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = IdempotencyEndpointExtensions.HeaderName,
            In = ParameterLocation.Header,
            Required = metadata.IsRequired,
            Description = "Khóa duy nhất cho thao tác (VD UUID). Gửi lại cùng khóa ⇒ nhận lại kết quả cũ, không thực thi lần 2.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 8, MaxLength = 64 }
        });
    }
}
