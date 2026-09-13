using System.Collections.Generic;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Services.Dependencies;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Builds dependency specifications for TheSuperHackers content.
/// TheSuperHackers game clients require the corresponding base game installation
/// (Zero Hour or Generals depending on the variant).
/// </summary>
public class SuperHackersDependencyBuilder : BaseDependencyBuilder
{
    /// <summary>
    /// Creates a dependency on Zero Hour 1.04 specifically for TheSuperHackers.
    /// </summary>
    /// <returns>A content dependency for Zero Hour 1.04 installation.</returns>
    public static ContentDependency CreateZeroHourDependencyForSuperHackers()
    {
        return new ContentDependency
        {
            // This is a type-only foundation requirement. The profile service supplies the
            // concrete installation manifest that matches the user's installed game.
            Id = ManifestId.Create(ManifestConstants.ZeroHourFoundationDependencyId),
            Name = GameClientConstants.ZeroHourInstallationDependencyName,
            DependencyType = ContentType.GameInstallation,
            MinVersion = ManifestConstants.ZeroHourManifestVersion, // "1.04"
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = false, // Any publisher's ZH installation will work
            CompatibleGameTypes = [GameType.ZeroHour],
        };
    }

    /// <summary>
    /// Creates a dependency on Generals 1.08 specifically for TheSuperHackers.
    /// </summary>
    /// <returns>A content dependency for Generals 1.08 installation.</returns>
    public static ContentDependency CreateGeneralsDependencyForSuperHackers()
    {
        return new ContentDependency
        {
            // This is a type-only foundation requirement. The profile service supplies the
            // concrete installation manifest that matches the user's installed game.
            Id = ManifestId.Create(ManifestConstants.GeneralsFoundationDependencyId),
            Name = "Generals 1.08 (Required)",
            DependencyType = ContentType.GameInstallation,
            MinVersion = ManifestConstants.GeneralsManifestVersion, // "1.08"
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = false, // Any publisher's Generals installation will work
            CompatibleGameTypes = [GameType.Generals],
        };
    }

    /// <summary>
    /// Gets the list of dependencies for a TheSuperHackers Zero Hour variant.
    /// </summary>
    /// <returns>List of dependencies for Zero Hour variant.</returns>
    public static List<ContentDependency> GetDependenciesForZeroHour()
    {
        return
        [
            CreateZeroHourDependencyForSuperHackers(),
        ];
    }

    /// <summary>
    /// Gets the list of dependencies for a TheSuperHackers Generals variant.
    /// </summary>
    /// <returns>List of dependencies for Generals variant.</returns>
    public static List<ContentDependency> GetDependenciesForGenerals()
    {
        return
        [
            CreateGeneralsDependencyForSuperHackers(),
        ];
    }

    /// <summary>
    /// Gets dependencies based on game type.
    /// </summary>
    /// <param name="gameType">The target game type.</param>
    /// <returns>List of dependencies for the specified game type.</returns>
    public static List<ContentDependency> GetDependenciesForGameType(GameType gameType)
    {
        return gameType switch
        {
            GameType.ZeroHour => GetDependenciesForZeroHour(),
            GameType.Generals => GetDependenciesForGenerals(),
            _ => [],
        };
    }

    /// <summary>
    /// Gets the dependencies for TheSuperHackers content.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <returns>List of dependencies.</returns>
    public override List<ContentDependency> GetDependencies(ContentManifest manifest)
    {
        // All TheSuperHackers game clients require the corresponding game installation
        if (manifest.ContentType == ContentType.GameClient)
        {
            return GetDependenciesForGameType(manifest.TargetGame);
        }

        return [];
    }
}
