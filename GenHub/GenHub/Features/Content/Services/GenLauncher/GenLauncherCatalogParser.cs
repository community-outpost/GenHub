using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GenLauncher;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Initializes a new instance of the <see cref="GenLauncherCatalogParser"/> class.
/// Parses GenLauncher YAML catalogs and modification manifests into ContentSearchResult objects.
/// </summary>
/// <param name="logger">The logger instance.</param>
public partial class GenLauncherCatalogParser(ILogger<GenLauncherCatalogParser> logger) : ICatalogParser
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <inheritdoc/>
    public string CatalogFormat => GenLauncherConstants.CatalogFormat;

    /// <summary>
    /// Maps a GenLauncher modification type to GenHub ContentType.
    /// </summary>
    /// <param name="type">The GenLauncher modification type.</param>
    /// <returns>The corresponding GenHub content type.</returns>
    public static ContentType MapContentType(GenLauncherModificationType type)
    {
        return type switch
        {
            GenLauncherModificationType.Mod => ContentType.Mod,
            GenLauncherModificationType.Addon => ContentType.Addon,
            GenLauncherModificationType.Patch => ContentType.Patch,
            GenLauncherModificationType.Executable => ContentType.GameClient,
            GenLauncherModificationType.Advertising => ContentType.UnknownContentType,
            _ => ContentType.Mod,
        };
    }

    /// <summary>
    /// Converts a display name into a URL-friendly slug.
    /// </summary>
    /// <param name="name">The display name to slugify.</param>
    /// <returns>A URL-friendly slug representation.</returns>
    public static string Slugify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var cleaned = CleanSlugRegex().Replace(name.Trim().ToLowerInvariant(), "-");
        return CollapseDashesRegex().Replace(cleaned, "-").Trim('-');
    }

    /// <inheritdoc/>
    public Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        ProviderDefinition provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(catalogContent))
        {
            return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([]));
        }

        try
        {
            var targetGame = provider.TargetGame ?? GameType.ZeroHour;
            var gameStr = targetGame.ToString().ToLowerInvariant();

            if (RootCatalogRegex().IsMatch(catalogContent))
            {
                var rootResults = ParseRootCatalogResults(catalogContent, targetGame, gameStr);
                return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(rootResults));
            }

            var singleResults = ParseSingleVersionResults(catalogContent, targetGame, gameStr);
            return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(singleResults));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse GenLauncher catalog content");
            return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                $"Failed to parse GenLauncher catalog: {ex.Message}"));
        }
    }

    /// <summary>
    /// Parses a GenLauncher root catalog YAML string.
    /// </summary>
    /// <param name="yamlContent">The raw YAML catalog content.</param>
    /// <returns>The deserialized root manifest.</returns>
    public GenLauncherRootManifest ParseRootCatalog(string yamlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);
        return _deserializer.Deserialize<GenLauncherRootManifest>(yamlContent);
    }

    /// <summary>
    /// Parses a GenLauncher version/mod manifest YAML string.
    /// </summary>
    /// <param name="yamlContent">The raw YAML version manifest content.</param>
    /// <returns>The deserialized version manifest.</returns>
    public GenLauncherVersionManifest ParseVersionManifest(string yamlContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yamlContent);
        return _deserializer.Deserialize<GenLauncherVersionManifest>(yamlContent);
    }

    [GeneratedRegex(@"^[Mm]od[Dd]atas\s*:", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RootCatalogRegex();

    [GeneratedRegex(@"[^a-z0-9\-_]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CleanSlugRegex();

    [GeneratedRegex(@"-+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CollapseDashesRegex();

    private IEnumerable<ContentSearchResult> ParseRootCatalogResults(
        string catalogContent,
        GameType targetGame,
        string gameStr)
    {
        var root = ParseRootCatalog(catalogContent);
        var results = new List<ContentSearchResult>();

        foreach (var mod in root.ModDatas)
        {
            if (string.IsNullOrWhiteSpace(mod.ModName))
            {
                continue;
            }

            var modSlug = Slugify(mod.ModName);
            var searchResult = new ContentSearchResult
            {
                Id = $"genlauncher-{gameStr}-{modSlug}",
                Name = mod.ModName,
                ContentType = ContentType.Mod,
                TargetGame = targetGame,
                ProviderName = PublisherTypeConstants.GenLauncher,
                ResolverId = GenLauncherConstants.PublisherId,
                SourceUrl = mod.ModLink,
                RequiresResolution = true,
                VariantGroupId = modSlug,
                VariantFamilyName = mod.ModName,
            };

            searchResult.Tags.Add("genlauncher");
            searchResult.Tags.Add("mod");
            searchResult.Tags.Add(gameStr);

            searchResult.ResolverMetadata["modLink"] = mod.ModLink;
            if (!string.IsNullOrWhiteSpace(mod.ModLink))
            {
                searchResult.ResolverMetadata["yamlUrl"] = mod.ModLink;
            }

            searchResult.ResolverMetadata["patchesCount"] = (mod.ModPatches?.Count ?? 0).ToString();
            searchResult.ResolverMetadata["addonsCount"] = (mod.ModAddons?.Count ?? 0).ToString();

            results.Add(searchResult);
        }

        return results;
    }

    private IEnumerable<ContentSearchResult> ParseSingleVersionResults(
        string catalogContent,
        GameType targetGame,
        string gameStr)
    {
        var versionManifest = ParseVersionManifest(catalogContent);
        if (string.IsNullOrWhiteSpace(versionManifest.Name) || versionManifest.GetParsedType() == GenLauncherModificationType.Advertising)
        {
            return [];
        }

        var singleSlug = Slugify(versionManifest.Name);
        var contentType = MapContentType(versionManifest.GetParsedType());

        var singleResult = new ContentSearchResult
        {
            Id = $"genlauncher-{gameStr}-{singleSlug}",
            Name = versionManifest.Name,
            Version = versionManifest.Version,
            ContentType = contentType,
            TargetGame = targetGame,
            ProviderName = PublisherTypeConstants.GenLauncher,
            ResolverId = GenLauncherConstants.PublisherId,
            SourceUrl = versionManifest.SimpleDownloadLink ?? string.Empty,
            IconUrl = versionManifest.UIImageSourceLink,
            RequiresResolution = true,
            VariantGroupId = !string.IsNullOrEmpty(versionManifest.DependenceName) ? Slugify(versionManifest.DependenceName) : singleSlug,
            VariantFamilyName = !string.IsNullOrEmpty(versionManifest.DependenceName) ? versionManifest.DependenceName : versionManifest.Name,
        };

        singleResult.Tags.Add("genlauncher");
        singleResult.Tags.Add(contentType.ToString().ToLowerInvariant());
        if (versionManifest.Deprecated)
        {
            singleResult.Tags.Add("deprecated");
        }

        singleResult.SetData(versionManifest);
        return [singleResult];
    }
}
