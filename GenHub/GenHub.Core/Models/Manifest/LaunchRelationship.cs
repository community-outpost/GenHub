using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Declares the process a manifest's entry point spawns and hands the session to.
/// <para>
/// Bootstrapper-style entries (anti-cheat launchers, DRM stubs, custom launchers) exit
/// or idle while the real game runs beside them. Without this declaration the launch
/// pipeline can only guess the handoff from the entry's file name, which breaks every
/// time a publisher renames either side. Publisher factories declare this from the
/// extracted package bytes; Publisher Studio authors it directly.
/// </para>
/// </summary>
public class LaunchRelationship
{
    private static readonly char[] PortableInvalidFileNameChars =
    [
        '\"', '<', '>', '|', ':', '*', '?', '\\', '/', '\0',
    ];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// Gets or sets the expected child process name, without extension or path.
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how long to wait for the child process in milliseconds.
    /// Null selects the pipeline default.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DiscoveryTimeoutMs { get; set; }

    /// <summary>
    /// Determines whether a declared child process name is usable for adoption.
    /// </summary>
    /// <param name="processName">The declared name.</param>
    /// <returns><c>true</c> when the name is a bare process name.</returns>
    /// <remarks>
    /// A relationship names a process, not a file: separators, parent traversal, and
    /// executable extensions are rejected rather than normalized, so a bad declaration
    /// fails loudly at the producer instead of silently adopting nothing.
    /// </remarks>
    public static bool IsValidProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        if (processName != processName.Trim())
        {
            return false;
        }

        if (processName.Any(char.IsControl))
        {
            return false;
        }

        var dotIndex = processName.IndexOf('.');
        var stem = dotIndex >= 0 ? processName[..dotIndex] : processName;
        if (ReservedDeviceNames.Contains(stem.TrimEnd())
            || processName.IndexOfAny(PortableInvalidFileNameChars) >= 0)
        {
            return false;
        }

        if (processName == "." || processName.EndsWith('.'))
        {
            return false;
        }

        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            processName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            processName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
            processName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            processName.EndsWith(".com", StringComparison.OrdinalIgnoreCase) ||
            processName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
