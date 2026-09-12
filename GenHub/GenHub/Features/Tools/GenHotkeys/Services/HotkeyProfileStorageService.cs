using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.GenHotkeys;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Service for persisting and managing user hotkey profiles and presets.
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
                profile?.NormalizeComparers();
                if (profile is { } p && p.TargetGame == gameType)
                {
                    profiles.Add(p);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
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
            var profile = JsonSerializer.Deserialize<HotkeyProfile>(json, JsonOptions);
            profile?.NormalizeComparers();
            return profile;
        }
        catch (OperationCanceledException)
        {
            throw;
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
            try
            {
                File.Delete(filePath);
                logger.LogInformation("Deleted hotkey profile {Id}", profileId);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete hotkey profile {Id}", profileId);
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<HotkeyProfile> LoadPresetAsync(
        string presetName,
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetName);

        var profile = new HotkeyProfile
        {
            Name = $"{presetName} ({GenHotkeysConstants.GetGameDisplayName(gameType)})",
            BasePreset = presetName,
            TargetGame = gameType,
            OverlayEnabled = true,
            OverlayCorner = OverlayCorner.TopLeft,
        };

        // Determine preset CSF asset
        var presetCsfPath = presetName.Equals(GenHotkeysConstants.PresetLegionnaire, StringComparison.OrdinalIgnoreCase)
            ? GenHotkeysConstants.PresetsLegionnaireRu
            : GenHotkeysConstants.PresetsLeikezeEn;

        return await Task.Run(
            () =>
            {
                using var stream = GenHotkeysAssetLoader.TryOpenAssetStream(presetCsfPath);
                if (stream != null)
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
        var gameDisplayName = GenHotkeysConstants.GetGameDisplayName(gameType);
        return new HotkeyProfile
        {
            Name = $"Default ({gameDisplayName})",
            BasePreset = GenHotkeysConstants.PresetVanilla,
            TargetGame = gameType,
            OverlayEnabled = true,
            OverlayCorner = OverlayCorner.TopLeft,
        };
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
