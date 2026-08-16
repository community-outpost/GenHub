using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder.Converters;

/// <summary>
/// Handles deserialization of BundlePack lists from both JSON arrays ([]) and objects/dictionaries ({}) format.
/// </summary>
public class BundlePackListConverter : JsonConverter<List<BundlePack>>
{
    /// <inheritdoc/>
    public override List<BundlePack>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new List<BundlePack>();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
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

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var list = new List<BundlePack>();
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
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
            }

            return list;
        }

        throw new JsonException($"Unexpected token type {reader.TokenType} for BundlePack list");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, List<BundlePack> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
