using GenHub.Core.Constants;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for managing profile resources like icons and covers.
/// </summary>
public class ProfileResourceService(ILogger<ProfileResourceService> logger)
{
    private const string IconsPath = "/Assets/Icons";
    private const string CoversPath = "/Assets/Covers";
    private const string LogosPath = "/Assets/Logos";
    private const string ImagesPath = "/Assets/Images";
    private const string GeneralsGameType = "Generals";
    private const string ZeroHourGameType = "ZeroHour";

    private readonly object _initLock = new();
    private readonly List<ProfileResourceItem> _icons = [];
    private readonly List<ProfileResourceItem> _covers = [];
    private bool _initialized = false;

    /// <summary>
    /// Gets all available icons.
    /// </summary>
    /// <returns>A read-only list of all icons.</returns>
    public IReadOnlyList<ProfileResourceItem> GetAvailableIcons()
    {
        EnsureInitialized();
        return _icons.AsReadOnly();
    }

    /// <summary>
    /// Gets all available covers.
    /// </summary>
    /// <returns>A read-only list of all covers.</returns>
    public IReadOnlyList<ProfileResourceItem> GetAvailableCovers()
    {
        EnsureInitialized();
        return _covers.AsReadOnly();
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
    /// Ensures resources are initialized (thread-safe).
    /// </summary>
    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            lock (_initLock)
            {
                if (!_initialized)
                {
                    LoadBuiltInResources();
                    _initialized = true;
                    logger.LogInformation(
                        "ProfileResourceService initialized with {IconCount} icons and {CoverCount} covers",
                        _icons.Count,
                        _covers.Count);
                }
            }
        }
    }

    /// <summary>
    /// Loads built-in icons and covers from Assets.
    /// </summary>
    private void LoadBuiltInResources()
    {
        // Load icons
        var iconFiles = new (string FileName, string DisplayName, string? GameType)[]
        {
            ("generals-icon.png", "Generals Icon", GeneralsGameType),
            ("zerohour-icon.png", "Zero Hour Icon", ZeroHourGameType),
            ("generalshub-icon.png", "GenHub Icon", null),
            ("steam-icon.png", "Steam Icon", null),
            ("eaapp-icon.png", "EA App Icon", null),
            ("origin-icon.png", "Origin Icon", null),
            ("genpatcher-icon.png", "GenPatcher Icon", null),
            ("modbuilder-icon.png", "ModBuilder Icon", null),
            ("publisherstudio-icon.png", "Publisher Studio Icon", null),
            ("hotkeyseditor-icon.png", "Hotkeys Editor Icon", null),
            ("mapmanager-icon.png", "Map Manager Icon", null),
            ("replaymanager-icon.png", "Replay Manager Icon", null),
            ("gameprofilesettings-icon.png", "Profile Settings Icon", null),
            ("settings-icon.png", "Settings Icon", null),
            ("Factions/china.png", "China Faction Icon", null),
            ("Factions/gla.png", "GLA Faction Icon", null),
            ("Factions/usa.png", "USA Faction Icon", null),
        };

        foreach (var (fileName, displayName, gameType) in iconFiles)
        {
            _icons.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{IconsPath}/{fileName}",
                DisplayName = displayName,
                IsBuiltIn = true,
                GameType = gameType,
            });
        }

        // Load logos as additional icons
        var logoFiles = new[]
        {
            ("generalshub-logo.png", "GenHub Logo"),
            ("generalsonline-logo.png", "Generals Online Logo"),
            ("thesuperhackers-logo.png", "The Super Hackers Logo"),
            ("cnclabs-logo.png", "CNC Labs Logo"),
            ("communityoutpost-logo.png", "Community Outpost Logo"),
            ("moddb-logo.png", "ModDB Logo"),
            ("genlauncher-logo.png", "GenLauncher Logo"),
            ("genpatcher-logo.png", "GenPatcher Logo"),
            ("dominator-logo.png", "Dominator Logo"),
            ("aodmaps-logo.png", "AoD Maps Logo"),
            ("github-logo.png", "GitHub Logo"),
        };

        foreach (var (fileName, displayName) in logoFiles)
        {
            _icons.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{LogosPath}/{fileName}",
                DisplayName = displayName,
                IsBuiltIn = true,
                GameType = null,
            });
        }

        // Load game images as icons
        var imageFiles = new (string FileName, string DisplayName, string? GameType)[]
        {
            ("zero-hour-logo.png", "Zero Hour Logo", ZeroHourGameType),
            ("generals-logo.png", "Generals Logo", GeneralsGameType),
        };

        foreach (var (fileName, displayName, gameType) in imageFiles)
        {
            _icons.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{ImagesPath}/{fileName}",
                DisplayName = displayName,
                IsBuiltIn = true,
                GameType = gameType,
            });
        }

        // Load covers
        var coverFiles = new[]
        {
            ("generals-cover.png", "Generals Cover", "Generals"),
            ("generals-cover-2.png", "Generals Cover (Alt)", "Generals"),
            ("zerohour-cover.png", "Zero Hour Cover", "ZeroHour"),
        };

        foreach (var (fileName, displayName, gameType) in coverFiles)
        {
            _covers.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{CoversPath}/{fileName}",
                DisplayName = displayName,
                IsBuiltIn = true,
                GameType = gameType,
            });
        }

        // Load faction covers
        var factionCoverFiles = new (string, string, string?)[]
        {
            (UriConstants.ChinaCoverFilename, "China Cover", null),
            (UriConstants.GlaCoverFilename, "GLA Cover", null),
            (UriConstants.UsaCoverFilename, "USA Cover", null),
        };

        foreach (var (fileName, displayName, gameType) in factionCoverFiles)
        {
            _covers.Add(new ProfileResourceItem
            {
                Id = Path.GetFileNameWithoutExtension(fileName),
                Path = $"{CoversPath}/{fileName}",
                DisplayName = displayName,
                IsBuiltIn = true,
                GameType = gameType,
            });
        }

        // Add default background cover
        _covers.Add(new ProfileResourceItem
        {
            Id = "background",
            Path = "/Assets/background.jpg",
            DisplayName = "Default Background",
            IsBuiltIn = true,
            GameType = null,
        });

        logger.LogDebug(
            "Loaded {IconCount} built-in icons and {CoverCount} built-in covers",
            _icons.Count,
            _covers.Count);
    }
}
