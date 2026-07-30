using System.Text.Json;
using System.Text.Json.Serialization;

namespace BulkSharp.Core.Contracts;

/// <summary>
/// The single source of truth for how BulkSharp serializes HTTP responses.
/// </summary>
/// <remarks>
/// Hosts must apply <see cref="Configure"/> to their JSON options so that generated
/// API clients can rely on camelCase property names and string-valued enums.
/// Changing this contract is a breaking change for every consumer of the HTTP API.
/// </remarks>
public static class BulkSharpJsonSerialization
{
    /// <summary>
    /// Serializer options matching the BulkSharp HTTP contract: camelCase property
    /// names, string-valued enums, and null properties omitted.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>
    /// Applies the BulkSharp HTTP contract to an existing options instance.
    /// Safe to call more than once on the same instance.
    /// </summary>
    /// <param name="options">The options to mutate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        if (!options.Converters.Any(converter => converter is JsonStringEnumConverter))
            options.Converters.Add(new JsonStringEnumConverter());
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}
