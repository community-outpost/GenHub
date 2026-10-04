using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenHub.Core.Serialization;

/// <summary>
/// Resilient JSON converter for <see cref="ContentType"/> that reads case-insensitive strings
/// and integers, gracefully falling back to <see cref="ContentType.UnknownContentType"/> when
/// an unrecognized or future content type is encountered instead of throwing.
/// </summary>
public class JsonContentTypeConverter : JsonConverter<ContentType>
{
    /// <inheritdoc />
    [SuppressMessage("Maintainability", "CS-R1138:Inappropriate ordering of parameters", Justification = "Signature is defined by System.Text.Json.Serialization.JsonConverter<T>.Read")]
    [SuppressMessage("DeepSource", "CS-R1138", Justification = "Signature is defined by System.Text.Json.Serialization.JsonConverter<T>.Read")]
    [SuppressMessage("csharp", "CS-R1138", Justification = "Signature is defined by System.Text.Json.Serialization.JsonConverter<T>.Read")]
    public override ContentType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) // skipcq: CS-R1138
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var valueStr = reader.GetString();
            if (CatalogManifestIdentity.TryParseDeclaredContentType(valueStr, out var declared))
            {
                return declared;
            }

            return ContentType.UnknownContentType;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out var value) && Enum.IsDefined(typeof(ContentType), value))
            {
                return (ContentType)value;
            }

            return ContentType.UnknownContentType;
        }

        return ContentType.UnknownContentType;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ContentType value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
