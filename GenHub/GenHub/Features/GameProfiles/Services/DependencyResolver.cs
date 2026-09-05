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

        var isAnyPublisher = declaredParts[2].Equals(ManifestConstants.AnyPublisherToken, StringComparison.OrdinalIgnoreCase);
        if (!isAnyPublisher && !declaredParts[2].Equals(acquiredParts[2], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!declaredParts[3].Equals(acquiredParts[3], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var declaredName = declaredParts[4];
        var acquiredName = acquiredParts[4];
        if (declaredName.Equals(acquiredName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (acquiredName.StartsWith(declaredName + ManifestConstants.VariantSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Allow publisher client or installation variants (e.g. 60hz, unlocked, eac-zerohour) for compatible games
        return AreGameVariantsCompatible(declaredParts[3], declaredName, acquiredName);
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
            {
                continue;
            }

            resolvedIds.Add(contentId);

            try
            {
                var manifest = await ResolveManifestWithFallbackAsync(contentId, cancellationToken);
                if (manifest != null)
                {
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

                            if (!resolvedIds.Contains(dep.Id))
                            {
                                toProcess.Enqueue(dep.Id);
                            }
                        }
                    }
                }
                else
                {
                    missingContentIds.Add(contentId);
                    await LogMissingManifestDiagnosticAsync(contentId, cancellationToken);
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
            {
                continue;
            }

            resolvedIds.Add(contentId);

            try
            {
                var manifest = await ResolveManifestWithFallbackAsync(contentId, cancellationToken);
                if (manifest != null)
                {
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
                            if (currentChain.Contains(dep.Id) || string.Equals(contentId, dep.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                var circularWarning = $"Circular dependency detected: '{dep.Id}' is already in the resolution path";
                                warnings.Add(circularWarning);
                                logger.LogWarning("Circular dependency detected: {ContentId} is already in the resolution path", dep.Id);
                            }
                            else if (!resolvedIds.Contains(dep.Id) && !visited.Contains(dep.Id))
                            {
                                var depAncestors = new HashSet<string>(currentChain, StringComparer.OrdinalIgnoreCase) { contentId };
                                ancestorMap[dep.Id] = depAncestors;
                                toProcess.Enqueue(dep.Id);
                            }
                        }
                    }
                }
                else
                {
                    missingContentIds.Add(contentId);
                    await LogMissingManifestDiagnosticAsync(contentId, cancellationToken);
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

    private static bool AreGameVariantsCompatible(string contentType, string declaredName, string acquiredName)
    {
        if (!contentType.Equals(ContentType.GameClient.ToManifestIdString(), StringComparison.OrdinalIgnoreCase) &&
            !contentType.Equals(ContentType.GameInstallation.ToManifestIdString(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var isDeclaredZeroHour = IsZeroHourContent(declaredName);
        var isAcquiredZeroHour = IsZeroHourContent(acquiredName);
        var isDeclaredGenerals = IsGeneralsContent(declaredName);
        var isAcquiredGenerals = IsGeneralsContent(acquiredName);

        // Reject cross-game mismatches (Generals client matching Zero Hour, or vice versa)
        if (isDeclaredZeroHour && isAcquiredGenerals)
        {
            return false;
        }

        if (isDeclaredGenerals && isAcquiredZeroHour)
        {
            return false;
        }

        return true;
    }

    private static bool IsZeroHourContent(string name) =>
        name.Contains("zerohour", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("zh", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneralsContent(string name) =>
        name.Contains("generals", StringComparison.OrdinalIgnoreCase) && !IsZeroHourContent(name);

    private async Task<ContentManifest?> ResolveManifestWithFallbackAsync(string contentId, CancellationToken cancellationToken)
    {
        if (ManifestId.TryCreate(contentId, out var manifestId))
        {
            var exactResult = await manifestPool.GetManifestAsync(manifestId, cancellationToken);
            if (exactResult.Success && exactResult.Data != null)
            {
                return exactResult.Data;
            }
        }

        // Fallback: Check manifest pool for compatible catalog identity
        var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (allManifestsResult.Success && allManifestsResult.Data != null)
        {
            var declaredParts = contentId.Split(ManifestConstants.ManifestIdSegmentSeparator);
            ContentManifest? compatibleManifest = allManifestsResult.Data.FirstOrDefault(m =>
            {
                var acquiredParts = m.Id.Value.Split(ManifestConstants.ManifestIdSegmentSeparator);
                return HasCompatibleCatalogIdentity(declaredParts, acquiredParts);
            });

            if (compatibleManifest != null)
            {
                logger.LogInformation(
                    "[DependencyResolver] Resolved declared content ID '{DeclaredId}' to compatible pool manifest '{AcquiredId}' ({ManifestName})",
                    contentId,
                    compatibleManifest.Id.Value,
                    compatibleManifest.Name);
                return compatibleManifest;
            }
        }

        return null;
    }

    private async Task LogMissingManifestDiagnosticAsync(string contentId, CancellationToken cancellationToken)
    {
        var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        var availableIds = allManifestsResult.Success && allManifestsResult.Data != null
            ? string.Join(", ", allManifestsResult.Data.Select(m => m.Id.Value))
            : "none";

        logger.LogWarning(
            "[DependencyResolver] Manifest not found for content ID: {ContentId}. Available manifests in pool: [{AvailableIds}]",
            contentId,
            availableIds);
    }
}
