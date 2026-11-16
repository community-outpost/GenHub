using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Security;

namespace GenHub.Core.Helpers;

/// <summary>
/// Decides whether a subscription candidate comes from a different source than an existing subscription.
/// </summary>
public static class PublisherSourceComparer
{
    /// <summary>
    /// Returns true when the candidate's source differs from the existing subscription's source.
    /// A definition candidate compares definition URLs, or catalog URLs when the existing subscription
    /// has no definition. A catalog-direct candidate compares catalog URLs.
    /// </summary>
    /// <param name="existing">The existing subscription.</param>
    /// <param name="candidate">The subscription candidate.</param>
    /// <returns>True when the sources differ.</returns>
    public static bool IsDifferentSource(PublisherSubscription existing, PublisherSourceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!string.IsNullOrWhiteSpace(candidate.DefinitionUrl) && !string.IsNullOrWhiteSpace(existing.DefinitionUrl))
        {
            return !UrlsMatch(existing.DefinitionUrl, candidate.DefinitionUrl);
        }

        return !UrlsMatch(existing.CatalogUrl, candidate.CatalogUrl);
    }

    /// <summary>
    /// Compares two source URLs after cloud direct-download normalisation. Scheme and host compare
    /// case-insensitively; path and query compare exactly.
    /// </summary>
    /// <param name="first">The first URL.</param>
    /// <param name="second">The second URL.</param>
    /// <returns>True when both URLs are present and identify the same source.</returns>
    public static bool UrlsMatch(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        return string.Equals(Normalize(first), Normalize(second), StringComparison.Ordinal);
    }

    private static string Normalize(string url)
    {
        var normalized = CloudUrlHelper.NormalizeDirectDownloadUrl(url);
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : normalized;
    }
}
