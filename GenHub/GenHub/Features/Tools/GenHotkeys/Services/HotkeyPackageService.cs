using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Service for building a standalone .big archive containing customized CSF
/// and icon TGAs, and registering it as a GenHub ContentManifest Addon.
/// </summary>
public class HotkeyPackageService(
    ITechTreeService techTreeService,
    IIconOverlayService iconOverlayService,
    IServiceScopeFactory scopeFactory,
    ILogger<HotkeyPackageService> logger) : IHotkeyPackageService
{
    private static readonly Regex SafeFileNameRegex = new("[^a-zA-Z0-9_-]", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

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
            logger.LogDebug("Generating hotkey addon for profile '{Name}' ({Game})", profile.Name, profile.TargetGame);

            // 1. Build and modify CSF
            ApplyCsfModifications(profile, stagingDir);

            // 2. Process Icon Overlays if enabled
            if (profile.OverlayEnabled)
            {
                await GenerateOverlayTexturesAsync(profile, stagingDir, progress, cancellationToken);
            }

            // 3. Pack into .big archive
            progress?.Report("Packing files into .big archive...");
            await PackBigArchiveAsync(profile, stagingDir, packageDir);

            // 4. Register with GenHub as ContentManifest Addon
            progress?.Report("Registering hotkey addon in GenHub...");
            var result = await RegisterAddonManifestAsync(profile, packageDir, cancellationToken);

            if (!result.Success || result.Data == null)
            {
                logger.LogError("Failed to register hotkeys addon: {Errors}", string.Join(", ", result.Errors));
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to register hotkey addon: {string.Join(", ", result.Errors)}");
            }

            progress?.Report("Hotkey addon created successfully!");
            logger.LogInformation(
                "Successfully created hotkey addon manifest {Id} for profile {Name}",
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
            TryDeleteDirectory(stagingDir);
            TryDeleteDirectory(packageDir);
        }
    }

    private static void ApplyCsfModifications(HotkeyProfile profile, string stagingDir)
    {
        var baseCsf = LoadBaseCsf(profile);

        // Strip explicitly cleared hotkeys
        foreach (var label in profile.ClearedKeys)
        {
            var existing = baseCsf.GetString(label);
            if (!string.IsNullOrEmpty(existing))
            {
                var stripped = CsfFile.StripHotkey(existing);
                baseCsf.SetString(label, stripped);
            }
        }

        // Apply customized key mappings
        foreach (var (label, key) in profile.KeyMappings)
        {
            var existing = baseCsf.GetString(label);
            if (!string.IsNullOrEmpty(existing))
            {
                var updated = CsfFile.SetHotkey(existing, key);
                baseCsf.SetString(label, updated);
            }
        }

        var englishDir = Path.Combine(stagingDir, GenHotkeysConstants.DataEnglishDirectory);
        Directory.CreateDirectory(englishDir);
        var csfOutputPath = Path.Combine(englishDir, GenHotkeysConstants.GeneralsCsfFileName);
        baseCsf.Save(csfOutputPath);
    }

    private static char? ResolveActionHotkey(HotkeyAction action, HotkeyProfile profile)
    {
        if (!string.IsNullOrEmpty(action.HotkeyString))
        {
            if (profile.ClearedKeys.Contains(action.HotkeyString))
            {
                return null;
            }

            if (profile.KeyMappings.TryGetValue(action.HotkeyString, out var mappedKey))
            {
                return mappedKey;
            }
        }

        return action.Hotkey;
    }

    private static async Task<string> PackBigArchiveAsync(
        HotkeyProfile profile,
        string stagingDir,
        string packageDir)
    {
        var sanitizedName = SafeFileNameRegex.Replace(profile.Name, "_");
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = "Hotkeys";
        }

        var gameTag = profile.TargetGame == GameType.Generals ? "Gen" : "ZH";
        var bigFileName = string.Format(GenHotkeysConstants.BigFileNamePattern, sanitizedName, gameTag);
        var bigFilePath = Path.Combine(packageDir, bigFileName);

        await BigFilePacker.PackAsync(stagingDir, bigFilePath);
        return bigFilePath;
    }

    private static CsfFile LoadBaseCsf(HotkeyProfile profile)
    {
        var isVanilla = string.Equals(profile.BasePreset, GenHotkeysConstants.PresetVanilla, StringComparison.OrdinalIgnoreCase);
        var presetFile = profile.BasePreset?.Contains(GenHotkeysConstants.PresetLegionnaire, StringComparison.OrdinalIgnoreCase) == true
            ? GenHotkeysConstants.PresetsLegionnaireRu
            : GenHotkeysConstants.PresetsLeikezeEn;

        var stream = TryOpenAssetStream(presetFile);
        if (stream != null)
        {
            using (stream)
            {
                var csf = CsfFile.Load(stream);
                if (isVanilla)
                {
                    foreach (var (label, value) in csf.Strings.ToList())
                    {
                        if (label.StartsWith(GenHotkeysConstants.CsfControlBarPrefix, StringComparison.OrdinalIgnoreCase) ||
                            label.StartsWith(GenHotkeysConstants.CsfCommandPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var stripped = CsfFile.StripHotkey(value);
                            csf.SetString(label, stripped);
                        }
                    }
                }

                return csf;
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private static async Task GenerateMappedImagesIniAsync(
        HashSet<string> processedIcons,
        string stagingDir,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; ------------------------------------------------------------");
        sb.AppendLine("; Generated by GenHub GenHotkeys Tool");
        sb.AppendLine("; Maps custom overlaid icons to SAGE Engine cameo MappedImages");
        sb.AppendLine("; ------------------------------------------------------------");
        sb.AppendLine();

        foreach (var icon in processedIcons)
        {
            AppendIconMappedImages(sb, icon);
        }

        var iniContent = sb.ToString();

        // 1. Write to Data/INI/MappedImages/HandCreated/Hotkeys.ini (scanned last by SAGE ImageCollection::load)
        var handCreatedDir = Path.Combine(stagingDir, Path.Combine(GenHotkeysConstants.MappedImagesHandCreatedDirectory.Split('/')));
        Directory.CreateDirectory(handCreatedDir);
        await File.WriteAllTextAsync(Path.Combine(handCreatedDir, GenHotkeysConstants.HandCreatedHotkeysIniFileName), iniContent, cancellationToken);

        // 2. Write to Data/INI/MappedImages/TextureSize_512/zzHotkeys.ini (alphabetically sorts after retail SA/SN/SU in std::set)
        var textureSizeDir = Path.Combine(stagingDir, Path.Combine(GenHotkeysConstants.MappedImagesTextureSize512Directory.Split('/')));
        Directory.CreateDirectory(textureSizeDir);
        await File.WriteAllTextAsync(Path.Combine(textureSizeDir, GenHotkeysConstants.TextureSize512HotkeysIniFileName), iniContent, cancellationToken);
    }

    private static void AppendIconMappedImages(StringBuilder sb, string icon)
    {
        AppendMappedImageEntry(sb, icon, $"{icon}.tga");

        if (icon.Length <= 3)
        {
            return;
        }

        var baseName = icon[3..];
        var prefixes = icon[..3].ToUpperInvariant() switch
        {
            "USA" => new[] { "SAC", "SA" },
            "PRC" => new[] { "SN", "SNC" },
            "GLA" => new[] { "SU", "SUC" },
            _ => Array.Empty<string>(),
        };

        foreach (var prefix in prefixes)
        {
            AppendMappedImageEntry(sb, $"{prefix}{baseName}", $"{icon}.tga");
            AppendMappedImageEntry(sb, $"{prefix}{baseName}_L", $"{icon}.tga");
        }
    }

    private static void AppendMappedImageEntry(StringBuilder sb, string mappedName, string textureFileName)
    {
        sb.AppendLine($"MappedImage {mappedName}");
        sb.AppendLine($"  Texture = {textureFileName}");
        sb.AppendLine("  TextureWidth = 60");
        sb.AppendLine("  TextureHeight = 48");
        sb.AppendLine("  Coords = Left:0 Top:0 Right:60 Bottom:48");
        sb.AppendLine("  Status = NONE");
        sb.AppendLine("End");
        sb.AppendLine();
    }

    private async Task GenerateOverlayTexturesAsync(
        HotkeyProfile profile,
        string stagingDir,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
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
                await ProcessGameObjectOverlaysAsync(
                    obj,
                    profile,
                    texturesDir,
                    processedIcons,
                    cancellationToken);
            }
        }

        // Generate MappedImages INIs so SAGE engine binds cameos to our overlaid textures
        await GenerateMappedImagesIniAsync(processedIcons, stagingDir, cancellationToken);
    }

    private async Task ProcessGameObjectOverlaysAsync(
        HotkeyGameObject obj,
        HotkeyProfile profile,
        string texturesDir,
        HashSet<string> processedIcons,
        CancellationToken cancellationToken)
    {
        foreach (var layout in obj.KeyboardLayouts)
        {
            foreach (var action in layout)
            {
                if (string.IsNullOrWhiteSpace(action.IconName) || !processedIcons.Add(action.IconName))
                {
                    continue;
                }

                var assignedHotkey = ResolveActionHotkey(action, profile);
                if (assignedHotkey.HasValue)
                {
                    await TryRenderOverlayTgaAsync(
                        action.IconName,
                        assignedHotkey.Value,
                        profile,
                        texturesDir,
                        cancellationToken);
                }
            }
        }
    }

    private async Task TryRenderOverlayTgaAsync(
        string iconName,
        char hotkey,
        HotkeyProfile profile,
        string texturesDir,
        CancellationToken cancellationToken)
    {
        var iconBytes = await techTreeService.GetIconBytesAsync(
            iconName,
            profile.TargetGame,
            cancellationToken);

        if (iconBytes == null || iconBytes.Length == 0)
        {
            return;
        }

        try
        {
            var tgaBytes = await iconOverlayService.GenerateOverlayTgaAsync(
                iconBytes,
                hotkey,
                profile.OverlayCorner,
                cancellationToken);

            var tgaPath = Path.Combine(texturesDir, $"{iconName}.tga");
            await File.WriteAllBytesAsync(tgaPath, tgaBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to stamp hotkey overlay on icon {Icon}", iconName);
        }
    }

    private async Task<OperationResult<ContentManifest>> RegisterAddonManifestAsync(
        HotkeyProfile profile,
        string packageDir,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var localContentService = scope.ServiceProvider.GetRequiredService<ILocalContentService>();

        var gameTag = profile.TargetGame == GameType.Generals ? "Gen" : "ZH";
        var manifestDisplayName = $"Hotkeys - {profile.Name} ({gameTag})";

        return await localContentService.CreateLocalContentManifestAsync(
            directoryPath: packageDir,
            name: manifestDisplayName,
            contentType: ContentType.Addon,
            targetGame: profile.TargetGame,
            sourcePath: null,
            progress: null,
            cancellationToken: cancellationToken);
    }
}
