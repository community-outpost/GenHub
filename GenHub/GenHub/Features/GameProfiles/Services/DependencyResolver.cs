using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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

        var isAnyPublisher = declaredParts[2].Equals(ManifestConstants.AnyPublisherToken, StringComparison.OrdinalIgnoreCase);
        if (!isAnyPublisher && !declaredParts[2].Equals(acquiredParts[2], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var declaredType = declaredParts[3];
        var acquiredType = acquiredParts[3];
        var declaredName = declaredParts[4];
        var acquiredName = acquiredParts[4];

        // Content type match: allow exact match or interchangeable patch/gamedata types
        var isContentTypeCompatible = declaredType.Equals(acquiredType, StringComparison.OrdinalIgnoreCase) ||
            (IsPatchOrGameData(declaredType) && IsPatchOrGameData(acquiredType));

        if (!isContentTypeCompatible)
        {
            return false;
        }

        if (declaredName.Equals(acquiredName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (acquiredName.StartsWith(declaredName + ManifestConstants.VariantSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Allow gameclient variant compatibility (e.g. zerohour vs 60hz)
        if (declaredType.Equals("gameclient", StringComparison.OrdinalIgnoreCase) &&
            acquiredType.Equals("gameclient", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Allow gamedata/patch cross-naming compatibility
        if (IsPatchOrGameData(declaredType) && (declaredName.Equals("zerohour", StringComparison.OrdinalIgnoreCase) || declaredName.Equals("gamedata", StringComparison.OrdinalIgnoreCase)) &&
            (acquiredName.Equals("zerohour", StringComparison.OrdinalIgnoreCase) || acquiredName.Equals("gamedata", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
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

    private static bool IsPatchOrGameData(string typeOrName) =>
        typeOrName.Equals("patch", StringComparison.OrdinalIgnoreCase) ||
        typeOrName.Equals("gamedata", StringComparison.OrdinalIgnoreCase);

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
        if (allResult.Success && allResult.Data != null)
        {
            var poolList = allResult.Data.ToList();

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
            var parts = contentId.Split('.');
            if (parts.Length >= 3)
            {
                var publisher = parts[2];
                var publisherManifests = poolList
                    .Where(m => string.Equals(m.Publisher?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) ||
                                m.Id.Value.Contains($".{publisher}.", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (publisherManifests.Count > 0)
                {
                    var matchedByKeyword = publisherManifests.FirstOrDefault(m =>
                    {
                        if (contentId.Contains("gamedata", StringComparison.OrdinalIgnoreCase) &&
                            (m.Id.Value.Contains("gamedata", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Game Data", StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }

                        if ((contentId.Contains("quickmatchmaps", StringComparison.OrdinalIgnoreCase) || contentId.Contains("mappack", StringComparison.OrdinalIgnoreCase)) &&
                            (m.ContentType == ContentType.MapPack || m.Id.Value.Contains("mappack", StringComparison.OrdinalIgnoreCase) || m.Id.Value.Contains("quickmatchmaps", StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }

                        if ((contentId.Contains("60hz", StringComparison.OrdinalIgnoreCase) || (contentId.Contains("gameclient", StringComparison.OrdinalIgnoreCase) && !contentId.Contains("gamedata", StringComparison.OrdinalIgnoreCase) && !contentId.Contains("mappack", StringComparison.OrdinalIgnoreCase))) &&
                            m.ContentType == ContentType.GameClient)
                        {
                            return true;
                        }

                        return false;
                    });

                    if (matchedByKeyword != null)
                    {
                        logger.LogInformation(
                            "[DependencyResolver] Resolved manifest ID '{DeclaredId}' by publisher/variant match to pooled manifest '{ResolvedId}' ({ManifestName})",
                            contentId,
                            matchedByKeyword.Id.Value,
                            matchedByKeyword.Name);
                        return matchedByKeyword;
                    }
                }
            }

            logger.LogWarning(
                "[DependencyResolver] Manifest not found for content ID '{ContentId}'. Pool contains {Count} manifests: [{AvailableManifests}]",
                contentId,
                poolList.Count,
                string.Join(", ", poolList.Select(m => $"{m.Id.Value} ({m.Name})")));
        }
        else
        {
            logger.LogWarning(
                "[DependencyResolver] Manifest not found for content ID '{ContentId}' and manifest pool is empty or failed to load.",
                contentId);
        }

        return null;
    }
}