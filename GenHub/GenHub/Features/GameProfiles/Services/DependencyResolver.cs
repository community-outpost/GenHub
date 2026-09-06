using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for resolving content dependencies.
/// </summary>
public class DependencyResolver(
    IContentManifestPool manifestPool,
    ILogger<DependencyResolver> logger) : IDependencyResolver
{
    private const string GameDataName = "gamedata";
    private const string ZeroHourName = "zerohour";
    private const string GameClientType = "gameclient";

    /// <summary>
    /// Matches a declared catalog ID to an acquired manifest ID allowing version and variant differences.
    /// </summary>
    /// <param name="declaredId">The declared catalog ID.</param>
    /// <param name="acquiredId">The acquired manifest ID.</param>
    /// <returns><see langword="true"/> if identities are compatible; otherwise, <see langword="false"/>.</returns>
    public static bool HasCompatibleCatalogIdentity(string? declaredId, string? acquiredId)
    {
        if (string.IsNullOrWhiteSpace(declaredId) || string.IsNullOrWhiteSpace(acquiredId))
        {
            return false;
        }

        if (string.Equals(declaredId, acquiredId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var declaredParts = declaredId.Split('.');
        var acquiredParts = acquiredId.Split('.');

        return HasCompatibleCatalogIdentity(declaredParts, acquiredParts);
    }

    /// <summary>
    /// Matches a declared 5-segment catalog ID (<c>schemaVersion.userVersion.publisher.contentType.contentName</c>)
    /// to an acquired manifest ID. Requires <c>schemaVersion</c> (segment 0), <c>publisher</c> (segment 2, or wildcard <c>any</c>),
    /// and <c>contentType</c> (segment 3) to match, while allowing <c>userVersion</c> (segment 1) and trailing variant labels
    /// (e.g. <c>-720p</c> on <c>contentName</c> segment 4) to differ.
    /// </summary>
    /// <param name="declaredParts">The 5 segments of the declared catalog ID.</param>
    /// <param name="acquiredParts">The 5 segments of the acquired manifest ID.</param>
    /// <returns><see langword="true"/> if identities are compatible; otherwise, <see langword="false"/>.</returns>
    public static bool HasCompatibleCatalogIdentity(string[] declaredParts, string[] acquiredParts)
    {
        if (declaredParts.Length != ManifestConstants.MinManifestSegments || acquiredParts.Length != ManifestConstants.MinManifestSegments)
        {
            return false;
        }

        if (!declaredParts[0].Equals(acquiredParts[0], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsPublisherCompatible(declaredParts[2], acquiredParts[2]))
        {
            return false;
        }

        var declaredType = declaredParts[3];
        var acquiredType = acquiredParts[3];
        if (!IsContentTypeCompatible(declaredType, acquiredType))
        {
            return false;
        }

        return IsContentNameCompatible(declaredParts[4], acquiredParts[4], declaredType, acquiredType);
    }

    /// <inheritdoc/>
    public async Task<HashSet<string>> ResolveDependenciesAsync(IEnumerable<string> contentIds, CancellationToken cancellationToken = default)
    {
        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toProcess = new Queue<string>(contentIds);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingContentIds = new List<string>();

        while (toProcess.Count > 0)
        {
            var contentId = toProcess.Dequeue();
            if (!visited.Add(contentId))
                continue;

            var manifest = await FindManifestInPoolAsync(contentId, cancellationToken);
            if (manifest != null)
            {
                resolvedIds.Add(manifest.Id.Value);

                if (manifest.Dependencies != null)
                {
                    var relevantDeps = manifest.Dependencies.Where(d => d.InstallBehavior == DependencyInstallBehavior.RequireExisting || d.InstallBehavior == DependencyInstallBehavior.AutoInstall);
                    foreach (var dep in relevantDeps)
                    {
                        // Skip default/placeholder IDs - these are generic type-based constraints validated separately
                        if (dep.Id.ToString() == ManifestConstants.DefaultContentDependencyId)
                        {
                            logger.LogDebug("Skipping generic dependency {DependencyName} (type-based constraint, not specific manifest)", dep.Name);
                            continue;
                        }

                        // Skip type-based dependencies (StrictPublisher = false means any matching type will satisfy)
                        // These use semantic IDs like "1.104.any.gameinstallation.zerohour" and are validated separately
                        if (!dep.StrictPublisher)
                        {
                            logger.LogDebug("Skipping type-based dependency {DependencyName} (StrictPublisher=false, validated by type matching)", dep.Name);
                            continue;
                        }

                        // AutoInstall dependencies are resolved here but not automatically installed.
                        // Future work should implement IAutoInstallService to acquire missing AutoInstall content.
                        if (!resolvedIds.Contains(dep.Id.Value))
                        {
                            toProcess.Enqueue(dep.Id.Value);
                        }
                    }
                }
            }
            else
            {
                missingContentIds.Add(contentId);
            }
        }

        if (missingContentIds.Count > 0)
        {
            throw new InvalidOperationException($"Missing or invalid content IDs: {string.Join(", ", missingContentIds)}");
        }

        return resolvedIds;
    }

    /// <inheritdoc/>
    public async Task<DependencyResolutionResult> ResolveDependenciesWithManifestsAsync(IEnumerable<string> contentIds, CancellationToken cancellationToken = default)
    {
        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedManifests = new List<ContentManifest>();
        var missingContentIds = new List<string>();
        var warnings = new List<string>();
        var toProcess = new Queue<string>(contentIds);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processingStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Track currently processing path for circular detection

        while (toProcess.Count > 0)
        {
            var contentId = toProcess.Dequeue();

            // Circular dependency detection
            if (processingStack.Contains(contentId))
            {
                var circularWarning = $"Circular dependency detected: '{contentId}' is already in the resolution path";
                warnings.Add(circularWarning);
                logger.LogWarning("Circular dependency detected: {ContentId} is already in the resolution path", contentId);
                continue;
            }

            if (!visited.Add(contentId))
                continue;

            processingStack.Add(contentId);

            try
            {
                var manifest = await FindManifestInPoolAsync(contentId, cancellationToken);
                if (manifest != null)
                {
                    resolvedIds.Add(manifest.Id.Value);
                    resolvedManifests.Add(manifest);

                    if (manifest.Dependencies != null)
                    {
                        var relevantDeps = manifest.Dependencies.Where(d => d.InstallBehavior == DependencyInstallBehavior.RequireExisting || d.InstallBehavior == DependencyInstallBehavior.AutoInstall);
                        foreach (var dep in relevantDeps)
                        {
                            // Skip default/placeholder IDs - these are generic type-based constraints validated separately
                            if (dep.Id.ToString() == ManifestConstants.DefaultContentDependencyId)
                            {
                                logger.LogDebug("Skipping generic dependency {DependencyName} (type-based constraint, not specific manifest)", dep.Name);
                                continue;
                            }

                            // Skip type-based dependencies (StrictPublisher = false means any matching type will satisfy)
                            // These use semantic IDs like "1.104.any.gameinstallation.zerohour" and are validated separately
                            if (!dep.StrictPublisher)
                            {
                                logger.LogDebug("Skipping type-based dependency {DependencyName} (StrictPublisher=false, validated by type matching)", dep.Name);
                                continue;
                            }

                            if (!resolvedIds.Contains(dep.Id.Value))
                            {
                                toProcess.Enqueue(dep.Id.Value);
                            }
                        }
                    }
                }
                else
                {
                    missingContentIds.Add(contentId);
                }
            }
            finally
            {
                processingStack.Remove(contentId);
            }
        }

        if (missingContentIds.Count > 0)
        {
            return DependencyResolutionResult.CreateFailure($"Missing or invalid content IDs: {string.Join(", ", missingContentIds)}");
        }

        if (warnings.Count > 0)
        {
            return DependencyResolutionResult.CreateSuccessWithWarnings([..resolvedIds], resolvedManifests, missingContentIds, warnings);
        }

        return DependencyResolutionResult.CreateSuccess([..resolvedIds], resolvedManifests, missingContentIds);
    }

    /// <summary>
    /// Finds a compatible acquired manifest match for a declared catalog dependency by identity and version constraint.
    /// </summary>
    /// <param name="declaredDependencyId">The declared dependency identifier.</param>
    /// <param name="dependency">The dependency requirements and version constraints.</param>
    /// <param name="allManifests">All installed content manifests.</param>
    /// <returns>The matching manifest identifier, or <see langword="null"/> when no candidate satisfies version requirements.</returns>
    internal static string? FindVersionIndependentCatalogMatch(
        string declaredDependencyId,
        ContentDependency dependency,
        IReadOnlyList<ContentManifest> allManifests)
    {
        var declaredParts = declaredDependencyId.Split('.');
        if (declaredParts.Length != 5)
        {
            return null;
        }

        var matchingManifests = allManifests
            .Where(manifest => HasCompatibleCatalogIdentity(declaredParts, manifest.Id.Value.Split('.')))
            .ToList();

        if (matchingManifests.Count == 0)
        {
            return null;
        }

        // Evaluate version constraints if specified on dependency
        var versionConstraint = new VersionConstraint
        {
            MinVersion = dependency.MinVersion,
            MaxVersion = dependency.MaxVersion,
        };

        var compatible = matchingManifests.Where(m =>
        {
            if (dependency.CompatibleVersions is { Count: > 0 } &&
                !dependency.CompatibleVersions.Contains(m.Version, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(dependency.MinVersion) || !string.IsNullOrEmpty(dependency.MaxVersion))
            {
                return versionConstraint.IsSatisfiedBy(m.Version);
            }

            return true;
        }).ToList();

        if (compatible.Count == 0)
        {
            return null;
        }

        // Sort descending by parsed version to pick latest compatible version
        var best = compatible
            .OrderByDescending(m => GameVersionHelper.ExtractVersionFromVersionString(m.Version))
            .ThenByDescending(m => m.Version, StringComparer.OrdinalIgnoreCase)
            .First();

        return best.Id.Value;
    }

    private static bool IsPublisherCompatible(string declaredPublisher, string acquiredPublisher) =>
        declaredPublisher.Equals(ManifestConstants.AnyPublisherToken, StringComparison.OrdinalIgnoreCase) ||
        declaredPublisher.Equals(acquiredPublisher, StringComparison.OrdinalIgnoreCase);

    private static bool IsContentTypeCompatible(string declaredType, string acquiredType) =>
        declaredType.Equals(acquiredType, StringComparison.OrdinalIgnoreCase) ||
        (IsPatchOrGameData(declaredType) && IsPatchOrGameData(acquiredType));

    private static bool IsContentNameCompatible(
        string declaredName,
        string acquiredName,
        string declaredType,
        string acquiredType)
    {
        if (declaredName.Equals(acquiredName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (acquiredName.StartsWith(declaredName + ManifestConstants.VariantSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (declaredType.Equals(GameClientType, StringComparison.OrdinalIgnoreCase) &&
            acquiredType.Equals(GameClientType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsPatchOrGameData(declaredType) && IsPatchOrGameDataName(declaredName) && IsPatchOrGameDataName(acquiredName))
        {
            return true;
        }

        return false;
    }

    private static bool IsPatchOrGameDataName(string name) =>
        name.Equals(ZeroHourName, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(GameDataName, StringComparison.OrdinalIgnoreCase);

    private static bool IsPatchOrGameData(string typeOrName) =>
        typeOrName.Equals("patch", StringComparison.OrdinalIgnoreCase) ||
        typeOrName.Equals(GameDataName, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesContentKeyword(string contentId, ContentManifest manifest)
    {
        if (contentId.Contains(GameDataName, StringComparison.OrdinalIgnoreCase) &&
            (manifest.Id.Value.Contains(GameDataName, StringComparison.OrdinalIgnoreCase) ||
             manifest.Name.Contains("Game Data", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if ((contentId.Contains("quickmatchmaps", StringComparison.OrdinalIgnoreCase) ||
             contentId.Contains("mappack", StringComparison.OrdinalIgnoreCase)) &&
            (manifest.ContentType == ContentType.MapPack ||
             manifest.Id.Value.Contains("mappack", StringComparison.OrdinalIgnoreCase) ||
             manifest.Id.Value.Contains("quickmatchmaps", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if ((contentId.Contains("60hz", StringComparison.OrdinalIgnoreCase) ||
             (contentId.Contains(GameClientType, StringComparison.OrdinalIgnoreCase) &&
              !contentId.Contains(GameDataName, StringComparison.OrdinalIgnoreCase) &&
              !contentId.Contains("mappack", StringComparison.OrdinalIgnoreCase))) &&
            manifest.ContentType == ContentType.GameClient)
        {
            return true;
        }

        return false;
    }

    private async Task<ContentManifest?> FindManifestInPoolAsync(string contentId, CancellationToken cancellationToken)
    {
        // 1. Try exact match first
        try
        {
            var exactResult = await manifestPool.GetManifestAsync(ManifestId.Create(contentId), cancellationToken);
            if (exactResult.Success && exactResult.Data != null)
            {
                return exactResult.Data;
            }
        }
        catch (ArgumentException)
        {
            // Invalid manifest ID format for exact match - continue to fallback search
        }

        // 2. Fallback: Search all pooled manifests for a compatible catalog match
        var allResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!allResult.Success || allResult.Data == null)
        {
            logger.LogWarning(
                "[DependencyResolver] Manifest not found for content ID '{ContentId}' and manifest pool is empty or failed to load.",
                contentId);
            return null;
        }

        var poolList = allResult.Data.ToList();
        return FindCompatiblePooledManifest(contentId, poolList);
    }

    private ContentManifest? FindCompatiblePooledManifest(string contentId, IReadOnlyList<ContentManifest> poolList)
    {
        // First pass: try HasCompatibleCatalogIdentity
        var compatible = poolList.FirstOrDefault(m => HasCompatibleCatalogIdentity(contentId, m.Id.Value));
        if (compatible != null)
        {
            logger.LogInformation(
                "[DependencyResolver] Resolved manifest ID '{DeclaredId}' to compatible pooled manifest '{ResolvedId}' ({ManifestName})",
                contentId,
                compatible.Id.Value,
                compatible.Name);
            return compatible;
        }

        // Second pass: if contentId has publisher info, look for best matching manifest from that publisher
        var publisherMatched = FindManifestByPublisherMatch(contentId, poolList);
        if (publisherMatched != null)
        {
            return publisherMatched;
        }

        logger.LogWarning(
            "[DependencyResolver] Manifest not found for content ID '{ContentId}'. Pool contains {Count} manifests: [{AvailableManifests}]",
            contentId,
            poolList.Count,
            string.Join(", ", poolList.Select(m => $"{m.Id.Value} ({m.Name})")));
        return null;
    }

    private ContentManifest? FindManifestByPublisherMatch(string contentId, IReadOnlyList<ContentManifest> poolList)
    {
        var parts = contentId.Split('.');
        if (parts.Length < 3)
        {
            return null;
        }

        var publisher = parts[2];
        var publisherManifests = poolList
            .Where(m => string.Equals(m.Publisher?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) ||
                        m.Id.Value.Contains($".{publisher}.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (publisherManifests.Count == 0)
        {
            return null;
        }

        var matched = publisherManifests.FirstOrDefault(m => MatchesContentKeyword(contentId, m));
        if (matched != null)
        {
            logger.LogInformation(
                "[DependencyResolver] Resolved manifest ID '{DeclaredId}' by publisher/variant match to pooled manifest '{ResolvedId}' ({ManifestName})",
                contentId,
                matched.Id.Value,
                matched.Name);
            return matched;
        }

        return null;
    }

    /// <summary>
    /// Identifies semantic Community Outpost dependencies without weakening exact-ID semantics for
    /// other publishers.
    /// </summary>
    public static class CommunityOutpostDependencyIdentity
    {
        /// <summary>
        /// Determines whether a dependency is an intentionally type-only constraint rather than a
        /// reference to an acquired manifest. Type-only dependencies use the <c>any</c> publisher
        /// segment, for example <c>1.104.any.gameinstallation.zerohour</c>.
        /// </summary>
        /// <param name="dependency">The content dependency to evaluate.</param>
        /// <returns>True if the dependency is a generic type-only constraint; otherwise, false.</returns>
        /// <remarks>
        /// Legacy manifests also used placeholder publishers such as <c>genhub</c> or <c>ea</c>
        /// for GameInstallation RequireExisting constraints. Those IDs never existed in the
        /// manifest pool (real installations are <c>steam</c>/<c>eaapp</c>/etc.), so they must
        /// be treated as type-only. ProfileContentService injects the concrete installation.
        /// </remarks>
        public static bool IsGenericTypeDependency(ContentDependency dependency)
        {
            var idParts = dependency.Id.Value.Split('.');
            if (dependency.StrictPublisher || idParts.Length != 5)
            {
                return false;
            }

            // Only GameInstallation dependencies with "any" or legacy publisher are type-only constraints.
            // Non-GameInstallation dependencies (MapPacks, Addons, Mods, Tools) must be resolved to acquired manifests.
            return dependency.DependencyType == ContentType.GameInstallation &&
                   (idParts[2].Equals("any", StringComparison.OrdinalIgnoreCase) ||
                    dependency.InstallBehavior == DependencyInstallBehavior.RequireExisting);
        }

        /// <summary>
        /// Extracts Community Outpost's stable catalog content code from a concrete manifest ID.
        /// </summary>
        /// <param name="manifestId">The raw manifest identifier string.</param>
        /// <param name="contentType">When successful, the extracted content type segment.</param>
        /// <param name="contentCode">When successful, the resolved Community Outpost content code.</param>
        /// <returns>True if a content code could be determined; otherwise, false.</returns>
        public static bool TryGetCommunityOutpostContentCode(
            string manifestId,
            out string contentType,
            out string contentCode)
        {
            contentType = string.Empty;
            contentCode = string.Empty;
            var idParts = manifestId.Split('.');
            if (idParts.Length != 5 ||
                !idParts[2].Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            contentType = idParts[3];
            var nameSegment = idParts[4];
            var metadata = GenPatcherContentRegistry.GetMetadata(nameSegment);
            if (metadata.ContentType != ContentType.UnknownContentType)
            {
                contentCode = metadata.ContentCode;
                return true;
            }

            var dashIndex = nameSegment.IndexOf('-');
            var codePrefix = dashIndex > 0 ? nameSegment[..dashIndex] : nameSegment;
            var prefixMetadata = GenPatcherContentRegistry.GetMetadata(codePrefix);
            if (prefixMetadata.ContentType != ContentType.UnknownContentType)
            {
                contentCode = prefixMetadata.ContentCode;
                return true;
            }

            contentCode = codePrefix.Length >= 4 ? codePrefix[..4] : codePrefix;
            return !string.IsNullOrEmpty(contentCode);
        }

        /// <summary>
        /// Gets the authoritative Community Outpost code recorded in a manifest, falling back to
        /// its canonical identifier for older manifests that predate the metadata tag.
        /// </summary>
        /// <param name="manifest">The content manifest to evaluate.</param>
        /// <returns>The resolved Community Outpost content code, or an empty string if none matched.</returns>
        public static string GetCommunityOutpostContentCode(ContentManifest manifest)
        {
            var contentCodeTag = manifest.Metadata?.Tags?
                .FirstOrDefault(tag => tag.StartsWith("contentCode:", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(contentCodeTag))
            {
                var tagValue = contentCodeTag["contentCode:".Length..];
                var directMeta = GenPatcherContentRegistry.GetMetadata(tagValue);
                if (directMeta.ContentType != ContentType.UnknownContentType)
                {
                    return directMeta.ContentCode;
                }

                var dashIdx = tagValue.IndexOf('-');
                if (dashIdx > 0)
                {
                    var prefix = tagValue[..dashIdx];
                    var prefixMeta = GenPatcherContentRegistry.GetMetadata(prefix);
                    if (prefixMeta.ContentType != ContentType.UnknownContentType)
                    {
                        return prefixMeta.ContentCode;
                    }
                }

                return tagValue;
            }

            return TryGetCommunityOutpostContentCode(manifest.Id.Value, out _, out var contentCode)
                ? contentCode
                : string.Empty;
        }
    }
}
