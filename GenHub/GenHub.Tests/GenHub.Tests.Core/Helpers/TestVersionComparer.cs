using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Providers;
using GenHub.Core.Services.Providers.VersionSchemes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Builds a real <see cref="IContentVersionComparer"/> over the real schemes so tests
/// exercise the same ordering the application uses.
/// </summary>
public static class TestVersionComparer
{
    /// <summary>
    /// Creates a comparer backed by the given publisher-to-scheme assignments.
    /// </summary>
    /// <param name="publisherSchemes">Publisher type and scheme identifier pairs.</param>
    /// <returns>A comparer using the real version schemes.</returns>
    public static IContentVersionComparer Create(params (string PublisherType, string SchemeId)[] publisherSchemes)
    {
        var definitions = publisherSchemes
            .Select(pair => new ProviderDefinition
            {
                ProviderId = pair.PublisherType,
                PublisherType = pair.PublisherType,
                VersionScheme = pair.SchemeId,
            })
            .ToList();

        return new ContentVersionComparer(new StubProviderDefinitionLoader(definitions), CreateSchemeFactory());
    }

    /// <summary>
    /// Creates a comparer wired with the default provider-to-scheme assignments shipped in the provider definitions.
    /// </summary>
    /// <returns>A comparer using the real version schemes.</returns>
    public static IContentVersionComparer CreateDefault() => Create(
        (PublisherTypeConstants.GeneralsOnline, VersionSchemeConstants.MmddyyQfe),
        (CommunityOutpostConstants.PublisherType, VersionSchemeConstants.IsoDate),
        (PublisherTypeConstants.TheSuperHackers, VersionSchemeConstants.Numeric));

    /// <summary>
    /// Creates a factory containing every registered version scheme.
    /// </summary>
    /// <returns>The scheme factory.</returns>
    public static IVersionSchemeFactory CreateSchemeFactory() => new VersionSchemeFactory(
        [new NumericVersionScheme(), new IsoDateVersionScheme(), new MmddyyQfeVersionScheme()],
        NullLogger<VersionSchemeFactory>.Instance);

    private sealed class StubProviderDefinitionLoader(List<ProviderDefinition> definitions) : IProviderDefinitionLoader
    {
        public Task<OperationResult<IEnumerable<ProviderDefinition>>> LoadProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IEnumerable<ProviderDefinition>>.CreateSuccess(definitions));

        public ProviderDefinition? GetProvider(string providerId) =>
            definitions.FirstOrDefault(d => string.Equals(d.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<ProviderDefinition> GetAllProviders() => definitions;

        public IEnumerable<ProviderDefinition> GetProvidersByType(ProviderType providerType) =>
            definitions.Where(d => d.ProviderType == providerType);

        public Task<OperationResult<bool>> ReloadProvidersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<bool>.CreateSuccess(true));

        public OperationResult<bool> AddCustomProvider(ProviderDefinition definition)
        {
            definitions.Add(definition);
            return OperationResult<bool>.CreateSuccess(true);
        }

        public OperationResult<bool> RemoveCustomProvider(string providerId)
        {
            definitions.RemoveAll(d => string.Equals(d.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            return OperationResult<bool>.CreateSuccess(true);
        }
    }
}
