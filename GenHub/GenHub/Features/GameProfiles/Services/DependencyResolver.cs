using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
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

        return IsContentNameCompatible(declaredParts[4], acquiredParts[4], declaredType);
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
        var ancestorMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in contentIds)
        {
            ancestorMap[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        while (toProcess.Count > 0)
        {
            var contentId = toProcess.Dequeue();

            if (!visited.Add(contentId))
                continue;

            resolvedIds.Add(contentId);

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
                        ancestorMap.TryGetValue(contentId, out var currentAncestors);
                        var currentChain = currentAncestors ?? [];

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

                            // True circular dependency: the dependency is already an ancestor of the current node
                            if (currentChain.Contains(dep.Id.Value) || string.Equals(contentId, dep.Id.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                var circularWarning = $"Circular dependency detected: '{dep.Id.Value}' is already in the resolution path";
                                warnings.Add(circularWarning);
                                logger.LogWarning("Circular dependency detected: {ContentId} is already in the resolution path", dep.Id.Value);
                            }
                            else if (!resolvedIds.Contains(dep.Id.Value) && !visited.Contains(dep.Id.Value))
                            {
                                var depAncestors = new HashSet<string>(currentChain, StringComparer.OrdinalIgnoreCase) { contentId };
                                ancestorMap[dep.Id.Value] = depAncestors;
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
            catch (ArgumentException ex)
            {
                missingContentIds.Add(contentId);
                logger.LogWarning(ex, "Invalid manifest ID during dependency resolution: {ContentId}", contentId);
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

    private static bool IsPublisherCompatible(string declaredPublisher, string acquiredPublisher) =>
        declaredPublisher.Equals(ManifestConstants.AnyPublisherToken, StringComparison.OrdinalIgnoreCase) ||
        declaredPublisher.Equals(acquiredPublisher, StringComparison.OrdinalIgnoreCase);

    private static bool IsContentTypeCompatible(string declaredType, string acquiredType) =>
        declaredType.Equals(acquiredType, StringComparison.OrdinalIgnoreCase) ||
        (IsPatchOrGameData(declaredType) && IsPatchOrGameData(acquiredType));

    private static bool IsContentNameCompatible(
        string declaredName,
        string acquiredName,
        string declaredType)
    {
        if (declaredName.Equals(acquiredName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (acquiredName.StartsWith(declaredName + ManifestConstants.VariantSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (declaredType.Equals(ManifestConstants.GameClientContentTypeName, StringComparison.OrdinalIgnoreCase) ||
            declaredType.Equals(ContentType.GameInstallation.ToManifestIdString(), StringComparison.OrdinalIgnoreCase))
        {
            return AreGameVariantsCompatible(declaredName, acquiredName);
        }

        if (IsPatchOrGameData(declaredType) && IsPatchOrGameDataName(declaredName) && IsPatchOrGameDataName(acquiredName))
        {
            return true;
        }

        return false;
    }

    private static bool IsZeroHourIdentifier(string name)
    {
        if (name.Contains(ManifestConstants.ZeroHourContentName, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(ManifestConstants.GeneralsZeroHourContentName, StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(ManifestConstants.ZeroHourShortContentName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = name.Split(ManifestConstants.VariantSeparator);
        return tokens.Any(IsZeroHourToken);
    }

    private static bool IsZeroHourToken(string token) =>
        string.Equals(token, ManifestConstants.ZeroHourShortContentName, StringComparison.OrdinalIgnoreCase) ||
        token.EndsWith(ManifestConstants.ZeroHourShortContentName, StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneralsIdentifier(string name)
    {
        if (IsZeroHourIdentifier(name))
        {
            return false;
        }

        if (string.Equals(name, ManifestConstants.GeneralsContentName, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(ManifestConstants.GeneralsContentName, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(ManifestConstants.GeneralsContentName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = name.Split(ManifestConstants.VariantSeparator);
        return tokens.Any(t =>
            string.Equals(t, ManifestConstants.GeneralsContentName, StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith(ManifestConstants.GeneralsContentName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool AreGameVariantsCompatible(string declaredName, string acquiredName)
    {
        var isDeclaredZeroHour = IsZeroHourIdentifier(declaredName);
        var isAcquiredZeroHour = IsZeroHourIdentifier(acquiredName);
        var isDeclaredGenerals = IsGeneralsIdentifier(declaredName);
        var isAcquiredGenerals = IsGeneralsIdentifier(acquiredName);

        if ((isDeclaredZeroHour && isAcquiredGenerals) || (isDeclaredGenerals && isAcquiredZeroHour))
        {
            return false;
        }

        return true;
    }

    private static bool IsPatchOrGameDataName(string name) =>
        name.Equals(ManifestConstants.ZeroHourContentName, StringComparison.OrdinalIgnoreCase) ||
        name.Equals(ManifestConstants.GameDataContentTypeName, StringComparison.OrdinalIgnoreCase);

    private static bool IsPatchOrGameData(string typeOrName) =>
        typeOrName.Equals(ContentType.Patch.ToManifestIdString(), StringComparison.OrdinalIgnoreCase) ||
        typeOrName.Equals(ManifestConstants.GameDataContentTypeName, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesContentKeyword(string contentId, ContentManifest manifest)
    {
        if (contentId.Contains(ManifestConstants.GameDataContentTypeName, StringComparison.OrdinalIgnoreCase) &&
            (manifest.Id.Value.Contains(ManifestConstants.GameDataContentTypeName, StringComparison.OrdinalIgnoreCase) ||
             manifest.Name.Contains(ManifestConstants.GameDataDisplayKeyword, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if ((contentId.Contains(ManifestConstants.QuickMatchMapsKeyword, StringComparison.OrdinalIgnoreCase) ||
             contentId.Contains(ManifestConstants.MapPackKeyword, StringComparison.OrdinalIgnoreCase)) &&
            (manifest.ContentType == ContentType.MapPack ||
             manifest.Id.Value.Contains(ManifestConstants.MapPackKeyword, StringComparison.OrdinalIgnoreCase) ||
             manifest.Id.Value.Contains(ManifestConstants.QuickMatchMapsKeyword, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if ((contentId.Contains(ManifestConstants.SixtyHzKeyword, StringComparison.OrdinalIgnoreCase) ||
             (contentId.Contains(ManifestConstants.GameClientContentTypeName, StringComparison.OrdinalIgnoreCase) &&
              !contentId.Contains(ManifestConstants.GameDataContentTypeName, StringComparison.OrdinalIgnoreCase) &&
              !contentId.Contains(ManifestConstants.MapPackKeyword, StringComparison.OrdinalIgnoreCase))) &&
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
}
