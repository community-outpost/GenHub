using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Services.Dependencies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Builds dependency specifications for Generals Online content.
/// Generals Online game clients require a base Zero Hour installation
/// and the QuickMatch MapPack for multiplayer functionality, along with an optional GameData patch.
/// </summary>
public class GeneralsOnlineDependencyBuilder : BaseDependencyBuilder
{
    /// <summary>
    /// Creates a dependency on Zero Hour 1.04 specifically for Generals Online.
    /// Generals Online works with any Zero Hour installation (Steam, EA, or TUC).
    /// </summary>
    /// <returns>A content dependency for Zero Hour 1.04 installation.</returns>
    public static ContentDependency CreateZeroHourDependencyForGeneralsOnline()
    {
        // Use the shared type-only constraint with publisher segment any.
        // Concrete installations are injected by the profile content service.
        return CreateZeroHour104Dependency();
    }

    /// <summary>
    /// Creates a dependency on the GeneralsOnline QuickMatch MapPack.
    /// This is required for QuickMatch multiplayer functionality.
    /// </summary>
    /// <param name="version">Optional version constraint for the mappack.</param>
    /// <returns>A content dependency for the QuickMatch MapPack.</returns>
    public static ContentDependency CreateQuickMatchMapPackDependency(int version = 0)
    {
        return new ContentDependency
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.MapPack,
                GeneralsOnlineConstants.QuickMatchMapPackSuffix,
                version)),
            Name = $"{GeneralsOnlineConstants.QuickMatchMapPackDisplayName} (Required for QuickMatch)",
            DependencyType = ContentType.MapPack,
            InstallBehavior = DependencyInstallBehavior.AutoInstall,
            IsOptional = false,

            // Must be from GeneralsOnline publisher
            StrictPublisher = true,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
            CompatibleGameTypes = new List<GameType> { GameType.ZeroHour },
        };
    }

    /// <summary>
    /// Creates a dependency on the GeneralsOnline GameData patch.
    /// This is an optional auto-install dependency for GeneralsOnline game clients.
    /// </summary>
    /// <param name="version">Optional version constraint for the data patch.</param>
    /// <returns>A content dependency for the GameData patch.</returns>
    public static ContentDependency CreateGameDataPatchDependency(int version = 0)
    {
        return new ContentDependency
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.Patch,
                GeneralsOnlineConstants.GameDataPatchSuffix,
                version)),
            Name = $"{GeneralsOnlineConstants.GameDataDisplayName} (Optional)",
            DependencyType = ContentType.Patch,
            InstallBehavior = DependencyInstallBehavior.AutoInstall,
            IsOptional = true,

            // Must be from GeneralsOnline publisher
            StrictPublisher = true,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
            CompatibleGameTypes = [GameType.ZeroHour],
        };
    }

    /// <summary>
    /// Creates a dependency on the GeneralsOnline 60Hz GameClient.
    /// </summary>
    /// <param name="version">Optional version constraint for the game client.</param>
    /// <returns>A content dependency for the 60Hz GameClient.</returns>
    public static ContentDependency CreateGameClient60HzDependency(int version = 0)
    {
        return new ContentDependency
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.GameClient,
                GeneralsOnlineConstants.Variant60HzSuffix,
                version)),
            Name = $"{GameClientConstants.GeneralsOnline60HzDisplayName} (Required)",
            DependencyType = ContentType.GameClient,
            InstallBehavior = DependencyInstallBehavior.AutoInstall,
            IsOptional = false,

            // Must be from GeneralsOnline publisher
            StrictPublisher = true,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
            CompatibleGameTypes = [GameType.ZeroHour],
        };
    }

    /// <summary>
    /// Gets the list of all dependencies for a Generals Online 60Hz variant.
    /// Includes Zero Hour installation, QuickMatch MapPack, and optional GameData patch.
    /// </summary>
    /// <param name="version">The version of the components to depend on.</param>
    /// <returns>List of dependencies for 60Hz variant.</returns>
    public static List<ContentDependency> GetDependenciesFor60Hz(int version = 0)
    {
        return
        [
            CreateZeroHourDependencyForGeneralsOnline(),
            CreateQuickMatchMapPackDependency(version),
            CreateGameDataPatchDependency(version),
        ];
    }

    /// <summary>
    /// Gets the list of all dependencies for a Generals Online Test Environment variant.
    /// Delegates to 60Hz dependency configuration (Zero Hour installation, QuickMatch MapPack, and optional GameData patch).
    /// </summary>
    /// <param name="version">The version of the components to depend on.</param>
    /// <returns>List of dependencies for Test Environment variant.</returns>
    public static List<ContentDependency> GetDependenciesForTestEnvironment(int version = 0)
    {
        return GetDependenciesFor60Hz(version);
    }

    /// <summary>
    /// Gets the list of dependencies for the GeneralsOnlineGameData data patch.
    /// Requires only a base Zero Hour installation so it can be used with any compatible game client.
    /// </summary>
    /// <param name="clientVersion">Unused version parameter kept for API compatibility.</param>
    /// <returns>List of dependencies for GameData data patch.</returns>
    public static List<ContentDependency> GetDependenciesForGameData(int clientVersion = 0)
    {
        return
        [
            CreateZeroHourDependencyForGeneralsOnline(),
        ];
    }

    /// <summary>
    /// Gets the dependencies for Generals Online content.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <returns>List of dependencies.</returns>
    public override List<ContentDependency> GetDependencies(ContentManifest manifest)
    {
        var userVersion = 0;
        if (!string.IsNullOrWhiteSpace(manifest.Version))
        {
            userVersion = GameVersionHelper.GetGeneralsOnlineManifestIdComponent(manifest.Version);
        }
        else if (!string.IsNullOrWhiteSpace(manifest.Id.Value))
        {
            var parts = manifest.Id.Value.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var parsedVersion))
            {
                userVersion = parsedVersion;
            }
        }

        if (manifest.ContentType == ContentType.GameClient)
        {
            var isTestEnv = manifest.Id.Value.EndsWith($".{GeneralsOnlineConstants.VariantTestEnvironmentSuffix}", StringComparison.OrdinalIgnoreCase)
                || (manifest.Metadata?.Tags is not null && manifest.Metadata.Tags.Contains(GeneralsOnlineVariantTags.TagTestEnvironment));

            return isTestEnv
                ? GetDependenciesForTestEnvironment(userVersion)
                : GetDependenciesFor60Hz(userVersion);
        }

        return manifest.ContentType switch
        {
            ContentType.MapPack => [CreateZeroHourDependencyForGeneralsOnline()],
            ContentType.Patch => GetDependenciesForGameData(userVersion),
            _ => [],
        };
    }
}
