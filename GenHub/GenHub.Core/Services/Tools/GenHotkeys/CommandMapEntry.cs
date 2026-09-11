using System;
using System.Collections.Generic;

namespace GenHub.Core.Services.Tools.GenHotkeys;

/// <summary>
/// Represents a single CommandMap entry.
/// </summary>
public class CommandMapEntry
{
    /// <summary>Gets or sets the command name (e.g. "SAVE_VIEW1").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets properties in this block.</summary>
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the primary key (e.g. "KEY_F1").</summary>
    public string? Key
    {
        get => Properties.TryGetValue("Key", out var val) ? val : null;
        set
        {
            if (value != null)
            {
                Properties["Key"] = value;
            }
            else
            {
                Properties.Remove("Key");
            }
        }
    }
}
