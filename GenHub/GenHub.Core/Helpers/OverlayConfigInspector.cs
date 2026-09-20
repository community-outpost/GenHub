using System;
using System.Text;
using System.Text.Json;

namespace GenHub.Core.Helpers;

/// <summary>
/// Inspects opaque overlay adapter configuration payloads without executing them.
/// </summary>
public static class OverlayConfigInspector
{
    /// <summary>
    /// Reads the overlay name from an adapter configuration payload.
    /// </summary>
    /// <param name="adapterConfig">The opaque adapter configuration.</param>
    /// <returns>The overlay name, or null when unreadable.</returns>
    public static string? TryGetOverlayName(string adapterConfig)
    {
        if (string.IsNullOrWhiteSpace(adapterConfig))
        {
            return null;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(adapterConfig));
            using var document = JsonDocument.Parse(decoded);
            if (document.RootElement.TryGetProperty("overlay", out var overlay) &&
                overlay.ValueKind == JsonValueKind.String)
            {
                return overlay.GetString();
            }

            return null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}
