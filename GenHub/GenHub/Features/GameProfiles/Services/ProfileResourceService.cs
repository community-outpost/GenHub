using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for managing profile resources like icons and covers.
/// </summary>
public class ProfileResourceService(ILogger<ProfileResourceService> logger, ILocalizationService localizationService)
{
    private const string IconsPath = "/Assets/Icons";
    private const string CoversPath = "/Assets/Covers";
    private const string LogosPath = "/Assets/Logos";
    private const string ImagesPath = "/Assets/Images";
    private const string GeneralsGameType = "Generals";
    private const string ZeroHourGameType = "ZeroHour";
    private const string IconFormatKey = "GameProfiles.Resources.IconFormat";
    private const string LogoFormatKey = "GameProfiles.Resources.LogoFormat";
    private const string CoverFormatKey = "GameProfiles.Resources.CoverFormat";
    private const string CoverAltFormatKey = "GameProfiles.Resources.CoverAltFormat";
    private const string DefaultBackgroundKey = "GameProfiles.Resources.DefaultBackground";

    private readonly object _initLock = new();
    private IReadOnlyList<ProfileResourceItem> _icons = [];
    private IReadOnlyList<ProfileResourceItem> _covers = [];
    private bool _initialized = false;
    private string? _resolvedCultureName;

    /// <summary>
    /// Gets all available icons.
    /// </summary>
    /// <returns>A read-only list of all icons.</returns>
    public IReadOnlyList<ProfileResourceItem> GetAvailableIcons()
    {
        EnsureInitialized();
        return _icons;
    }

    /// <summary>
    /// Gets all available covers.
    /// </summary>
    /// <returns>A read-only list of all covers.</returns>
    public IReadOnlyList<ProfileResourceItem> GetAvailableCovers()
    {
        EnsureInitialized();
        return _covers;
    }

    /// <summary>
    /// Gets icons filtered by game type.
    /// </summary>
    /// <param name="gameType">The game type to filter by.</param>
    /// <returns>A read-only list of icons for the specified game type.</returns>
    public IReadOnlyList<ProfileResourceItem> GetIconsForGameType(string? gameType)
    {
        EnsureInitialized();
        return _icons.Where(i => i.GameType == null || i.GameType == gameType).ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets covers filtered by game type.
    /// </summary>
    /// <param name="gameType">The game type to filter by.</param>
    /// <returns>A read-only list of covers for the specified game type.</returns>
    public IReadOnlyList<ProfileResourceItem> GetCoversForGameType(string? gameType)
    {
        EnsureInitialized();
        return _covers.Where(c => c.GameType == null || c.GameType == gameType).ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets the default icon path for a game type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <returns>The default icon path for the game type.</returns>
    public string GetDefaultIconPath(string gameType)
    {
        EnsureInitialized();
        var icon = _icons.FirstOrDefault(i => i.GameType == gameType);
        return icon?.Path ?? $"{IconsPath}/generalshub-icon.png";
    }

    /// <summary>
    /// Gets the default cover path for a game type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <returns>The default cover path for the game type.</returns>
    public string GetDefaultCoverPath(string gameType)
    {
        EnsureInitialized();
        var cover = _covers.FirstOrDefault(c => c.GameType == gameType);
        return cover?.Path ?? $"{CoversPath}/generals-cover-2.png";
    }

    /// <summary>
    /// Ensures resources are initialized (thread-safe), rebuilding display names when the UI culture changed.
    /// Rebuilds publish a new immutable snapshot so readers never observe a partially rebuilt collection.
    /// </summary>
    private void EnsureInitialized()
    {
        var cultureName = localizationService.CurrentCulture.Name;
        if (!_initialized || !string.Equals(_resolvedCultureName, cultureName, StringComparison.Ordinal))
        {
            lock (_initLock)
            {
                cultureName = localizationService.CurrentCulture.Name;
                if (!_initialized || !string.Equals(_resolvedCultureName, cultureName, StringComparison.Ordinal))
                {
                    var (icons, covers) = LoadBuiltInResources();
                    _icons = icons;
                    _covers = covers;
                    _initialized = true;
                    _resolvedCultureName = cultureName;
                    logger.LogInformation(
                        "ProfileResourceService initialized with {IconCount} icons and {CoverCount} covers",
                        icons.Count,
                        covers.Count);
                }
            }
        }
    }

    /// <summary>
    /// Loads built-in icons and covers from Assets.
    /// Display names resolve through localized format keys so pickers localize with the rest of the UI.
    /// </summary>
    /// <returns>The loaded icons and covers.</returns>
    private (IReadOnlyList<ProfileResourceItem> Icons, IReadOnlyList<ProfileResourceItem> Covers) LoadBuiltInResources()
    {
        var icons = new List<ProfileResourceItem>();
        var covers = new List<ProfileResourceItem>();

        // Load icons
        var iconFiles = new (string FileName, string BaseName, string? GameType)[]
        {
            ("generals-icon.png", GeneralsGameType, GeneralsGameType),
            ("zerohour-icon.png", ZeroHourGameType, ZeroHourGameType),
            ("generalshub-icon.png", "GenHub", null),
            ("steam-icon.png", "Steam", null),
            ("eaapp-icon.png", "EA App", null),
            ("origin-icon.png", "Origin", null),
            ("genpatcher-icon.png", "GenPatcher", null),
            ("modbuilder-icon.png", "ModBuilder", null),
            ("publisherstudio-icon.png", "Publisher Studio", null),
            ("hotkeyseditor-icon.png", "Hotkeys Editor", null),
            ("mapmanager-icon.png", "Map Manager", null),
            ("replaymanager-icon.png", "Replay Manager", null),
            ("gameprofilesettings-icon.png", "Profile Settings", null),
            ("settings-icon.png", "Settings", null),
            ("Factions/china.png", "China Faction", null),
            ("Factions/gla.png", "GLA Faction", null),
            ("Factions/usa.png", "USA Faction", null),
        };

        foreach (var (fileName, baseName, gameType) in iconFiles)
        {
            icons.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{IconsPath}/{fileName}",
                DisplayName = localizationService.GetString(IconFormatKey, baseName),
                IsBuiltIn = true,
                GameType = gameType,
            });
        }

        // Load logos as additional icons
        var logoFiles = new[]
        {
            ("generalshub-logo.png", "GenHub"),
            ("generalsonline-logo.png", "Generals Online"),
            ("thesuperhackers-logo.png", "The Super Hackers"),
            ("cnclabs-logo.png", "CNC Labs"),
            ("communityoutpost-logo.png", "Community Outpost"),
            ("moddb-logo.png", "ModDB"),
            ("genlauncher-logo.png", "GenLauncher"),
            ("genpatcher-logo.png", "GenPatcher"),
            ("dominator-logo.png", "Dominator"),
            ("aodmaps-logo.png", "AoD Maps"),
            ("github-logo.png", "GitHub"),
        };

        foreach (var (fileName, baseName) in logoFiles)
        {
            icons.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{LogosPath}/{fileName}",
                DisplayName = localizationService.GetString(LogoFormatKey, baseName),
                IsBuiltIn = true,
                GameType = null,
            });
        }

        // Load game images as icons
        var imageFiles = new (string FileName, string BaseName, string? GameType)[]
        {
            ("zero-hour-logo.png", ZeroHourGameType, ZeroHourGameType),
            ("generals-logo.png", GeneralsGameType, GeneralsGameType),
        };

        foreach (var (fileName, baseName, gameType) in imageFiles)
        {
            icons.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{ImagesPath}/{fileName}",
                DisplayName = localizationService.GetString(LogoFormatKey, baseName),
                IsBuiltIn = true,
                GameType = gameType,
            });
        }

        // Load covers
        var coverFiles = new[]
        {
            ("generals-cover.png", GeneralsGameType, CoverFormatKey, GeneralsGameType),
            ("generals-cover-2.png", GeneralsGameType, CoverAltFormatKey, GeneralsGameType),
            ("zerohour-cover.png", ZeroHourGameType, CoverFormatKey, ZeroHourGameType),
        };

        foreach (var (fileName, baseName, formatKey, gameType) in coverFiles)
        {
            covers.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{CoversPath}/{fileName}",
                DisplayName = localizationService.GetString(formatKey, baseName),
                IsBuiltIn = true,
                GameType = gameType,
            });
        }

        // Load faction covers
        var factionCoverFiles = new (string FileName, string BaseName, string? GameType)[]
        {
            (UriConstants.ChinaCoverFilename, "China", null),
            (UriConstants.GlaCoverFilename, "GLA", null),
            (UriConstants.UsaCoverFilename, "USA", null),
        };

        foreach (var (fileName, baseName, gameType) in factionCoverFiles)
        {
            covers.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{CoversPath}/{fileName}",
                DisplayName = localizationService.GetString(CoverFormatKey, baseName),
                IsBuiltIn = true,
                GameType = gameType,
            });
        }

        // Add default background cover
        covers.Add(new ProfileResourceItem
        {
            Id = "background",
            Path = "/Assets/background.jpg",
            DisplayName = localizationService.GetString(DefaultBackgroundKey),
            IsBuiltIn = true,
            GameType = null,
        });

        logger.LogDebug(
            "Loaded {IconCount} built-in icons and {CoverCount} built-in covers",
            icons.Count,
            covers.Count);
        return (icons, covers);
    }
}
