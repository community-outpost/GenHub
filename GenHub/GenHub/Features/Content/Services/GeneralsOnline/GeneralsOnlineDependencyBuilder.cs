using System.Collections.Generic;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Services.Dependencies;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Builds dependency specifications for Generals Online content.
/// Generals Online game clients require a base Zero Hour installation
/// and the QuickMatch MapPack for multiplayer functionality.
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
        return new ContentDependency
        {
            Id = ManifestId.Create($"1.104.genhub.gameinstallation.zerohour"),
            Name = GameClientConstants.ZeroHourInstallationDependencyName,
            DependencyType = ContentType.GameInstallation,

            // "1.04"
            MinVersion = ManifestConstants.ZeroHourManifestVersion,
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,

            // Any publisher's ZH installation will work
            StrictPublisher = false,
            CompatibleGameTypes = new List<GameType> { GameType.ZeroHour },
        };
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
    /// Creates a dependency on the GeneralsOnline 60Hz GameClient.
    /// This is required for GeneralsOnline data patches to work.
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
            CompatibleGameTypes = new List<GameType> { GameType.ZeroHour },
        };
    }

    /// <summary>
    /// Gets the list of all dependencies for a Generals Online 60Hz variant.
    /// Includes Zero Hour installation and QuickMatch MapPack.
    /// </summary>
    /// <param name="mapPackVersion">The version of the QuickMatch MapPack to depend on.</param>
    /// <returns>List of dependencies for 60Hz variant.</returns>
    public static List<ContentDependency> GetDependenciesFor60Hz(int mapPackVersion = 0)
    {
        return new List<ContentDependency>
        {
            CreateZeroHourDependencyForGeneralsOnline(),
            CreateQuickMatchMapPackDependency(mapPackVersion),
        };
    }

    /// <summary>
    /// Gets the list of dependencies for the GeneralsOnlineGameData data patch.
    /// Includes Zero Hour installation and GeneralsOnline 60Hz GameClient.
    /// </summary>
    /// <param name="clientVersion">The version of the GameClient to depend on.</param>
    /// <returns>List of dependencies for GameData data patch.</returns>
    public static List<ContentDependency> GetDependenciesForGameData(int clientVersion = 0)
    {
        return new List<ContentDependency>
        {
            CreateZeroHourDependencyForGeneralsOnline(),
            CreateGameClient60HzDependency(clientVersion),
        };
    }

    /// <summary>
    /// Gets the dependencies for Generals Online content.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <returns>List of dependencies.</returns>
    public override List<ContentDependency> GetDependencies(ContentManifest manifest)
    {
        var dependencies = new List<ContentDependency>();

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

        // All Generals Online game clients require Zero Hour 1.04 and the QuickMatch MapPack
        if (manifest.ContentType == ContentType.GameClient)
        {
            dependencies.Add(CreateZeroHourDependencyForGeneralsOnline());
            dependencies.Add(CreateQuickMatchMapPackDependency(userVersion));
        }
        else if (manifest.ContentType == ContentType.MapPack)
        {
            // MapPacks only require Zero Hour installation
            dependencies.Add(CreateZeroHourDependencyForGeneralsOnline());
        }
        else if (manifest.ContentType == ContentType.Patch)
        {
            // Data patch requires Zero Hour installation and GeneralsOnline GameClient
            dependencies.Add(CreateZeroHourDependencyForGeneralsOnline());
            dependencies.Add(CreateGameClient60HzDependency(userVersion));
        }

        return dependencies;
    }
}
