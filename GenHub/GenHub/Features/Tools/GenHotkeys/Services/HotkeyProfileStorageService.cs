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
/// Service for loading, persisting, and managing hotkey profiles and presets.
/// </summary>
public class HotkeyProfileStorageService(
    IAppConfiguration appConfig,
    ILogger<HotkeyProfileStorageService> logger) : IHotkeyProfileStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _profilesDirectory = Path.Combine(appConfig.GetConfiguredDataPath(), GenHotkeysConstants.HotkeysStorageDirectory);

    /// <inheritdoc />
    public async Task<IReadOnlyList<HotkeyProfile>> GetProfilesAsync(
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectory();

        var profiles = new List<HotkeyProfile>();
        var files = Directory.GetFiles(_profilesDirectory, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var profile = JsonSerializer.Deserialize<HotkeyProfile>(json);
                if (profile != null && profile.TargetGame == gameType)
                {
                    profiles.Add(profile);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read hotkey profile from {File}", file);
            }
        }

        if (profiles.Count == 0)
        {
            var defaultProfile = CreateDefaultProfile(gameType);
            await SaveProfileAsync(defaultProfile, cancellationToken);
            profiles.Add(defaultProfile);
        }

        return profiles.OrderBy(p => p.Name).ToList();
    }

    /// <inheritdoc />
    public async Task<HotkeyProfile> SaveProfileAsync(
        HotkeyProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
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
        var profile = new HotkeyProfile
        {
            Name = $"{presetName} ({gameType})",
            TargetGame = gameType,
            OverlayEnabled = true,
            OverlayCorner = OverlayCorner.TopLeft,
        };

        if (presetName.Contains(GenHotkeysConstants.PresetVanilla, StringComparison.OrdinalIgnoreCase) ||
            presetName.Contains("Default", StringComparison.OrdinalIgnoreCase))
        {
            return profile;
        }

        // Load corresponding CSF preset
        var csfFileName = presetName.Contains(GenHotkeysConstants.PresetLegionnaire, StringComparison.OrdinalIgnoreCase)
            ? GenHotkeysConstants.PresetsLegionnaireRu
            : GenHotkeysConstants.PresetsLeikezeEn;

        var stream = TryOpenAssetStream(csfFileName);
        if (stream != null)
        {
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
        }

        return profile;
    }

    private string GetSafeProfilePath(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var safeId = Path.GetFileName(profileId);
        if (!string.Equals(safeId, profileId, StringComparison.Ordinal) ||
            safeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Invalid profile ID: path traversal or invalid characters detected.", nameof(profileId));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_profilesDirectory, $"{safeId}.json"));
        var basePathWithSeparator = Path.GetFullPath(_profilesDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(basePathWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Profile path escapes allowed profiles directory.", nameof(profileId));
        }

        return fullPath;
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_profilesDirectory))
        {
            Directory.CreateDirectory(_profilesDirectory);
        }
    }

    private HotkeyProfile CreateDefaultProfile(GameType gameType)
    {
        return new HotkeyProfile
        {
            Name = gameType == GameType.Generals ? "Default Generals Hotkeys" : "Default Zero Hour Hotkeys",
            TargetGame = gameType,
            OverlayEnabled = true,
            OverlayCorner = OverlayCorner.TopLeft,
        };
    }

    private Stream? TryOpenAssetStream(string relativePath)
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

        var devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "GenHotkeys", relativePath);
        if (File.Exists(devPath))
        {
            return File.OpenRead(devPath);
        }

        return null;
    }
}
