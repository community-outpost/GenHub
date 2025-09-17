
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Interfaces.Content;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.Manifest;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

public class CSVDiscoverer : IContentDiscoverer
{
     private readonly ILogger<CSVDiscoverer> _logger;
private readonly ManifestDiscoveryService _manifestDiscoveryService;
     private readonly IConfigurationProviderService _configurationProvider;



    public CSVDiscoverer(ILogger<CSVDiscoverer> logger, ManifestDiscoveryService manifestDiscoveryService, IConfigurationProviderService configurationProvider)

    {

        _configurationProvider = configurationProvider;
        _logger = logger;
        _manifestDiscoveryService = manifestDiscoveryService;
    }
    /// <inheritdoc />
    public string SourceName => "CSV Discoverer";

    /// <inheritdoc />
    public string Description => "Discovers content from CSV files.";
  public string ResolverId => "CSVResolver";
    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.SupportsManifestGeneration | ContentSourceCapabilities.DirectSearch | ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <summary>
    /// Discovers content from CSV files based on the search query.
    /// </summary>
    /// <param name="query">The search criteria.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="T:GenHub.Core.Models.Results.OperationResult"/> containing discovered content.</returns>
   public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
       ContentSearchQuery query,
       CancellationToken cancellationToken = default)
      {
      var results = new List<ContentSearchResult>();

        // Generals 1.08
        if (query.TargetGame == GameType.Generals )
       { 
          results.Add(new ContentSearchResult
        {
         Id = "generals-1.08",
         Name = "Command & Conquer Generals 1.08",
        ResolverId = ResolverId,
        ResolverMetadata = 
            {
            { "game", "Generals" },
            { "version", "1.08" },
            { "csvUrl", "https://raw.githubusercontent.com/TheSuperHackers/GeneralsGamePatch/refs/heads/main/Patch104pZH/Resources/FileHashRegistry/Generals-108-GeneralsZH-104.csv" }
            }
               });
            }

        // Zero Hour 1.04
    if (query.TargetGame == GameType.ZeroHour )
    {
    results.Add(new ContentSearchResult
    {
    Id = "zerohour-1.04",
    Name = "Command & Conquer Generals: Zero Hour 1.04",
    ResolverId = ResolverId,
    ResolverMetadata = 
    {
    { "game", "ZeroHour" },
    { "version", "1.04" },
    { "csvUrl", "https://raw.githubusercontent.com/TheSuperHackers/GeneralsGamePatch/refs/heads/main/Patch104pZH/Resources/FileHashRegistry/Generals-108-GeneralsZH-104.csv" }
    }
    });
    }

 return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
 }
}