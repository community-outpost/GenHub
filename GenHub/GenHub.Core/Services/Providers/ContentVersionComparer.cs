using GenHub.Core.Interfaces.Providers;

namespace GenHub.Core.Services.Providers;

/// <summary>
/// Compares versions using the scheme named by the publisher's provider definition.
/// </summary>
/// <param name="providerLoader">Supplies provider definitions.</param>
/// <param name="schemeFactory">Resolves schemes by identifier.</param>
public class ContentVersionComparer(
    IProviderDefinitionLoader providerLoader,
    IVersionSchemeFactory schemeFactory) : IContentVersionComparer
{
    /// <inheritdoc/>
    public int Compare(string? version1, string? version2, string? publisherType) =>
        GetScheme(publisherType).Compare(version1, version2);

    /// <inheritdoc/>
    public bool IsNewer(string? candidate, string? baseline, string? publisherType) =>
        Compare(candidate, baseline, publisherType) > 0;

    /// <inheritdoc/>
    public IVersionScheme GetScheme(string? publisherType) =>
        schemeFactory.GetScheme(FindSchemeId(publisherType));

    private string? FindSchemeId(string? publisherType)
    {
        if (string.IsNullOrWhiteSpace(publisherType))
        {
            return null;
        }

        // Provider definitions are keyed by providerId, which does not always match
        // the publisherType carried on manifests (e.g. "community-outpost" vs "communityoutpost").
        var definition = providerLoader.GetProvider(publisherType)
            ?? providerLoader.GetAllProviders().FirstOrDefault(provider =>
                string.Equals(provider.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase));

        return definition?.VersionScheme;
    }
}
