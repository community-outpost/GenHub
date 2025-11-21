using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// JSON converter for <see cref="ManifestId"/>, enabling (de)serialization as a string.
/// </summary>
public sealed class ManifestIdJsonConverter : JsonConverter<ManifestId>
{
    /// <inheritdoc/>
    public override ManifestId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? string.Empty;
        return ManifestId.Create(s);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ManifestId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}