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
                // Legacy compatibility: 0 was previously SymlinkOnly (now 1), Default was HardLink (now 0)
                // If we encounter raw 0 in JSON, it likely means old "SymlinkOnly" setting
                if (value == 0)
                {
                    return WorkspaceStrategy.SymlinkOnly;
                }

                if (Enum.IsDefined(typeof(WorkspaceStrategy), value))
                {
                    return (WorkspaceStrategy)value;
                }
            }
        }
        else if (reader.TokenType == JsonTokenType.String)
        {
            var valueStr = reader.GetString();
            if (Enum.TryParse<WorkspaceStrategy>(valueStr, true, out var result) && Enum.IsDefined(typeof(WorkspaceStrategy), result))
            {
                return result;
            }
        }

        // Fallback for unknown values or unexpected token types
        return WorkspaceStrategy.HardLink;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, WorkspaceStrategy value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
