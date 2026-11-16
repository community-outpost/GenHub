using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Finds required dependencies that the enabled content of a profile does not satisfy.
/// </summary>
/// <param name="manifestPool">The manifest pool used to load enabled content.</param>
/// <param name="logger">The logger.</param>
public sealed class ContentDependencyValidator(
    IContentManifestPool manifestPool,
    ILogger<ContentDependencyValidator> logger) : IContentDependencyValidator
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<ContentDependencyGap>> FindMissingDependenciesAsync(
        IEnumerable<string> enabledContentIds,
        CancellationToken cancellationToken = default)
    {
        var manifests = new List<ContentManifest>();
        foreach (var contentId in enabledContentIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ManifestId.TryCreate(contentId, out var manifestId))
            {
                logger.LogWarning("Skipping enabled content {ContentId} in dependency validation: the ID is not valid", contentId);
                continue;
            }

            var manifestResult = await manifestPool.GetManifestAsync(manifestId, cancellationToken);
            if (manifestResult.Success && manifestResult.Data != null)
            {
                manifests.Add(manifestResult.Data);
            }
            else
            {
                logger.LogWarning("Skipping enabled content {ContentId} in dependency validation: its manifest could not be loaded", contentId);
            }
        }

        return FindMissingDependencies(manifests);
    }

    /// <inheritdoc/>
    public bool IsSatisfiedBy(ContentDependency dependency, IReadOnlyCollection<ContentManifest> manifests) =>
        IsSatisfied(dependency, manifests);

    /// <inheritdoc/>
    public bool AppliesTo(ContentDependency dependency, IReadOnlyCollection<ContentManifest> enabledManifests) =>
        !dependency.RetailClientOnly || enabledManifests.Any(IsRetailGameClient);

    /// <inheritdoc/>
    public IReadOnlyList<ContentDependencyGap> FindMissingDependencies(IReadOnlyCollection<ContentManifest> enabledManifests)
    {
        var gaps = new List<ContentDependencyGap>();
        var reportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in enabledManifests)
        {
            foreach (var dependency in manifest.Dependencies ?? [])
            {
                if (!IsRequired(dependency) ||
                    !AppliesTo(dependency, enabledManifests) ||
                    IsSatisfied(dependency, enabledManifests) ||
                    !reportedKeys.Add(GetGapKey(dependency)))
                {
                    continue;
                }

                var gap = CreateGap(manifest, dependency);
                logger.LogInformation(
                    "Content {RequiredById} requires {DependencyId} ({DependencyType}), which is not enabled",
                    gap.RequiredById,
                    gap.DependencyId,
                    gap.DependencyType);
                gaps.Add(gap);
            }
        }

        return gaps;
    }

    /// <summary>
    /// Gets the four-character content code from a Community Outpost manifest ID.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="contentCode">The content code, or an empty string.</param>
    /// <returns>True when the ID is a Community Outpost manifest ID with a content code.</returns>
    internal static bool TryParseCommunityOutpostContentCode(string manifestId, out string contentCode)
    {
        contentCode = string.Empty;
        var parts = manifestId.Split('.');

        if (parts.Length < 5 ||
            !parts[2].Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var codePart = parts[4];
        contentCode = codePart.Length >= 4 ? codePart[..4] : codePart;
        return !string.IsNullOrEmpty(contentCode);
    }

    /// <summary>
    /// Checks whether a manifest satisfies a dependency by content type and name, ignoring publisher and schema version.
    /// </summary>
    /// <param name="dependency">The dependency.</param>
    /// <param name="declaredParts">The segments of the declared dependency ID.</param>
    /// <param name="manifest">The candidate manifest.</param>
    /// <returns>True if the manifest matches by type and name; otherwise, false.</returns>
    internal static bool MatchesByType(ContentDependency dependency, string[] declaredParts, ContentManifest manifest)
    {
        if (manifest.ContentType != dependency.DependencyType)
        {
            return false;
        }

        if (IsFoundationType(dependency.DependencyType) &&
            dependency.CompatibleGameTypes is { Count: > 0 } &&
            dependency.CompatibleGameTypes.Contains(manifest.TargetGame))
        {
            return true;
        }

        if (declaredParts.Length < ManifestConstants.MinManifestSegments)
        {
            return false;
        }

        var manifestParts = manifest.Id.Value.Split('.');
        return manifestParts.Length >= ManifestConstants.MinManifestSegments &&
               string.Equals(manifestParts[3], declaredParts[3], StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(manifestParts[4], declaredParts[4], StringComparison.OrdinalIgnoreCase) ||
                CatalogManifestIdentity.IsContentNameOrVariantMatch(manifestParts[4], declaredParts[4]));
    }

    private static bool IsRequired(ContentDependency dependency) =>
        !dependency.IsOptional &&
        dependency.InstallBehavior is DependencyInstallBehavior.RequireExisting or DependencyInstallBehavior.AutoInstall;

    private static bool IsRetailGameClient(ContentManifest manifest)
    {
        if (manifest.ContentType != ContentType.GameClient)
        {
            return false;
        }

        var parts = manifest.Id.Value.Split('.');
        return parts.Length >= ManifestConstants.MinManifestSegments && InstallationExtensions.IsInstallationIdentifier(parts[2]);
    }

    private static bool IsFoundationType(ContentType contentType) =>
        contentType is ContentType.GameInstallation or ContentType.GameClient;

    private static bool IsSatisfied(ContentDependency dependency, IReadOnlyCollection<ContentManifest> enabledManifests)
    {
        var manifests = enabledManifests
            .Where(manifest => IsCompatibleCandidate(dependency, manifest) && MatchesStrictPublisher(dependency, manifest))
            .ToList();
        var declaredId = dependency.Id.Value;
        if (string.Equals(declaredId, ManifestConstants.DefaultContentDependencyId, StringComparison.OrdinalIgnoreCase))
        {
            return manifests.Any(manifest => manifest.ContentType == dependency.DependencyType);
        }

        if (manifests.Any(manifest => string.Equals(manifest.Id.Value, declaredId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var declaredParts = declaredId.Split('.');
        if (declaredParts.Length == ManifestConstants.MinManifestSegments &&
            manifests.Any(manifest => DependencyResolver.HasCompatibleIdentity(declaredParts, dependency, manifest)))
        {
            return true;
        }

        return (!dependency.StrictPublisher && IsSatisfiedByType(dependency, declaredParts, manifests)) ||
            IsSatisfiedByBundledBasePayload(dependency, enabledManifests);
    }

    /// <summary>
    /// Accepts content that already ships a Community Outpost base item's output file, the same rule
    /// the acquired-manifest lookup uses. A different bundle's own version is unrelated to the base
    /// item's, so only the game and strict publisher rules apply to it. The base item itself must
    /// still meet the version rules.
    /// </summary>
    private static bool IsSatisfiedByBundledBasePayload(ContentDependency dependency, IReadOnlyCollection<ContentManifest> enabledManifests)
    {
        if (!TryParseCommunityOutpostContentCode(dependency.Id.Value, out var contentCode))
        {
            return false;
        }

        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);
        if (!metadata.IsBaseDependency || string.IsNullOrWhiteSpace(metadata.OutputFilename))
        {
            return false;
        }

        return enabledManifests.Any(manifest =>
            MatchesStrictPublisher(dependency, manifest) &&
            IsCompatibleGame(dependency, manifest) &&
            (!IsSameCommunityOutpostItem(manifest, contentCode) || IsCompatibleCandidate(dependency, manifest)) &&
            ManifestVariantResolver.ResolveFiles(manifest).Any(file => string.Equals(
                System.IO.Path.GetFileName(file.RelativePath),
                metadata.OutputFilename,
                StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsSameCommunityOutpostItem(ContentManifest manifest, string contentCode) =>
        TryParseCommunityOutpostContentCode(manifest.Id.Value, out var manifestCode) &&
        string.Equals(manifestCode, contentCode, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesStrictPublisher(ContentDependency dependency, ContentManifest manifest) =>
        !dependency.StrictPublisher ||
        string.IsNullOrEmpty(dependency.PublisherType) ||
        string.Equals(manifest.Publisher?.PublisherType, dependency.PublisherType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rejects a candidate for the wrong game or outside the declared version range. A candidate
    /// without a recorded game or version is not rejected on that basis.
    /// </summary>
    private static bool IsCompatibleCandidate(ContentDependency dependency, ContentManifest manifest) =>
        IsCompatibleGame(dependency, manifest) &&
        (!HasComparableVersion(manifest.Version) || IsVersionCompatible(manifest.Version, dependency));

    private static bool IsCompatibleGame(ContentDependency dependency, ContentManifest manifest) =>
        dependency.CompatibleGameTypes is not { Count: > 0 } ||
        manifest.TargetGame == GameType.Unknown ||
        dependency.CompatibleGameTypes.Contains(manifest.TargetGame);

    private static bool HasComparableVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version) &&
        !string.Equals(version, ManifestConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase) &&
        version.Any(char.IsDigit);

    private static bool IsVersionInRange(string version, ContentDependency dependency)
    {
        if (!string.IsNullOrWhiteSpace(dependency.MinVersion))
        {
            var comparison = CatalogManifestIdentity.CompareVersions(version, dependency.MinVersion);
            if (dependency.MinInclusive ? comparison < 0 : comparison <= 0)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(dependency.MaxVersion))
        {
            var comparison = CatalogManifestIdentity.CompareVersions(version, dependency.MaxVersion);
            if (dependency.MaxInclusive ? comparison > 0 : comparison >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsVersionCompatible(string version, ContentDependency dependency)
    {
        if (!string.IsNullOrWhiteSpace(dependency.ExactVersion))
        {
            return CatalogManifestIdentity.CompareVersions(version, dependency.ExactVersion) == 0;
        }

        // Like the dependency resolver, a version list and a range must both hold.
        if (dependency.CompatibleVersions is { Count: > 0 } &&
            dependency.CompatibleVersions.All(compatible =>
                !string.Equals(compatible, version, StringComparison.OrdinalIgnoreCase) &&
                CatalogManifestIdentity.CompareVersions(compatible, version) != 0))
        {
            return false;
        }

        return IsVersionInRange(version, dependency);
    }

    private static bool IsSatisfiedByType(
        ContentDependency dependency,
        string[] declaredParts,
        IReadOnlyCollection<ContentManifest> manifests) =>
        manifests.Any(manifest => MatchesByType(dependency, declaredParts, manifest));

    private static ContentDependencyGap CreateGap(ContentManifest requiredBy, ContentDependency dependency)
    {
        var dependencyId = dependency.Id.Value;
        var isCommunityOutpostContent = CommunityOutpostDependencyIdentity.TryGetCommunityOutpostContentCode(
            dependencyId,
            out _,
            out var contentCode);

        var displayName = dependency.Name;
        if (isCommunityOutpostContent)
        {
            var registryName = GenPatcherContentRegistry.GetMetadata(contentCode).DisplayName;
            if (!string.IsNullOrWhiteSpace(registryName))
            {
                displayName = registryName;
            }
        }

        return new ContentDependencyGap
        {
            DisplayName = displayName ?? string.Empty,
            DependencyId = dependencyId,
            DependencyType = dependency.DependencyType,
            CanDownload = isCommunityOutpostContent && !IsFoundationType(dependency.DependencyType),
            RequiredByName = requiredBy.Name ?? string.Empty,
            RequiredById = requiredBy.Id.Value,
            ItemKey = GetGapIdentityKey(dependency),
            Dependency = dependency,
        };
    }

    /// <summary>
    /// Keys a gap by what it asks for. Two declarations of the same item with different version,
    /// publisher or game limits stay separate, so the stricter one is not dropped.
    /// </summary>
    private static string GetGapKey(ContentDependency dependency) =>
        $"{GetGapIdentityKey(dependency)}|{dependency.MinVersion}|{dependency.MinInclusive}|{dependency.MaxVersion}|{dependency.MaxInclusive}|{dependency.ExactVersion}|{string.Join(",", dependency.CompatibleVersions ?? [])}|{dependency.StrictPublisher}|{dependency.PublisherType}|{string.Join(",", (dependency.CompatibleGameTypes ?? []).Order())}";

    private static string GetGapIdentityKey(ContentDependency dependency)
    {
        if (CommunityOutpostDependencyIdentity.TryGetCommunityOutpostContentCode(dependency.Id.Value, out _, out var contentCode))
        {
            return $"{CommunityOutpostConstants.PublisherType}:{contentCode}";
        }

        if (IsFoundationType(dependency.DependencyType) && !dependency.StrictPublisher)
        {
            return $"{dependency.DependencyType}:{string.Join(",", (dependency.CompatibleGameTypes ?? []).Order())}";
        }

        if (string.Equals(dependency.Id.Value, ManifestConstants.DefaultContentDependencyId, StringComparison.OrdinalIgnoreCase))
        {
            return $"{dependency.DependencyType}:{dependency.Id.Value}";
        }

        return dependency.Id.Value;
    }
}
