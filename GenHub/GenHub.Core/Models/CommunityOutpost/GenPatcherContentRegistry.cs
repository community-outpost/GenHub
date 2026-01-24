using System;
using System.Collections.Generic;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.CommunityOutpost;

/// <summary>
/// Registry that maps GenPatcher 4-character content codes to GenHub content metadata.
/// Based on the dl.dat format from GenPatcher (legi.cc/gp2/dl.dat).
/// </summary>
public static class GenPatcherContentRegistry
{
    /// <summary>
    /// Language code mappings for patch suffixes.
    /// </summary>
    private static readonly Dictionary<char, (string Code, string Name)> LanguageSuffixes = new()
    {
        ['e'] = ("en", "English"),
        ['b'] = ("pt-BR", "Brazilian Portuguese"),
        ['c'] = ("zh", "Chinese"),
        ['d'] = ("de", "German"),
        ['f'] = ("fr", "French"),
        ['i'] = ("it", "Italian"),
        ['k'] = ("ko", "Korean"),
        ['p'] = ("pl", "Polish"),
        ['s'] = ("es", "Spanish"),
        ['2'] = ("de-alt", "German (Alternate)"),
    };

    /// <summary>
    /// Static content metadata for known content codes.
    /// </summary>
    private static readonly Dictionary<string, GenPatcherContentMetadata> KnownContent = new(StringComparer.OrdinalIgnoreCase)
    {
        // Community Patch (TheSuperHackers Build from legi.cc/patch)
        ["community-patch"] = new GenPatcherContentMetadata
        {
            ContentCode = "community-patch",
            DisplayName = "Community Patch (TheSuperHackers Build)",
            Description = "The latest TheSuperHackers patch build for Zero Hour. Includes bug fixes, balance changes, and quality of life improvements.",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.CommunityPatch,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Base Game Files - These are the patched versions (1.08 for Generals, 1.04 for Zero Hour)
        // The GenPatcher dl.dat codes "10gn" and "10zh" represent the latest patched game clients
        ["10gn"] = new GenPatcherContentMetadata
        {
            ContentCode = "10gn",
            DisplayName = "Generals 1.08",
            Description = "Generals game client (Version 1.08) - the latest official patch version",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            Version = "1.08",
            Category = GenPatcherContentCategory.BaseGame,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["10zh"] = new GenPatcherContentMetadata
        {
            ContentCode = "10zh",
            DisplayName = "Zero Hour 1.04",
            Description = "Zero Hour game client (Version 1.04) - the latest official patch version",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Version = "1.04",
            Category = GenPatcherContentCategory.BaseGame,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Control Bar Addons
        ["cbbs"] = new GenPatcherContentMetadata
        {
            ContentCode = "cbbs",
            DisplayName = "Control Bar HD (Base)",
            Description = "Base files for Control Bar HD",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["cben"] = new GenPatcherContentMetadata
        {
            ContentCode = "cben",
            DisplayName = "Control Bar HD (English)",
            Description = "English language pack for Control Bar HD",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["cbpc"] = new GenPatcherContentMetadata
        {
            ContentCode = "cbpc",
            DisplayName = "Control Bar Pro (Common)",
            Description = "Common files for Control Bar Pro (ExiLe/Xezon)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["cbpr"] = new GenPatcherContentMetadata
        {
            ContentCode = "cbpr",
            DisplayName = "Control Bar Pro (ExiLe)",
            Description = "Base files for Control Bar Pro (ExiLe)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["cbpx"] = new GenPatcherContentMetadata
        {
            ContentCode = "cbpx",
            DisplayName = "Control Bar Pro (Xezon)",
            Description = "Base files for Control Bar Pro (Xezon)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.ControlBar,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Camera Modifications
        ["crgn"] = new GenPatcherContentMetadata
        {
            ContentCode = "crgn",
            DisplayName = "Camera Mod - Generals",
            Description = "Camera modification for Generals",
            ContentType = ContentType.Addon,
            TargetGame = GameType.Generals,
            Category = GenPatcherContentCategory.Camera,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["crzh"] = new GenPatcherContentMetadata
        {
            ContentCode = "crzh",
            DisplayName = "Camera Mod - Zero Hour",
            Description = "Camera modification for Zero Hour",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Camera,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["dczh"] = new GenPatcherContentMetadata
        {
            ContentCode = "dczh",
            DisplayName = "D-Control - Zero Hour",
            Description = "D-Control camera for Zero Hour",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Camera,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Hotkeys & WorldBuilder
        ["ewba"] = new GenPatcherContentMetadata
        {
            ContentCode = "ewba",
            DisplayName = "Enhanced World Builder v2.7 by Adriane",
            Description = "Advanced World Builder with new theme",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Tools,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["ewbi"] = new GenPatcherContentMetadata
        {
            ContentCode = "ewbi",
            DisplayName = "Enhanced World Builder v2.2",
            Description = "Standard Enhanced World Builder",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Tools,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["hlde"] = new GenPatcherContentMetadata
        {
            ContentCode = "hlde",
            DisplayName = "Leikeze's Hotkeys Indicators (German)",
            Description = "German version of Leikeze's indicators",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            LanguageCode = "de",
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["hleg"] = new GenPatcherContentMetadata
        {
            ContentCode = "hleg",
            DisplayName = "Legionnaire's Hotkeys",
            Description = "Legionnaire's custom hotkey layout",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            LanguageCode = "en",
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["hlei"] = new GenPatcherContentMetadata
        {
            ContentCode = "hlei",
            DisplayName = "Leikeze's Hotkeys (Icons)",
            Description = "Leikeze's hotkeys with icon support",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            LanguageCode = "en",
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["hlen"] = new GenPatcherContentMetadata
        {
            ContentCode = "hlen",
            DisplayName = "Leikeze's Hotkeys Indicators",
            Description = "Leikeze's hotkey indicators overlay",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            LanguageCode = "en",
            Category = GenPatcherContentCategory.Hotkeys,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Tools
        ["gent"] = new GenPatcherContentMetadata
        {
            ContentCode = "gent",
            DisplayName = "GenTool",
            Description = "GenTool utility for Generals/Zero Hour",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Tools,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["gena"] = new GenPatcherContentMetadata
        {
            ContentCode = "gena",
            DisplayName = "GenAssist",
            Description = "GenAssist helper utility",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Tools,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["genl"] = new GenPatcherContentMetadata
        {
            ContentCode = "genl",
            DisplayName = "GenLauncher",
            Description = "GenLauncher standalone launcher utility",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Tools,
            InstallTarget = ContentInstallTarget.Workspace,
        },

        // Prerequisites - System dependencies (VCRedist, DirectX, etc.)
        ["vc05"] = new GenPatcherContentMetadata
        {
            ContentCode = "vc05",
            DisplayName = "Visual C++ 2005 Redistributable",
            Description = "Microsoft Visual C++ 2005 Redistributable Package",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Prerequisites,
            InstallTarget = ContentInstallTarget.System,
        },
        ["vc08"] = new GenPatcherContentMetadata
        {
            ContentCode = "vc08",
            DisplayName = "Visual C++ 2008 Redistributable",
            Description = "Microsoft Visual C++ 2008 Redistributable Package",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Prerequisites,
            InstallTarget = ContentInstallTarget.System,
        },
        ["vc10"] = new GenPatcherContentMetadata
        {
            ContentCode = "vc10",
            DisplayName = "Visual C++ 2010 Redistributable",
            Description = "Microsoft Visual C++ 2010 Redistributable Package",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Prerequisites,
            InstallTarget = ContentInstallTarget.System,
        },

        // Maps and Missions - These go to user Documents directory
        ["maod"] = new GenPatcherContentMetadata
        {
            ContentCode = "maod",
            DisplayName = "AOD and comp-stomp maps",
            Description = "Maps designed for AOD/Comp-Stomp",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Maps,
            InstallTarget = ContentInstallTarget.UserMapsDirectory,
        },
        ["mmis"] = new GenPatcherContentMetadata
        {
            ContentCode = "mmis",
            DisplayName = "Single player and multiplayer co-op missions",
            Description = "Custom missions campaign",
            ContentType = ContentType.Mission,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Maps,
            InstallTarget = ContentInstallTarget.UserMapsDirectory,
        },
        ["mscr"] = new GenPatcherContentMetadata
        {
            ContentCode = "mscr",
            DisplayName = "Scripted and no-money maps",
            Description = "Maps with heavy scripting or no-money rules",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Maps,
            InstallTarget = ContentInstallTarget.UserMapsDirectory,
        },
        ["mskr"] = new GenPatcherContentMetadata
        {
            ContentCode = "mskr",
            DisplayName = "Skirmish and multiplayer maps",
            Description = "Collection of skirmish and MP maps",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            LanguageCode = "ko",
            Category = GenPatcherContentCategory.Maps,
            InstallTarget = ContentInstallTarget.UserMapsDirectory,
        },

        // Visuals
        ["icon"] = new GenPatcherContentMetadata
        {
            ContentCode = "icon",
            DisplayName = "Icons Pack",
            Description = "Custom icons for the game",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Visuals,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["drtx"] = new GenPatcherContentMetadata
        {
            ContentCode = "drtx",
            DisplayName = "DirectX Textures",
            Description = "High-resolution DirectX texture pack",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Visuals,
            InstallTarget = ContentInstallTarget.Workspace,
        },
        ["unct"] = new GenPatcherContentMetadata
        {
            ContentCode = "unct",
            DisplayName = "Uncut Content",
            Description = "Restored uncut game content",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Visuals,
            InstallTarget = ContentInstallTarget.Workspace,
        },
    };

    /// <summary>
    /// Gets metadata for a content code.
    /// </summary>
    /// <param name="contentCode">The 4-character content code.</param>
    /// <returns>Content metadata, or a dynamically generated one if the code is unknown.</returns>
    public static GenPatcherContentMetadata GetMetadata(string contentCode)
    {
        if (string.IsNullOrEmpty(contentCode))
        {
            return CreateUnknownMetadata(contentCode);
        }

        // Check for known content first
        if (KnownContent.TryGetValue(contentCode.ToLowerInvariant(), out var metadata))
        {
            return metadata;
        }

        // Try to parse as a patch code (e.g., "108e", "104b")
        var patchMetadata = TryParsePatchCode(contentCode);
        if (patchMetadata != null)
        {
            return patchMetadata;
        }

        // Return unknown metadata
        return CreateUnknownMetadata(contentCode);
    }

    /// <summary>
    /// Gets all known content codes.
    /// </summary>
    /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="string"/> where each element is a known content code.</returns>
    public static IEnumerable<string> GetKnownContentCodes()
    {
        return KnownContent.Keys;
    }

    /// <summary>
    /// Checks if a content code is known.
    /// </summary>
    /// <param name="contentCode">The content code to check.</param>
    /// <returns>true if the content code is known; otherwise, false.</returns>
    public static bool IsKnownCode(string contentCode)
    {
        return KnownContent.ContainsKey(contentCode.ToLowerInvariant());
    }

    /// <summary>
    /// Tries to parse a content code as a patch code (e.g., "108e" = Patch 1.08 English).
    /// </summary>
    private static GenPatcherContentMetadata? TryParsePatchCode(string code)
    {
        if (code.Length != 4)
        {
            return null;
        }

        // Pattern: [1][version digit][version digit][language char]
        // e.g., 108e = 1.08 English, 104b = 1.04 Brazilian
        if (code[0] != '1')
        {
            return null;
        }

        // Try to parse the version (positions 1-2)
        var versionPart = code.Substring(1, 2);
        if (!int.TryParse(versionPart, out var versionNumber))
        {
            return null;
        }

        // Get the language suffix (position 3)
        var languageSuffix = code[3];
        if (!LanguageSuffixes.TryGetValue(languageSuffix, out var languageInfo))
        {
            return null;
        }

        // Determine target game based on version
        // 108 = Generals 1.08, 104 = Zero Hour 1.04
        var isGenerals = versionNumber == 8; // 1.08 is Generals

        var targetGame = isGenerals ? GameType.Generals : GameType.ZeroHour;
        var version = $"1.0{versionNumber}";

        return new GenPatcherContentMetadata
        {
            ContentCode = code,
            DisplayName = $"Patch {version} ({languageInfo.Name})",
            Description = $"Official {(isGenerals ? "Generals" : "Zero Hour")} patch {version} - {languageInfo.Name} version",
            ContentType = ContentType.Patch,
            TargetGame = targetGame,
            LanguageCode = languageInfo.Code,
            Version = version,
            Category = GenPatcherContentCategory.OfficialPatch,
            InstallTarget = ContentInstallTarget.Workspace,
        };
    }

    /// <summary>
    /// Creates metadata for an unknown content code.
    /// </summary>
    private static GenPatcherContentMetadata CreateUnknownMetadata(string code)
    {
        return new GenPatcherContentMetadata
        {
            ContentCode = code,
            DisplayName = $"Unknown Content ({code})",
            Description = $"GenPatcher content: {code}",
            ContentType = ContentType.UnknownContentType,
            TargetGame = GameType.ZeroHour,
            Category = GenPatcherContentCategory.Other,
            InstallTarget = ContentInstallTarget.Workspace,
        };
    }
}