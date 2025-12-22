using System.Text.Json;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Extension methods for working with Generals Online data.
/// </summary>
public static class GeneralsOnlineExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Tries to deserialize JSON to the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="json">The JSON string.</param>
    /// <param name="result">The deserialized result, or default if unsuccessful.</param>
    /// <returns>True if deserialization succeeded, false otherwise.</returns>
    public static bool TryDeserialize<T>(this string json, out T? result)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                result = default;
                return false;
            }

            result = JsonSerializer.Deserialize<T>(json, JsonOptions);
            return result != null;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    /// <summary>
    /// Safely deserializes JSON to the specified type, returning a default value on failure.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="json">The JSON string.</param>
    /// <param name="defaultValue">The default value to return on failure.</param>
    /// <returns>The deserialized object or the default value.</returns>
    public static T DeserializeOrDefault<T>(this string json, T defaultValue = default!) where T : class
    {
        return TryDeserialize<T>(json, out var result) ? result! : defaultValue;
    }

    /// <summary>
    /// Safely deserializes JSON to a list, returning an empty list on failure.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="json">The JSON string.</param>
    /// <returns>The deserialized list or an empty list.</returns>
    public static List<T> DeserializeList<T>(this string json)
    {
        return TryDeserialize<List<T>>(json, out var result) ? result! : new List<T>();
    }
}
