using System.Text.RegularExpressions;

namespace GenHub.Core.Helpers;

/// <summary>
/// Scrubs IP endpoints and credential material from log lines, toasts, and
/// diagnostics output. Underlay endpoints must never appear in user-visible text.
/// </summary>
public static partial class OnlineLogScrubber
{
    /// <summary>
    /// Replacement token for scrubbed IP addresses.
    /// </summary>
    public const string RedactedIp = "[redacted-ip]";

    /// <summary>
    /// Replacement token for scrubbed credential material.
    /// </summary>
    public const string RedactedCredential = "[redacted]";

    /// <summary>
    /// Scrubs IPv4/IPv6 addresses and bearer-style tokens from the given text.
    /// </summary>
    /// <param name="text">The text to scrub.</param>
    /// <returns>The scrubbed text.</returns>
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var scrubbed = Ipv4Regex().Replace(text, RedactedIp);
        scrubbed = Ipv6Regex().Replace(scrubbed, RedactedIp);
        scrubbed = BearerRegex().Replace(scrubbed, $"$1{RedactedCredential}");
        return scrubbed;
    }

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b", RegexOptions.Compiled)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{0,4}:){2,}[0-9A-Fa-f:.]+\b", RegexOptions.Compiled)]
    private static partial Regex Ipv6Regex();

    [GeneratedRegex(@"(?i)(bearer\s+|grant\s*[:=]\s*|password\s*[:=]\s*)[^\s;,""]+", RegexOptions.Compiled)]
    private static partial Regex BearerRegex();
}
