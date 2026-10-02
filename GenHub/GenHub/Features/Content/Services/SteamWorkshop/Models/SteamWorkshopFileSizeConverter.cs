using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Reads workshop file sizes expressed either as JSON strings or numbers.
/// Unparseable or out-of-range values fall back to zero.
/// </summary>
public sealed class SteamWorkshopFileSizeConverter : JsonConverter<long>
{
    /// <inheritdoc />
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.String => long.TryParse(reader.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0,
            _ => 0,
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static long ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var integer))
        {
            return integer >= 0 ? integer : 0;
        }

        if (reader.TryGetDouble(out var floating) && double.IsFinite(floating) && floating >= 0)
        {
            return floating >= long.MaxValue ? long.MaxValue : (long)floating;
        }

        return 0;
    }
}
