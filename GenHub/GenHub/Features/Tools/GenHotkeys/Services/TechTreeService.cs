using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.GenHotkeys;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Service for loading C&amp;C Generals and Zero Hour tech tree structures, icons, and localized hotkey strings.
/// </summary>
public class TechTreeService(ILogger<TechTreeService> logger) : ITechTreeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConcurrentDictionary<GameType, IReadOnlyList<HotkeyFaction>> _cache = new();
    private readonly ConcurrentDictionary<string, byte[]?> _iconCache = new();

    /// <inheritdoc />
    public async Task<IReadOnlyList<HotkeyFaction>> LoadTechTreeAsync(
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(gameType, out var cached))
        {
            return cached;
        }

        var relativePath = gameType == GameType.Generals
            ? GenHotkeysConstants.TechTreeGenerals
            : GenHotkeysConstants.TechTreeGeneralsZh;

        var jsonString = await LoadAssetStringAsync(relativePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(jsonString))
        {
            logger.LogWarning("Failed to load tech tree JSON for {GameType} from {Path}", gameType, relativePath);
            return [];
        }

        var root = JsonSerializer.Deserialize<TechTreeRoot>(jsonString, JsonOptions);
        if (root?.TechTree == null)
        {
            return [];
        }

        // Load reference CSF for default strings and hotkeys
        var refCsf = LoadReferenceCsf(gameType);

        var factions = new List<HotkeyFaction>();
        foreach (var factionJson in root.TechTree)
        {
            var faction = new HotkeyFaction
            {
                ShortName = factionJson.ShortName,
                DisplayName = factionJson.DisplayName,
                DisplayNameDescription = factionJson.DisplayNameDescription,
            };

            AddGameObjects(faction.GameObjects, factionJson.Buildings, HotkeyCategory.Buildings, refCsf);
            AddGameObjects(faction.GameObjects, factionJson.Infantry, HotkeyCategory.Infantry, refCsf);
            AddGameObjects(faction.GameObjects, factionJson.Vehicles, HotkeyCategory.Vehicles, refCsf);
            AddGameObjects(faction.GameObjects, factionJson.Aircrafts, HotkeyCategory.Aircrafts, refCsf);

            factions.Add(faction);
        }

        _cache[gameType] = factions;
        return factions;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetIconBytesAsync(
        string iconName,
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return null;
        }

        var profileDir = gameType == GameType.Generals ? "Generals" : "GeneralsZH";
        var cacheKey = $"{profileDir}/{iconName}";

        if (_iconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var extensions = new[] { ".webp", ".png" };
        foreach (var ext in extensions)
        {
            var relativePath = $"Profiles/{profileDir}/Icons/{iconName}{ext}";
            var stream = TryOpenAssetStream(relativePath);
            if (stream != null)
            {
                using (stream)
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms, cancellationToken);
                    var data = ms.ToArray();
                    _iconCache[cacheKey] = data;
                    return data;
                }
            }
        }

        _iconCache[cacheKey] = null;
        return null;
    }

    private static void AddGameObjects(
        List<HotkeyGameObject> targetList,
        List<TechTreeGameObjectJson> sourceList,
        HotkeyCategory category,
        CsfFile? refCsf)
    {
        foreach (var objJson in sourceList)
        {
            targetList.Add(CreateGameObject(objJson, category, refCsf));
        }
    }

    private static HotkeyGameObject CreateGameObject(
        TechTreeGameObjectJson objJson,
        HotkeyCategory category,
        CsfFile? refCsf)
    {
        var cleanDisplayName = objJson.Name;
        if (refCsf != null && !string.IsNullOrEmpty(objJson.IngameName))
        {
            var localized = refCsf.GetString(objJson.IngameName);
            if (!string.IsNullOrWhiteSpace(localized))
            {
                cleanDisplayName = CsfFile.StripHotkey(localized);
            }
        }

        var gameObj = new HotkeyGameObject
        {
            Name = objJson.Name,
            IngameName = objJson.IngameName,
            DisplayName = cleanDisplayName,
            Category = category,
            IconName = objJson.Name,
        };

        foreach (var layoutJson in objJson.KeyboardLayouts)
        {
            var layout = new List<HotkeyAction>();
            foreach (var actJson in layoutJson)
            {
                layout.Add(CreateAction(actJson, refCsf));
            }

            gameObj.KeyboardLayouts.Add(layout);
        }

        return gameObj;
    }

    private static HotkeyAction CreateAction(TechTreeActionJson actJson, CsfFile? refCsf)
    {
        var actDisplayName = actJson.IconName;
        char? defaultHk = null;

        if (refCsf != null && !string.IsNullOrEmpty(actJson.HotkeyString))
        {
            var csfVal = refCsf.GetString(actJson.HotkeyString);
            if (!string.IsNullOrWhiteSpace(csfVal))
            {
                actDisplayName = CsfFile.StripHotkey(csfVal);
                defaultHk = CsfFile.ExtractHotkey(csfVal);
            }
        }

        return new HotkeyAction
        {
            IconName = actJson.IconName,
            HotkeyString = actJson.HotkeyString,
            DisplayName = actDisplayName,
            DefaultHotkey = defaultHk,
            Hotkey = defaultHk,
        };
    }

    private static async Task<string?> LoadAssetStringAsync(string relativePath, CancellationToken cancellationToken)
    {
        var stream = TryOpenAssetStream(relativePath);
        if (stream == null)
        {
            return null;
        }

        using (stream)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }

    private static Stream? TryOpenAssetStream(string relativePath)
    {
        // 1. Try Avalonia resource loader
        try
        {
            var uri = new Uri($"avares://GenHub/Assets/GenHotkeys/{relativePath.Replace('\\', '/')}");
            if (AssetLoader.Exists(uri))
            {
                return AssetLoader.Open(uri);
            }
        }
        catch
        {
            // Ignore and fall back to filesystem
        }

        // 2. Try AppContext.BaseDirectory
        var fileOnDisk = Path.Combine(AppContext.BaseDirectory, "Assets", "GenHotkeys", relativePath);
        if (File.Exists(fileOnDisk))
        {
            return File.OpenRead(fileOnDisk);
        }

        // 3. Try relative to solution directory during development
        var devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "GenHotkeys", relativePath);
        if (File.Exists(devPath))
        {
            return File.OpenRead(devPath);
        }

        return null;
    }

    private CsfFile? LoadReferenceCsf(GameType gameType)
    {
        try
        {
            var stream = TryOpenAssetStream(GenHotkeysConstants.PresetsLeikezeEn);
            if (stream != null)
            {
                using (stream)
                {
                    return CsfFile.Load(stream);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load reference CSF for {GameType}", gameType);
        }

        return null;
    }
}
