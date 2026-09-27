namespace GenHub.Core.Helpers;

/// <summary>
/// Provides helpers for scrubbing URLs before they are attached to telemetry events.
/// </summary>
public static class TelemetryUrlHelper
{
    /// <summary>
    /// Reduces a URL to scheme, host, and path so query strings and fragments that may
    /// carry credentials or signed tokens are never transmitted to analytics endpoints.
    /// </summary>
    /// <param name="url">The URL to scrub.</param>
    /// <returns>The URL without query string or fragment, or the original value when it is not an absolute URI.</returns>
    public static string? StripSensitiveUrlParts(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        return uri.GetLeftPart(UriPartial.Path);
    }
}
