namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// An ordered map property dictionary preserving file order and duplicate keys.
/// </summary>
public sealed class MapDict
{
    private readonly List<MapDictValue> values = [];

    /// <summary>Gets the entries in file order.</summary>
    public IReadOnlyList<MapDictValue> Values => values;

    /// <summary>Adds an entry preserving file order.</summary>
    /// <param name="value">The entry to add.</param>
    public void Add(MapDictValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        values.Add(value);
    }

    /// <summary>Removes all entries.</summary>
    public void Clear()
    {
        values.Clear();
    }

    /// <summary>Finds the first entry with the given key (case-insensitive).</summary>
    /// <param name="key">The key to find.</param>
    /// <returns>The first matching entry, or null.</returns>
    public MapDictValue? Find(string key)
    {
        return values.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads an ASCII string entry.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="defaultValue">Value when the key is absent.</param>
    /// <returns>The stored string or the default.</returns>
    public string GetString(string key, string defaultValue = "")
    {
        return Find(key)?.StringValue ?? defaultValue;
    }

    /// <summary>Reads an integer entry.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="defaultValue">Value when the key is absent.</param>
    /// <returns>The stored integer or the default.</returns>
    public int GetInt(string key, int defaultValue = 0)
    {
        return Find(key)?.IntValue ?? defaultValue;
    }

    /// <summary>Reads a boolean entry.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="defaultValue">Value when the key is absent.</param>
    /// <returns>The stored boolean or the default.</returns>
    public bool GetBool(string key, bool defaultValue = false)
    {
        var found = Find(key);
        return found == null ? defaultValue : found.AsBool;
    }

    /// <summary>Reads a float entry.</summary>
    /// <param name="key">The key to read.</param>
    /// <param name="defaultValue">Value when the key is absent.</param>
    /// <returns>The stored float or the default.</returns>
    public float GetReal(string key, float defaultValue = 0f)
    {
        return Find(key)?.RealValue ?? defaultValue;
    }

    /// <summary>Replaces the first entry with the key, or appends when absent.</summary>
    /// <param name="value">The entry to store.</param>
    public void Set(MapDictValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var index = values.FindIndex(v => string.Equals(v.Key, value.Key, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            values.Add(value);
        }
        else
        {
            values[index] = value;
        }
    }
}
