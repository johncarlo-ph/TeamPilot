using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TeamPilot.API.Swagger;

/// <summary>
/// Renders enums in Swagger as their string names (e.g. "Coding") rather than raw integers,
/// matching the <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> registered
/// for the API's JSON serialization.
/// </summary>
public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum || schema is not OpenApiSchema concreteSchema)
        {
            return;
        }

        concreteSchema.Enum = Enum.GetNames(context.Type)
            .Select(name => (JsonNode)JsonValue.Create(name))
            .ToList();
        concreteSchema.Type = JsonSchemaType.String;
        concreteSchema.Format = null;
    }
}
