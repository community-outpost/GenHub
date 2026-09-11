using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.GenHotkeys;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Service for saving, loading, deleting, and managing persistent user hotkey profiles and presets.
/// </summary>
public class HotkeyProfileStorageService(
    IAppConfiguration appConfig,
    ILogger<HotkeyProfileStorageService> logger) : IHotkeyProfileStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private string ProfilesDirectory =>
        Path.Combine(appConfig.GetConfiguredDataPath(), GenHotkeysConstants.HotkeysStorageDirectory);

    /// <inheritdoc />
    public async Task<IReadOnlyList<HotkeyProfile>> GetProfilesAsync(
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        var profiles = new List<HotkeyProfile>();
        var files = Directory.GetFiles(ProfilesDirectory, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var profile = JsonSerializer.Deserialize<HotkeyProfile>(json, JsonOptions);
                if (profile is { } p && p.TargetGame == gameType)
                {
                    profiles.Add(p);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize hotkey profile from {Path}", file);
            }
        }

        if (profiles.Count == 0)
        {
            // Seed default profile for this game
            var defaultProfile = CreateDefaultProfile(gameType);
            await SaveProfileAsync(defaultProfile, cancellationToken);
            profiles.Add(defaultProfile);
        }

        return profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<HotkeyProfile?> GetProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var filePath = GetSafeProfilePath(profileId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<HotkeyProfile>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read hotkey profile {Id} from {Path}", profileId, filePath);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<HotkeyProfile> SaveProfileAsync(
        HotkeyProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);
        EnsureDirectory();

        profile.UpdatedAt = DateTime.UtcNow;
        var filePath = GetSafeProfilePath(profile.Id);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        logger.LogInformation("Saved hotkey profile '{Name}' ({Id})", profile.Name, profile.Id);

        return profile;
    }

    /// <inheritdoc />
    public Task<bool> DeleteProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var filePath = GetSafeProfilePath(profileId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            logger.LogInformation("Deleted hotkey profile {Id}", profileId);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<HotkeyProfile> LoadPresetAsync(
        string presetName,
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var profile = new HotkeyProfile
                {
                    Name = $"{presetName} Preset",
                    BasePreset = presetName,
                    TargetGame = gameType,
                    OverlayEnabled = true,
                    OverlayCorner = OverlayCorner.TopLeft,
                };

                if (string.Equals(presetName, GenHotkeysConstants.PresetVanilla, StringComparison.OrdinalIgnoreCase) ||
                    presetName.Contains("Default", StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }

                var assetPath = presetName.Contains(GenHotkeysConstants.PresetLegionnaire, StringComparison.OrdinalIgnoreCase)
                    ? GenHotkeysConstants.PresetsLegionnaireRu
                    : GenHotkeysConstants.PresetsLeikezeEn;

                var stream = TryOpenAssetStream(assetPath);
                if (stream == null)
                {
                    logger.LogWarning("Preset file not found: {Path}", assetPath);
                    return profile;
                }

                using (stream)
                {
                    var csf = CsfFile.Load(stream);
                    foreach (var (label, value) in csf.Strings)
                    {
                        if (label.StartsWith(GenHotkeysConstants.CsfControlBarPrefix, StringComparison.OrdinalIgnoreCase) ||
                            label.StartsWith(GenHotkeysConstants.CsfCommandPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var hk = CsfFile.ExtractHotkey(value);
                            if (hk.HasValue)
                            {
                                profile.KeyMappings[label] = hk.Value;
                            }
                        }
                    }
                }

                return profile;
            },
            cancellationToken);
    }

    private static HotkeyProfile CreateDefaultProfile(GameType gameType)
    {
        var gameTag = gameType == GameType.Generals ? "Generals" : "Zero Hour";
        return new HotkeyProfile
        {
            Name = $"Default ({gameTag})",
            BasePreset = GenHotkeysConstants.PresetVanilla,
            TargetGame = gameType,
            OverlayEnabled = true,
            OverlayCorner = OverlayCorner.TopLeft,
        };
    }

    private static Stream? TryOpenAssetStream(string relativePath)
    {
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
            // Fall back
        }

        var fileOnDisk = Path.Combine(AppContext.BaseDirectory, "Assets", "GenHotkeys", relativePath);
        if (File.Exists(fileOnDisk))
        {
            return File.OpenRead(fileOnDisk);
        }

        var searchRoots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "GenHotkeys", relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GenHub", "Assets", "GenHotkeys", relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GenHub", "Assets", "GenHotkeys", relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GenHub", "GenHub", "Assets", "GenHotkeys", relativePath),
        };

        var match = searchRoots.FirstOrDefault(File.Exists);
        return match != null ? File.OpenRead(match) : null;
    }

    private string GetSafeProfilePath(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) ||
            profileId.Contains("..", StringComparison.Ordinal) ||
            profileId.Contains('/') ||
            profileId.Contains('\\') ||
            profileId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"Invalid profile identifier: '{profileId}'", nameof(profileId));
        }

        var fileName = $"{profileId}.json";
        var fullPath = Path.GetFullPath(Path.Combine(ProfilesDirectory, fileName));
        var dirPath = Path.GetFullPath(ProfilesDirectory);

        if (!fullPath.StartsWith(dirPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Path traversal detected.", nameof(profileId));
        }

        return fullPath;
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(ProfilesDirectory))
        {
            Directory.CreateDirectory(ProfilesDirectory);
        }
    }
}
