using System;
using System.Collections.Generic;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Features.Content.Services.CommunityOutpost.Models;

/// <summary>
/// Builds dependency specifications for GenPatcher content.
/// Centralizes the dependency logic for all Community Outpost content types.
/// </summary>
/// <remarks>
/// <para>
/// Dependencies use DependencyType, CompatibleGameTypes, and MinVersion for semantic matching.
/// The Id field is a reference identifier for display purposes, but actual matching is done
/// via the semantic properties (DependencyType, CompatibleGameTypes, MinVersion).
/// </para>
/// <para>
/// IMPORTANT: Dependencies should specify the game type (Generals/ZeroHour) and version requirement,
/// NOT a specific publisher. Any EA or Steam installation that meets the requirements will work.
/// </para>
/// </remarks>
public static class GenPatcherDependencyBuilder
{
    /// <summary>
    /// Gets the dependencies for a given content code and metadata.
    /// </summary>
    /// <param name="contentCode">The 4-character content code.</param>
    /// <param name="metadata">The content metadata.</param>
    /// <returns>List of dependencies for this content.</returns>
    public static List<ContentDependency> GetDependencies(string contentCode, GenPatcherContentMetadata metadata)
    {
        var dependencies = new List<ContentDependency>();

        // Route to appropriate dependency builder based on category
        switch (metadata.Category)
        {
            case GenPatcherContentCategory.CommunityPatch:
                // Community Patch is a complete game client, requires base game installation
                AddGenericGameDependency(dependencies, metadata);
                break;

            case GenPatcherContentCategory.OfficialPatch:
                AddOfficialPatchDependencies(dependencies, metadata);
                break;

            case GenPatcherContentCategory.BaseGame:
                // Base game files have no dependencies - they are the dependency
                break;

            case GenPatcherContentCategory.ControlBar:
                AddControlBarDependencies(dependencies, metadata);
                break;

            case GenPatcherContentCategory.Camera:
                AddCameraDependencies(dependencies, metadata);
                break;

            case GenPatcherContentCategory.Hotkeys:
                AddHotkeyDependencies(dependencies, metadata);
                break;

            case GenPatcherContentCategory.Tools:
                AddToolDependencies(dependencies, contentCode, metadata);
                break;

            case GenPatcherContentCategory.Maps:
                AddMapDependencies(dependencies, metadata);
                break;

            case GenPatcherContentCategory.Visuals:
                AddVisualDependencies(dependencies, metadata);
                break;

            case GenPatcherContentCategory.Prerequisites:
                // System prerequisites have no in-game dependencies
                break;

            default:
                // Unknown content - add generic game installation dependency
                AddGenericGameDependency(dependencies, metadata);
                break;
        }

        return dependencies;
    }

    /// <summary>
    /// Creates the standard Zero Hour 1.04 game installation dependency.
    /// Used by most content that requires a patched Zero Hour installation.
    /// </summary>
    /// <remarks>
    /// The dependency uses semantic matching via DependencyType and CompatibleGameTypes.
    /// Any Zero Hour 1.04+ installation (EA, Steam, or other) will satisfy this dependency.
    /// </remarks>
    /// <returns>A content dependency for Zero Hour 1.04.</returns>
    public static ContentDependency CreateZeroHour104Dependency()
    {
        return new ContentDependency
        {
            // Semantic ID indicates what this dependency represents
            // Matching is done via DependencyType + CompatibleGameTypes + MinVersion
            Id = ManifestId.Create("1.104.any.gameinstallation.zerohour"),
            Name = GameClientConstants.ZeroHourInstallationDependencyName,
            DependencyType = ContentType.GameInstallation,
            MinVersion = ManifestConstants.ZeroHourManifestVersion, // "1.04"
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = false, // Any publisher's ZH installation will work (EA, Steam, etc.)
            CompatibleGameTypes = new List<GameType> { GameType.ZeroHour },
        };
    }

    /// <summary>
    /// Creates the standard Generals 1.08 game installation dependency.
    /// </summary>
    /// <remarks>
    /// The dependency uses semantic matching via DependencyType and CompatibleGameTypes.
    /// Any Generals 1.08+ installation (EA, Steam, or other) will satisfy this dependency.
    /// </remarks>
    /// <returns>A content dependency for Generals 1.08.</returns>
    public static ContentDependency CreateGenerals108Dependency()
    {
        return new ContentDependency
        {
            Id = ManifestId.Create("1.108.any.gameinstallation.generals"),
            Name = "Generals 1.08 Installation (Required)",
            DependencyType = ContentType.GameInstallation,
            MinVersion = ManifestConstants.GeneralsManifestVersion, // "1.08"
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = false, // Any publisher's Generals installation will work
            CompatibleGameTypes = new List<GameType> { GameType.Generals },
        };
    }

    /// <summary>
    /// Creates a base Zero Hour game installation dependency (any version).
    /// Used for official patches that need the base game without a specific patch version.
    /// </summary>
    /// <returns>A content dependency for base Zero Hour installation.</returns>
    public static ContentDependency CreateBaseZeroHourDependency()
    {
        return new ContentDependency
        {
            Id = ManifestId.Create("1.0.any.gameinstallation.zerohour"),
            Name = "Zero Hour Base Installation (Required)",
            DependencyType = ContentType.GameInstallation,
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = false,
            CompatibleGameTypes = new List<GameType> { GameType.ZeroHour },
        };
    }

    /// <summary>
    /// Creates a base Generals game installation dependency (any version).
    /// Used for official patches that need the base game without a specific patch version.
    /// </summary>
    /// <returns>A content dependency for base Generals installation.</returns>
    public static ContentDependency CreateBaseGeneralsDependency()
    {
        return new ContentDependency
        {
            Id = ManifestId.Create("1.0.any.gameinstallation.generals"),
            Name = "Generals Base Installation (Required)",
            DependencyType = ContentType.GameInstallation,
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = false,
            CompatibleGameTypes = new List<GameType> { GameType.Generals },
        };
    }

    /// <summary>
    /// Creates a dependency on the GenTool addon.
    /// GenTool is required for many advanced features.
    /// </summary>
    /// <returns>A content dependency for GenTool.</returns>
    public static ContentDependency CreateGenToolDependency()
    {
        return new ContentDependency
        {
            Id = ManifestId.Create($"1.0.{CommunityOutpostConstants.PublisherType}.addon.gent"),
            Name = "GenTool (Required)",
            DependencyType = ContentType.Addon,
            InstallBehavior = DependencyInstallBehavior.AutoInstall,
            IsOptional = false,
        };
    }

    /// <summary>
    /// Creates an optional GenTool dependency for enhanced features.
    /// </summary>
    /// <returns>An optional content dependency for GenTool.</returns>
    public static ContentDependency CreateOptionalGenToolDependency()
    {
        return new ContentDependency
        {
            Id = ManifestId.Create($"1.0.{CommunityOutpostConstants.PublisherType}.addon.gent"),
            Name = "GenTool (Recommended)",
            DependencyType = ContentType.Addon,
            InstallBehavior = DependencyInstallBehavior.Suggest,
            IsOptional = true,
        };
    }

    /// <summary>
    /// Gets a list of content codes that conflict with each other.
    /// For example, control bars conflict with other control bars.
    /// </summary>
    /// <param name="contentCode">The content code to check.</param>
    /// <returns>List of conflicting content codes.</returns>
    public static List<string> GetConflictingCodes(string contentCode)
    {
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        return metadata.Category switch
        {
            // Control bars conflict with each other
            GenPatcherContentCategory.ControlBar => new List<string>
            {
                "cbbs", "cben", "cbpc", "cbpr", "cbpx",
            }.FindAll(c => !c.Equals(contentCode, StringComparison.OrdinalIgnoreCase)),

            // Camera mods for the same game conflict
            GenPatcherContentCategory.Camera when metadata.TargetGame == GameType.ZeroHour =>
                new List<string> { "crzh", "dczh" }
                    .FindAll(c => !c.Equals(contentCode, StringComparison.OrdinalIgnoreCase)),

            GenPatcherContentCategory.Camera when metadata.TargetGame == GameType.Generals =>
                new List<string> { "crgn" }
                    .FindAll(c => !c.Equals(contentCode, StringComparison.OrdinalIgnoreCase)),

            // Hotkey configs might conflict
            GenPatcherContentCategory.Hotkeys => new List<string>
            {
                "ewba", "ewbi", "hlde", "hleg", "hlei", "hlen",
            }.FindAll(c => !c.Equals(contentCode, StringComparison.OrdinalIgnoreCase)),

            _ => new List<string>(),
        };
    }

    /// <summary>
    /// Determines if a content type is exclusive (only one can be active at a time).
    /// </summary>
    /// <param name="category">The content category.</param>
    /// <returns>True if only one item of this category can be active.</returns>
    public static bool IsCategoryExclusive(GenPatcherContentCategory category)
    {
        return category switch
        {
            GenPatcherContentCategory.ControlBar => true,
            GenPatcherContentCategory.Camera => true,
            GenPatcherContentCategory.Hotkeys => true,
            _ => false,
        };
    }

    /// <summary>
    /// Adds dependencies for official patches (108e, 104e, etc.).
    /// Official patches require the base game installation (unpatched).
    /// </summary>
    private static void AddOfficialPatchDependencies(
        List<ContentDependency> dependencies,
        GenPatcherContentMetadata metadata)
    {
        // Official patches depend on the base game installation (no patch required)
        if (metadata.TargetGame == GameType.Generals)
        {
            dependencies.Add(CreateBaseGeneralsDependency());
        }
        else
        {
            dependencies.Add(CreateBaseZeroHourDependency());
        }
    }

    /// <summary>
    /// Adds dependencies for control bar addons.
    /// Control bars require Zero Hour 1.04 to work properly.
    /// </summary>
    private static void AddControlBarDependencies(
        List<ContentDependency> dependencies,
        GenPatcherContentMetadata metadata)
    {
        // All control bars need Zero Hour 1.04
        dependencies.Add(CreateZeroHour104Dependency());

        // Control bars benefit from GenTool for better UI integration
        dependencies.Add(CreateOptionalGenToolDependency());

        // Control bars conflict with each other (only one can be active)
        // Note: This is handled via IsExclusive flag
    }

    /// <summary>
    /// Adds dependencies for camera modifications.
    /// Camera mods require the corresponding game installation.
    /// </summary>
    private static void AddCameraDependencies(
        List<ContentDependency> dependencies,
        GenPatcherContentMetadata metadata)
    {
        // Camera mods need the corresponding patched game
        if (metadata.TargetGame == GameType.Generals)
        {
            dependencies.Add(CreateGenerals108Dependency());
        }
        else
        {
            dependencies.Add(CreateZeroHour104Dependency());
        }
    }

    /// <summary>
    /// Adds dependencies for hotkey configurations.
    /// Hotkeys require the patched game installation.
    /// </summary>
    private static void AddHotkeyDependencies(
        List<ContentDependency> dependencies,
        GenPatcherContentMetadata metadata)
    {
        // Hotkeys typically work with Zero Hour
        dependencies.Add(CreateZeroHour104Dependency());
    }

    /// <summary>
    /// Adds dependencies for tools (GenTool, GenLauncher, etc.).
    /// Tools have specific dependencies based on their function.
    /// </summary>
    private static void AddToolDependencies(
        List<ContentDependency> dependencies,
        string contentCode,
        GenPatcherContentMetadata metadata)
    {
        var code = contentCode.ToLowerInvariant();

        switch (code)
        {
            case "gent": // GenTool
                // GenTool requires Zero Hour 1.04 - it hooks into the game
                dependencies.Add(CreateZeroHour104Dependency());

                // GenTool conflicts with GeneralsOnline clients (they have their own GenTool)
                // Add conflict information but don't block - the resolver will handle this
                break;

            case "genl": // GenLauncher
                // GenLauncher is a standalone launcher, requires any game installation
                dependencies.Add(CreateZeroHour104Dependency());
                break;

            case "gena": // GenAssist
                // GenAssist helper utility
                dependencies.Add(CreateZeroHour104Dependency());
                break;

            case "laun": // Generic launcher
                // Launcher component needs a game to launch
                dependencies.Add(CreateZeroHour104Dependency());
                break;

            default:
                // Unknown tool - add generic dependency
                dependencies.Add(CreateZeroHour104Dependency());
                break;
        }
    }

    /// <summary>
    /// Adds dependencies for map packs and missions.
    /// Maps require the patched game to load correctly.
    /// </summary>
    private static void AddMapDependencies(
        List<ContentDependency> dependencies,
        GenPatcherContentMetadata metadata)
    {
        // Maps need the patched game
        dependencies.Add(CreateZeroHour104Dependency());
    }

    /// <summary>
    /// Adds dependencies for visual modifications (icons, textures, etc.).
    /// Visual mods require the base game and often benefit from tools.
    /// </summary>
    private static void AddVisualDependencies(
        List<ContentDependency> dependencies,
        GenPatcherContentMetadata metadata)
    {
        // Visual mods need the patched game
        if (metadata.TargetGame == GameType.Generals)
        {
            dependencies.Add(CreateGenerals108Dependency());
        }
        else
        {
            dependencies.Add(CreateZeroHour104Dependency());
        }
    }

    /// <summary>
    /// Adds a generic game installation dependency for unknown content.
    /// </summary>
    private static void AddGenericGameDependency(
        List<ContentDependency> dependencies,
        GenPatcherContentMetadata metadata)
    {
        if (metadata.TargetGame == GameType.Generals)
        {
            dependencies.Add(CreateGenerals108Dependency());
        }
        else
        {
            dependencies.Add(CreateZeroHour104Dependency());
        }
    }
}
