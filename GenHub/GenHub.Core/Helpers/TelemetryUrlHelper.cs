namespace GenHub.Core.Helpers;

/// <summary>
/// Provides helpers for scrubbing URLs before they are attached to telemetry events.
/// </summary>
public static class TelemetryUrlHelper
{
    /// <summary>
    /// Reduces a URL to scheme, host, and path so embedded credentials, query strings,
    /// and fragments are never transmitted to analytics endpoints.
    /// </summary>
    /// <param name="url">The URL to scrub.</param>
    /// <returns>The URL without user info, query string, or fragment; or null when the value is not an absolute URI.</returns>
    public static string? StripSensitiveUrlParts(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var sanitizedUri = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
        }.Uri;

        return sanitizedUri.GetLeftPart(UriPartial.Path);
    }
}
