using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Content provider for GenLauncher community mods, patches, and addons.
/// </summary>
public class GenLauncherProvider : BaseContentProvider
{
    private readonly IProviderDefinitionLoader _providerDefinitionLoader;
    private readonly IContentDiscoverer _discoverer;
    private readonly IContentResolver _resolver;
    private readonly IContentDeliverer _deliverer;
    private readonly GenLauncherManifestFactory _manifestFactory;
    private ProviderDefinition? _cachedProviderDefinition;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenLauncherProvider"/> class.
    /// </summary>
    public GenLauncherProvider(
        IProviderDefinitionLoader providerDefinitionLoader,
        IEnumerable<IContentDiscoverer> discoverers,
        IEnumerable<IContentResolver> resolvers,
        IEnumerable<IContentDeliverer> deliverers,
        GenLauncherManifestFactory manifestFactory,
        IContentValidator contentValidator,
        IInstallationInstructionsService installationInstructionsService,
        ILogger<GenLauncherProvider> logger)
        : base(contentValidator, installationInstructionsService, logger)
    {
        _providerDefinitionLoader = providerDefinitionLoader;
        _manifestFactory = manifestFactory;

        _discoverer = discoverers.FirstOrDefault(d =>
            d.SourceName.Equals(PublisherTypeConstants.GenLauncher, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No GenLauncher discoverer found");

        _resolver = resolvers.FirstOrDefault(r =>
            r.ResolverId.Equals(GenLauncherConstants.PublisherId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No GenLauncher resolver found with ResolverId '{GenLauncherConstants.PublisherId}'");

        _deliverer = deliverers.FirstOrDefault(d =>
            d.SourceName.Equals(PublisherTypeConstants.GenLauncher, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No GenLauncher deliverer found");
    }

    /// <inheritdoc/>
    public override string SourceName => PublisherTypeConstants.GenLauncher;

    /// <inheritdoc/>
    public override string Description => GenLauncherConstants.ProviderDescription;

    /// <inheritdoc/>
    public override bool IsEnabled => true;

    /// <inheritdoc/>
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc/>
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return OperationResult<ContentManifest>.CreateFailure("Content ID cannot be null or empty");
        }

        Logger.LogInformation("Resolving validated manifest for GenLauncher content: {ContentId}", contentId);

        var discovery = await Discoverer.DiscoverAsync(
            new ContentSearchQuery { SearchTerm = contentId },
            cancellationToken).ConfigureAwait(false);

        var item = discovery.Data?.Items.FirstOrDefault(x =>
            string.Equals(x.Id, contentId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Name, contentId, StringComparison.OrdinalIgnoreCase));

        if (item == null)
        {
            return OperationResult<ContentManifest>.CreateFailure($"Content '{contentId}' was not found in GenLauncher catalog");
        }

        var resolved = await Resolver.ResolveAsync(item, cancellationToken).ConfigureAwait(false);
        if (!resolved.Success || resolved.Data == null)
        {
            return OperationResult<ContentManifest>.CreateFailure($"Failed to resolve manifest for '{contentId}': {resolved.FirstError}");
        }

        var validation = await ContentValidator.ValidateManifestAsync(resolved.Data, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return OperationResult<ContentManifest>.CreateFailure(validation.Issues.Select(i => i.Message));
        }

        return resolved;
    }

    /// <inheritdoc/>
    protected override IContentDiscoverer Discoverer => _discoverer;

    /// <inheritdoc/>
    protected override IContentResolver Resolver => _resolver;

    /// <inheritdoc/>
    protected override IContentDeliverer Deliverer => _deliverer;

    /// <inheritdoc/>
    protected override ProviderDefinition? GetProviderDefinition()
    {
        if (_cachedProviderDefinition != null)
        {
            return _cachedProviderDefinition;
        }

        _cachedProviderDefinition = _providerDefinitionLoader.GetProvider(GenLauncherConstants.PublisherId);
        return _cachedProviderDefinition;
    }

    /// <inheritdoc/>
    protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing GenLauncher content: {Version}", manifest.Version);
        return DeliverAndEnrichContentAsync(
            Deliverer,
            _manifestFactory,
            manifest,
            workingDirectory,
            progress,
            cancellationToken);
    }
}
