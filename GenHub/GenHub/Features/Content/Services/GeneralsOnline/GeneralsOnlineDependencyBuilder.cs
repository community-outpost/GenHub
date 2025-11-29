using System.Collections.Generic;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Services.Dependencies;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Builds dependency specifications for Generals Online content.
/// Generals Online game clients require a base Zero Hour installation.
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
            MinVersion = ManifestConstants.ZeroHourManifestVersion, // "1.04"
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = false, // Any publisher's ZH installation will work
            CompatibleGameTypes = new List<GameType> { GameType.ZeroHour },
        };
    }

    /// <summary>
    /// Gets the list of all dependencies for a Generals Online 30Hz variant.
    /// </summary>
    /// <returns>List of dependencies for 30Hz variant.</returns>
    public static List<ContentDependency> GetDependenciesFor30Hz()
    {
        return new List<ContentDependency>
        {
            CreateZeroHourDependencyForGeneralsOnline(),
        };
    }

    /// <summary>
    /// Gets the list of all dependencies for a Generals Online 60Hz variant.
    /// </summary>
    /// <returns>List of dependencies for 60Hz variant.</returns>
    public static List<ContentDependency> GetDependenciesFor60Hz()
    {
        // 60Hz has the same dependencies as 30Hz
        return new List<ContentDependency>
        {
            CreateZeroHourDependencyForGeneralsOnline(),
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

        // All Generals Online game clients require Zero Hour 1.04
        if (manifest.ContentType == ContentType.GameClient)
        {
            dependencies.Add(CreateZeroHour104Dependency());
        }

        return dependencies;
    }
}
