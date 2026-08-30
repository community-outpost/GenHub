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

        return acquiredName.StartsWith(declaredName + ManifestConstants.VariantSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<HashSet<string>> ResolveDependenciesAsync(IEnumerable<string> contentIds, CancellationToken cancellationToken = default)
    {
        var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        var allManifests = allManifestsResult.Success && allManifestsResult.Data != null
            ? allManifestsResult.Data.ToList()
            : [];

        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toProcess = new Queue<string>(contentIds);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingContentIds = new List<string>();

        while (toProcess.Count > 0)
        {
            var contentId = toProcess.Dequeue();
            if (!visited.Add(contentId))
                continue;

            resolvedIds.Add(contentId);

            try
            {
                var manifest = allManifests.FirstOrDefault(m => string.Equals(m.Id.Value, contentId, StringComparison.OrdinalIgnoreCase));
                if (manifest == null)
                {
                    var manifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(contentId), cancellationToken);
                    manifest = manifestResult.Success ? manifestResult.Data : null;
                }

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

                            // Skip type-only constraints such as "1.104.any.gameinstallation.zerohour".
                            // A concrete ID is still a real dependency when StrictPublisher is false:
                            // Community Outpost emits those IDs with semantic matching metadata.
                            if (CommunityOutpostDependencyIdentity.IsGenericTypeDependency(dep))
                            {
                                logger.LogDebug("Skipping type-based dependency {DependencyName} (validated by type matching)", dep.Name);
                                continue;
                            }

                            // TODO: AutoInstall dependencies are resolved here but not automatically installed.
                            // Future PR should implement IAutoInstallService to acquire missing AutoInstall content.
                            var canonicalDependencyId = ResolveCanonicalDependencyId(
                                dep,
                                allManifests);
                            if (!resolvedIds.Contains(canonicalDependencyId))
                            {
                                toProcess.Enqueue(canonicalDependencyId);
                            }
                        }
                    }
                }
                else
                {
                    // Manifest not found - log and collect missing IDs
                    missingContentIds.Add(contentId);
                    logger.LogWarning("Manifest not found for content ID: {ContentId}", contentId);
                }
            }
            catch (ArgumentException ex)
            {
                // Invalid ID - log and collect as missing
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
    public async Task<DependencyResolutionResult> ResolveDependenciesWithManifestsAsync(
        IEnumerable<string> contentIds,
        CancellationToken cancellationToken = default)
    {
        var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        var allManifests = allManifestsResult.Success && allManifestsResult.Data != null
            ? allManifestsResult.Data.ToList()
            : [];

        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedManifests = new List<ContentManifest>();
        var toProcess = new Queue<string>(contentIds);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processingStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingContentIds = new List<string>();
        var warnings = new List<string>();

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
            resolvedIds.Add(contentId);

            try
            {
                var manifest = await TryFetchManifestAsync(contentId, allManifests, cancellationToken);

                if (manifest != null)
                {
                    resolvedManifests.Add(manifest);
                    EnqueueDependencies(manifest, allManifests, resolvedIds, toProcess);
                }
                else
                {
                    // Manifest not found
                    missingContentIds.Add(contentId);
                    logger.LogWarning("Manifest not found for content ID: {ContentId}", contentId);
                }
            }
            catch (ArgumentException ex)
            {
                // Invalid ID
                missingContentIds.Add(contentId);
                logger.LogWarning(ex, "Invalid manifest ID during dependency resolution: {ContentId}", contentId);
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

    private async Task<ContentManifest?> TryFetchManifestAsync(
        string contentId,
        IReadOnlyList<ContentManifest> allManifests,
        CancellationToken cancellationToken)
    {
        var manifest = allManifests.FirstOrDefault(m => string.Equals(m.Id.Value, contentId, StringComparison.OrdinalIgnoreCase));
        if (manifest != null)
        {
            return manifest;
        }

        var manifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(contentId), cancellationToken);
        return manifestResult.Success ? manifestResult.Data : null;
    }

    private void EnqueueDependencies(
        ContentManifest manifest,
        IReadOnlyList<ContentManifest> allManifests,
        HashSet<string> resolvedIds,
        Queue<string> toProcess)
    {
        if (manifest.Dependencies == null)
        {
            return;
        }

        var relevantDeps = manifest.Dependencies.Where(d =>
            d.InstallBehavior == DependencyInstallBehavior.RequireExisting ||
            d.InstallBehavior == DependencyInstallBehavior.AutoInstall);

        foreach (var dep in relevantDeps)
        {
            if (dep.Id.ToString() == ManifestConstants.DefaultContentDependencyId)
            {
                logger.LogDebug("Skipping generic dependency {DependencyName} (type-based constraint, not specific manifest)", dep.Name);
                continue;
            }

            if (CommunityOutpostDependencyIdentity.IsGenericTypeDependency(dep))
            {
                logger.LogDebug("Skipping type-based dependency {DependencyName} (validated by type matching)", dep.Name);
                continue;
            }

            var canonicalDependencyId = ResolveCanonicalDependencyId(dep, allManifests);
            if (!resolvedIds.Contains(canonicalDependencyId))
            {
                toProcess.Enqueue(canonicalDependencyId);
            }
        }
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
            .Where(manifest => HasCompatibleCatalogIdentity(declaredParts, manifest.Id.Value))
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

    private static bool HasCompatibleCatalogIdentity(string[] declaredParts, string acquiredManifestId)
    {
        var acquiredParts = acquiredManifestId.Split('.');
        return HasCompatibleCatalogIdentity(declaredParts, acquiredParts);
    }

    /// <summary>
    /// Resolves the real acquired identifier for a declared dependency. Community Outpost
    /// aliases by content code. Other publishers with <see cref="ContentDependency.StrictPublisher"/>
    /// false also accept a version-independent (and variant-suffix) match so catalog constraints
    /// like <c>&gt;=8.6</c> can bind to an acquired <c>8.9</c> artifact.
    /// </summary>
    private string ResolveCanonicalDependencyId(
        ContentDependency dependency,
        IReadOnlyList<ContentManifest> allManifests)
    {
        var declaredDependencyId = dependency.Id.Value;
        var exactManifest = allManifests.FirstOrDefault(m =>
            string.Equals(m.Id.Value, declaredDependencyId, StringComparison.OrdinalIgnoreCase));
        if (exactManifest != null)
        {
            return exactManifest.Id.Value;
        }

        if (CommunityOutpostDependencyIdentity.TryGetCommunityOutpostContentCode(
                declaredDependencyId,
                out var declaredContentType,
                out var declaredContentCode))
        {
            var canonicalManifest = allManifests.FirstOrDefault(manifest =>
            {
                if (!string.Equals(
                        manifest.Publisher?.PublisherType,
                        CommunityOutpostConstants.PublisherType,
                        StringComparison.OrdinalIgnoreCase) ||
                    !CommunityOutpostDependencyIdentity.TryGetCommunityOutpostContentCode(
                        manifest.Id.Value,
                        out var manifestContentType,
                        out _))
                {
                    return false;
                }

                return manifestContentType.Equals(declaredContentType, StringComparison.OrdinalIgnoreCase) &&
                       CommunityOutpostDependencyIdentity.GetCommunityOutpostContentCode(manifest).Equals(
                           declaredContentCode,
                           StringComparison.OrdinalIgnoreCase);
            });

            if (canonicalManifest != null)
            {
                logger.LogInformation(
                    "Resolved Community Outpost dependency alias {DeclaredDependencyId} to acquired manifest {CanonicalManifestId}",
                    declaredDependencyId,
                    canonicalManifest.Id.Value);
                return canonicalManifest.Id.Value;
            }
        }

        if (dependency.StrictPublisher)
        {
            return declaredDependencyId;
        }

        var catalogMatch = FindVersionIndependentCatalogMatch(declaredDependencyId, dependency, allManifests);
        if (catalogMatch != null)
        {
            logger.LogInformation(
                "Resolved catalog dependency alias {DeclaredDependencyId} to acquired manifest {CanonicalManifestId}",
                declaredDependencyId,
                catalogMatch);
            return catalogMatch;
        }

        return declaredDependencyId;
    }
}
