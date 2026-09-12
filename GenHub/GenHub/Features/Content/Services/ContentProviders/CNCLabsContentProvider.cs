using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentProviders;

/// <summary>
/// CNC Labs content provider that orchestrates discovery→resolution→delivery pipeline
/// for CNC Labs-hosted content.
/// </summary>
public class CNCLabsContentProvider(
    IEnumerable<IContentDiscoverer> discoverers,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    ILogger<CNCLabsContentProvider> logger,
    IContentValidator contentValidator,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private readonly IContentDiscoverer _cncLabsDiscoverer = discoverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.CNCLabsDiscoverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("CNC Labs discoverer not found", nameof(discoverers));

    private readonly IContentResolver _cncLabsResolver = resolvers.FirstOrDefault(r => r.ResolverId?.Equals(ContentSourceNames.CNCLabsResolverId, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("CNC Labs resolver not found", nameof(resolvers));

    private readonly IContentDeliverer _httpDeliverer = deliverers.FirstOrDefault(d => d.SourceName?.Equals(ContentSourceNames.HttpDeliverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new ArgumentException("HTTP deliverer not found", nameof(deliverers));

    /// <inheritdoc />
    /// <remarks>
    /// Must match the ProviderName set by CNCLabsMapDiscoverer on search results.
    /// </remarks>
    public override string SourceName => CNCLabsConstants.SourceName;

    /// <inheritdoc />
    public override string Description => "Provides maps and content from CNC Labs";

    /// <inheritdoc />
    protected override IContentDiscoverer Discoverer => _cncLabsDiscoverer;

    /// <inheritdoc />
    protected override IContentResolver Resolver => _cncLabsResolver;

    /// <inheritdoc />
    protected override IContentDeliverer Deliverer => _httpDeliverer;

}
