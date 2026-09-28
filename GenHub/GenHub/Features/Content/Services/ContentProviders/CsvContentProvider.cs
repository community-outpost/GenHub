using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// Content provider that orchestrates discovery→resolution→delivery pipeline
/// for base game installations from verified CSV registries.
/// </summary>
public class CsvContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    ILogger<CsvContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _discoverer = discoverers.OfType<CsvDiscoverer>().FirstOrDefault()
        ?? discoverers.FirstOrDefault(d => string.Equals(d.SourceName, CsvConstants.SourceName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("CSV discoverer not found");

    private readonly IContentResolver _resolver = ResolveResolver(resolvers, CsvConstants.ResolverId);

    private readonly IContentDeliverer _deliverer = ResolveDeliverer(deliverers, ContentSourceNames.HttpDeliverer);

    /// <inheritdoc />
    public override string SourceName => PublisherTypeConstants.CsvRegistry;

    /// <inheritdoc />
    public override string Description => CsvConstants.Description;

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _discoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _resolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _deliverer;

    /// <inheritdoc />
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default)
    {
        return await SearchManifestByIdAsync(contentId, requireExactIdMatch: true, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Preparing CSV catalog content for manifest {ManifestId}", manifest.Id);

        return await DeliverContentOnlyAsync(
            Deliverer,
            manifest,
            workingDirectory,
            progress,
            cancellationToken);
    }
}
