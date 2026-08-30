using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder.Converters;

/// <summary>
/// Handles deserialization of BundlePack lists from both JSON arrays ([]) and objects/dictionaries ({}) format.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CS-R1138:Inappropriate ordering of parameters", Justification = "Overridden from System.Text.Json.Serialization.JsonConverter<T>")]
public sealed class BundlePackListConverter : JsonConverter<List<BundlePack>>
{
    /// <inheritdoc/>
    // skipcq: CS-R1138
    public override List<BundlePack>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) // skipcq: CS-R1138
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => [],
            JsonTokenType.StartArray => ReadArray(ref reader, options),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            _ => throw new JsonException($"Unexpected token type {reader.TokenType} for BundlePack list")
        };
    }

    private static List<BundlePack> ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<BundlePack>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return list;
            }

            var item = JsonSerializer.Deserialize<BundlePack>(ref reader, options);
            if (item != null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static List<BundlePack> ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<BundlePack>();
        using var doc = JsonDocument.ParseValue(ref reader);

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var pack = JsonSerializer.Deserialize<BundlePack>(prop.Value.GetRawText(), options);
            if (pack != null)
            {
                if (string.IsNullOrEmpty(pack.Name))
                {
                    pack.Name = prop.Name;
                }

                list.Add(pack);
            }
        }

        return list;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, List<BundlePack> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
