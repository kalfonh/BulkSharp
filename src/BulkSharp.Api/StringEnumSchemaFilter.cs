using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BulkSharp.Api;

/// <summary>
/// Describes enums as strings in the OpenAPI document, matching how they are serialized.
/// </summary>
/// <remarks>
/// The BulkSharp JSON contract writes enums as their names via <c>JsonStringEnumConverter</c>,
/// but the schema generator does not inspect the minimal-API JSON options and would otherwise
/// emit <c>type: integer</c>. A generated client built from that document sends and expects
/// integers, and every enum-valued request or response fails at runtime.
/// </remarks>
internal sealed class StringEnumSchemaFilter : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var type = Nullable.GetUnderlyingType(context.Type) ?? context.Type;
        if (!type.IsEnum)
            return;

        schema.Type = "string";
        schema.Format = null;
        schema.Enum = Enum.GetNames(type)
            .Select(name => (IOpenApiAny)new OpenApiString(name))
            .ToList();
    }
}
