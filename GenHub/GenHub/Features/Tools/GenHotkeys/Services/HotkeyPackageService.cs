using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.GenHotkeys;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Service for building a standalone .big archive containing customized CSF, CommandMap.ini,
/// and icon TGAs, and registering it as a GenHub ContentManifest Addon.
/// </summary>
public class HotkeyPackageService(
    ITechTreeService techTreeService,
    IIconOverlayService iconOverlayService,
    ILocalContentService localContentService,
    ILogger<HotkeyPackageService> logger) : IHotkeyPackageService
{
    private static readonly Regex SafeFileNameRegex = new("[^a-zA-Z0-9_-]", RegexOptions.Compiled);

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> CreateHotkeysAddonAsync(
        HotkeyProfile profile,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var stagingDir = Path.Combine(Path.GetTempPath(), $"GenHub_Hotkeys_{Guid.NewGuid():N}");
        var packageDir = Path.Combine(Path.GetTempPath(), $"GenHub_Hotkeys_Pkg_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingDir);
            Directory.CreateDirectory(packageDir);

            progress?.Report("Preparing customized CSF string table...");
            logger.LogInformation("Generating hotkey addon for profile '{Name}' ({Game})", profile.Name, profile.TargetGame);

            // 1. Build and modify CSF
            var baseCsf = await LoadBaseCsfAsync(profile.TargetGame, cancellationToken);
            foreach (var (label, key) in profile.KeyMappings)
            {
                var existing = baseCsf.GetString(label);
                var updated = CsfFile.SetHotkey(existing, key);
                baseCsf.SetString(label, updated);
            }

            var englishDir = Path.Combine(stagingDir, GenHotkeysConstants.DataEnglishDirectory);
            Directory.CreateDirectory(englishDir);
            var csfOutputPath = Path.Combine(englishDir, "generals.csf");
            baseCsf.Save(csfOutputPath);

            // Also place in Data/generals.csf for maximum game engine compatibility
            var dataDir = Path.Combine(stagingDir, "Data");
            baseCsf.Save(Path.Combine(dataDir, "generals.csf"));

            // 2. Build CommandMap.ini
            progress?.Report("Configuring CommandMap.ini...");
            var iniDir = Path.Combine(stagingDir, "Data", "INI");
            Directory.CreateDirectory(iniDir);
            var commandMapStream = TryOpenAssetStream(GenHotkeysConstants.PresetsCommandMap);
            if (commandMapStream != null)
            {
                using (commandMapStream)
                {
                    var cmdMap = CommandMapFile.Load(commandMapStream);
                    cmdMap.Save(Path.Combine(iniDir, GenHotkeysConstants.CommandMapFileName));
                }
            }

            // 3. Process Icon Overlays if enabled
            if (profile.OverlayEnabled)
            {
                progress?.Report("Rendering hotkey badge overlays on unit icons...");
                var texturesDir = Path.Combine(stagingDir, GenHotkeysConstants.ArtTexturesDirectory);
                Directory.CreateDirectory(texturesDir);

                var factions = await techTreeService.LoadTechTreeAsync(profile.TargetGame, cancellationToken);
                var processedIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var faction in factions)
                {
                    foreach (var obj in faction.GameObjects)
                    {
                        foreach (var layout in obj.KeyboardLayouts)
                        {
                            foreach (var action in layout)
                            {
                                if (string.IsNullOrWhiteSpace(action.IconName) || !processedIcons.Add(action.IconName))
                                {
                                    continue;
                                }

                                // Check if this action has an assigned hotkey
                                char? assignedHotkey = null;
                                if (!string.IsNullOrEmpty(action.HotkeyString) &&
                                    profile.KeyMappings.TryGetValue(action.HotkeyString, out var mappedKey))
                                {
                                    assignedHotkey = mappedKey;
                                }
                                else if (action.Hotkey.HasValue)
                                {
                                    assignedHotkey = action.Hotkey.Value;
                                }

                                if (assignedHotkey.HasValue)
                                {
                                    var iconBytes = await techTreeService.GetIconBytesAsync(
                                        action.IconName,
                                        profile.TargetGame,
                                        cancellationToken);

                                    if (iconBytes != null && iconBytes.Length > 0)
                                    {
                                        try
                                        {
                                            var tgaBytes = await iconOverlayService.GenerateOverlayTgaAsync(
                                                iconBytes,
                                                assignedHotkey.Value,
                                                profile.OverlayCorner,
                                                cancellationToken);

                                            var tgaPath = Path.Combine(texturesDir, $"{action.IconName}.tga");
                                            await File.WriteAllBytesAsync(tgaPath, tgaBytes, cancellationToken);
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.LogWarning(ex, "Failed to stamp hotkey overlay on icon {Icon}", action.IconName);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 4. Pack into .big archive
            progress?.Report("Packing files into .big archive...");
            var sanitizedName = SafeFileNameRegex.Replace(profile.Name, "_");
            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                sanitizedName = "Hotkeys";
            }

            var gameTag = profile.TargetGame == GameType.Generals ? "Gen" : "ZH";
            var bigFileName = string.Format(GenHotkeysConstants.BigFileNamePattern, sanitizedName, gameTag);
            var bigFilePath = Path.Combine(packageDir, bigFileName);

            await BigFilePacker.PackAsync(stagingDir, bigFilePath);
            logger.LogInformation("Packed hotkeys .big archive at {Path}", bigFilePath);

            // 5. Register with GenHub as ContentManifest Addon
            progress?.Report("Registering hotkey addon in GenHub...");
            var manifestDisplayName = $"Hotkeys - {profile.Name} ({gameTag})";
            var result = await localContentService.CreateLocalContentManifestAsync(
                directoryPath: packageDir,
                name: manifestDisplayName,
                contentType: ContentType.Addon,
                targetGame: profile.TargetGame,
                sourcePath: null,
                progress: null,
                cancellationToken: cancellationToken);

            if (!result.Success || result.Data == null)
            {
                logger.LogError("Failed to register hotkeys addon: {Errors}", string.Join(", ", result.Errors));
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to register hotkey addon: {string.Join(", ", result.Errors)}");
            }

            progress?.Report("Hotkey addon created successfully!");
            logger.LogInformation(
                "Successfully created and registered hotkey addon manifest {Id} for profile {Name}",
                result.Data.Id,
                profile.Name);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while generating hotkeys addon for {Profile}", profile.Name);
            return OperationResult<ContentManifest>.CreateFailure($"Exception while creating hotkeys addon: {ex.Message}");
        }
        finally
        {
            // Clean up temporary directories
            TryDeleteDirectory(stagingDir);
            TryDeleteDirectory(packageDir);
        }
    }

    private static async Task<CsfFile> LoadBaseCsfAsync(GameType gameType, CancellationToken cancellationToken)
    {
        var stream = TryOpenAssetStream(GenHotkeysConstants.PresetsLeikezeEn);
        if (stream != null)
        {
            using (stream)
            {
                return CsfFile.Load(stream);
            }
        }

        // Return empty CSF if preset not found
        return new CsfFile { LanguageCode = 0, Version = 3 };
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

        var devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "GenHotkeys", relativePath);
        if (File.Exists(devPath))
        {
            return File.OpenRead(devPath);
        }

        return null;
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up temporary directory {Path}", path);
        }
    }
}
