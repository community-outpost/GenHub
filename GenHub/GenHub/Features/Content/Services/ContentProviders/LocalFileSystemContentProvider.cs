using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// Local file system provider that uses FileSystemDiscoverer for content discovery.
/// This eliminates duplication with ManifestDiscoveryService.
/// </summary>
public class LocalFileSystemContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    ILogger<LocalFileSystemContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService,
    IConfigurationProviderService configurationProvider)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _fileSystemDiscoverer = discoverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.FileSystemDiscoverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new InvalidOperationException("No FileSystem discoverer found");

    private readonly IContentResolver _localResolver = resolvers.FirstOrDefault(r => r.ResolverId?.Equals(ContentSourceNames.LocalResolverId, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new InvalidOperationException("No Local resolver found");

    private readonly IContentDeliverer _fileSystemDeliverer = deliverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.FileSystemDeliverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new InvalidOperationException("No FileSystem deliverer found");

    private readonly IConfigurationProviderService _configurationProvider = configurationProvider;

    /// <inheritdoc />
    public override string SourceName => "LocalFileSystem";

    /// <inheritdoc />
    public override string Description => "Local file system content provider";

    /// <inheritdoc />
    public override bool IsEnabled => true;

    /// <inheritdoc />
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.LocalFileDelivery |
        ContentSourceCapabilities.SupportsPackageAcquisition | ContentSourceCapabilities.SupportsManifestGeneration;

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _fileSystemDiscoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _localResolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _fileSystemDeliverer;

}
