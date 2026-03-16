using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Serialization;

/// <summary>
/// Custom JSON converter for WorkspaceStrategy that supports both string and integer formats.
/// Provides backward compatibility for integer-based strategy values.
/// </summary>
public class JsonWorkspaceStrategyConverter : JsonConverter<WorkspaceStrategy>
{
    /// <inheritdoc />
    public override WorkspaceStrategy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out var value))
            {
                if (Enum.IsDefined(typeof(WorkspaceStrategy), value))
                {
                    return (WorkspaceStrategy)value;
                }

                // Invalid numeric value - fallback
                return WorkspaceStrategy.HardLink;
            }
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            var valueStr = reader.GetString();
            if (!string.IsNullOrEmpty(valueStr) && Enum.TryParse<WorkspaceStrategy>(valueStr, true, out var result))
            {
                if (Enum.IsDefined(typeof(WorkspaceStrategy), result))
                {
                    return result;
                }

                // Invalid string value - fallback
                return WorkspaceStrategy.HardLink;
            }
        }

        // Fallback for unknown values or unexpected token types
        return WorkspaceStrategy.HardLink;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, WorkspaceStrategy value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((int)value);
    }
}
