using GenHub.Core.Models.Enums;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenHub.Core.Serialization;

/// <summary>
/// Resilient JSON converter for <see cref="GameType"/> that reads case-insensitive strings
/// and integers, gracefully falling back to <see cref="GameType.Unknown"/> when
/// an unrecognized or future game type is encountered instead of throwing.
/// </summary>
public class JsonGameTypeConverter : JsonConverter<GameType>
{
    /// <inheritdoc />
    [SuppressMessage("Maintainability", "CS-R1138:Inappropriate ordering of parameters", Justification = "Signature is defined by System.Text.Json.Serialization.JsonConverter<T>.Read")]
    [SuppressMessage("DeepSource", "CS-R1138", Justification = "Signature is defined by System.Text.Json.Serialization.JsonConverter<T>.Read")]
    [SuppressMessage("csharp", "CS-R1138", Justification = "Signature is defined by System.Text.Json.Serialization.JsonConverter<T>.Read")]
    public override GameType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) // skipcq: CS-R1138
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var valueStr = reader.GetString();
            if (!string.IsNullOrEmpty(valueStr) &&
                !int.TryParse(valueStr, out _) &&
                Enum.TryParse<GameType>(valueStr, ignoreCase: true, out var result) &&
                Enum.IsDefined(result))
            {
                return result;
            }

            return GameType.Unknown;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out var value) && Enum.IsDefined(typeof(GameType), value))
            {
                return (GameType)value;
            }

            return GameType.Unknown;
        }

        return GameType.Unknown;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, GameType value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
