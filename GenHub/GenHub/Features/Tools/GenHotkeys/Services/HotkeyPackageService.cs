using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
using GenHub.Features.Tools.GenHotkeys.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Packages customized hotkeys into a SAGE engine .big archive and registers it as a GenHub Addon.
/// </summary>
public class HotkeyPackageService(
    ITechTreeService techTreeService,
    IIconOverlayService iconOverlayService,
    IServiceScopeFactory scopeFactory,
    ILogger<HotkeyPackageService> logger) : IHotkeyPackageService
{
    private static readonly Regex SafeFileNameRegex = new(
        @"[^a-zA-Z0-9_\-]",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> CreateHotkeysAddonAsync(
        HotkeyProfile profile,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var stagingDir = Path.Combine(Path.GetTempPath(), $"GenHub_Hotkeys_{Guid.NewGuid():N}");
        var packageDir = Path.Combine(Path.GetTempPath(), $"GenHub_Hotkeys_Pkg_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingDir);
            Directory.CreateDirectory(packageDir);

            // Step 1: Generate customized generals.csf
            progress?.Report("Generating localized strings (generals.csf)...");
            await GenerateGeneralsCsfAsync(profile, stagingDir, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Step 2: Render and stamp icon overlays (if enabled)
            if (profile.OverlayEnabled)
            {
                await GenerateOverlayTexturesAsync(profile, stagingDir, progress, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Step 3: Pack staging folder into !Hotkeys_<ProfileName>_<Game>.big
            progress?.Report("Packing SAGE .big archive...");
            var bigFilePath = await PackBigArchiveAsync(profile, stagingDir, packageDir, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Step 4: Register as an Addon ContentManifest in GenHub
            progress?.Report("Registering addon in GenHub...");
            var manifestResult = await RegisterAddonManifestAsync(profile, packageDir, cancellationToken);

            if (manifestResult is { Success: true, Data: not null })
            {
                logger.LogInformation(
                    "Successfully exported hotkeys addon '{Name}' ({Id}) to {Path}",
                    profile.Name,
                    profile.Id,
                    bigFilePath);
            }
            else
            {
                logger.LogWarning(
                    "Failed to register addon manifest for profile '{Name}' ({Id}): {Errors}",
                    profile.Name,
                    profile.Id,
                    string.Join("; ", manifestResult.Errors));
            }

            return manifestResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to package hotkeys addon for profile '{Name}'", profile.Name);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to create hotkeys addon: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(stagingDir);
            TryDeleteDirectory(packageDir);
        }
    }

    private static async Task GenerateGeneralsCsfAsync(
        HotkeyProfile profile,
        string stagingDir,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var baseCsf = LoadBaseCsf(profile);

        // Strip explicitly cleared hotkeys (both primary label and linked shortcut aliases)
        foreach (var label in profile.ClearedKeys)
        {
            StripLabelAndAliases(baseCsf, label);
        }

        // Apply customized key mappings (both primary label and linked shortcut aliases)
        foreach (var (label, key) in profile.KeyMappings)
        {
            SetLabelAndAliases(baseCsf, label, key);
        }

        // Synchronize all shortcut aliases with their primary labels in baseCsf
        // for any powers not explicitly modified by the user, ensuring default/preset
        // hotkeys also apply to sidebar buttons (e.g. OBJECT:SpyDrone gets &Y from CONTROLBAR:SpyDrone).
        foreach (var (primaryLabel, aliases) in GenHotkeysConstants.ShortcutLabelAliases)
        {
            if (profile.ClearedKeys.Contains(primaryLabel) || profile.KeyMappings.ContainsKey(primaryLabel))
            {
                continue;
            }

            var primaryText = baseCsf.GetString(primaryLabel);
            if (!string.IsNullOrEmpty(primaryText))
            {
                var defaultHk = CsfFile.ExtractHotkey(primaryText);
                if (defaultHk.HasValue)
                {
                    foreach (var alias in aliases)
                    {
                        var aliasText = baseCsf.GetString(alias);
                        if (!string.IsNullOrEmpty(aliasText))
                        {
                            var updated = CsfFile.SetHotkey(aliasText, defaultHk.Value);
                            baseCsf.SetString(alias, updated);
                        }
                    }
                }
            }
        }

        var englishDir = Path.Combine(stagingDir, GenHotkeysConstants.DataEnglishDirectory);
        Directory.CreateDirectory(englishDir);
        var csfOutputPath = Path.Combine(englishDir, GenHotkeysConstants.GeneralsCsfFileName);
        await Task.Run(() => baseCsf.Save(csfOutputPath), cancellationToken);
    }

    private static void StripLabelAndAliases(CsfFile csf, string label)
    {
        var existing = csf.GetString(label);
        if (!string.IsNullOrEmpty(existing))
        {
            var stripped = CsfFile.StripHotkey(existing);
            csf.SetString(label, stripped);
        }

        if (GenHotkeysConstants.ShortcutLabelAliases.TryGetValue(label, out var aliases))
        {
            foreach (var alias in aliases)
            {
                var aliasVal = csf.GetString(alias);
                if (!string.IsNullOrEmpty(aliasVal))
                {
                    var aliasStripped = CsfFile.StripHotkey(aliasVal);
                    csf.SetString(alias, aliasStripped);
                }
            }
        }
    }

    private static void SetLabelAndAliases(CsfFile csf, string label, char key)
    {
        var existing = csf.GetString(label);
        if (!string.IsNullOrEmpty(existing))
        {
            var updated = CsfFile.SetHotkey(existing, key);
            csf.SetString(label, updated);
        }

        if (GenHotkeysConstants.ShortcutLabelAliases.TryGetValue(label, out var aliases))
        {
            foreach (var alias in aliases)
            {
                var aliasVal = csf.GetString(alias);
                if (!string.IsNullOrEmpty(aliasVal))
                {
                    var aliasUpdated = CsfFile.SetHotkey(aliasVal, key);
                    csf.SetString(alias, aliasUpdated);
                }
            }
        }
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
        string packageDir,
        CancellationToken cancellationToken)
    {
        var sanitizedName = SafeFileNameRegex.Replace(profile.Name, "_");
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = "Hotkeys";
        }

        var gameTag = GenHotkeysConstants.GetGameTag(profile.TargetGame);
        var bigFileName = string.Format(GenHotkeysConstants.BigFileNamePattern, sanitizedName, gameTag);
        var bigFilePath = Path.Combine(packageDir, bigFileName);

        await BigFilePacker.PackAsync(stagingDir, bigFilePath, cancellationToken).ConfigureAwait(false);
        return bigFilePath;
    }

    /// <summary>
    /// Loads the base CSF template for a given profile.
    /// Prefers the Leikeze English reference preset (Presets/LeikezeEN.csf),
    /// which provides the reference English layout used across the editor.
    /// </summary>
    private static CsfFile LoadBaseCsf(HotkeyProfile profile)
    {
        var presetFile = profile.BasePreset?.Contains(GenHotkeysConstants.PresetLegionnaire, StringComparison.OrdinalIgnoreCase) == true
            ? GenHotkeysConstants.PresetsLegionnaireRu
            : GenHotkeysConstants.PresetsLeikezeEn;

        var stream = GenHotkeysAssetLoader.TryOpenAssetStream(presetFile);
        if (stream != null)
        {
            using (stream)
            {
                return CsfFile.Load(stream);
            }
        }

        throw new FileNotFoundException($"Base CSF preset '{presetFile}' could not be loaded. Ensure GenHotkeys assets are present.");
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
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddEntry(string name)
        {
            if (written.Add(name))
            {
                AppendMappedImageEntry(sb, name, $"{icon}.tga");
            }
        }

        AddEntry(icon);
        AddEntry($"{icon}_L");

        // 1. Check known retail ButtonImage mappings from SAGE CommandButton.ini
        if (HotkeyRetailCameoMappings.Mappings.TryGetValue(icon, out var retailImages))
        {
            foreach (var img in retailImages)
            {
                AddEntry(img);
                AddEntry($"{img}_L");
            }
        }

        // 2. Faction prefix heuristics as fallback/supplement
        if (icon.Length > 3)
        {
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
                AddEntry($"{prefix}{baseName}");
                AddEntry($"{prefix}{baseName}_L");
            }
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
        catch (OperationCanceledException)
        {
            throw;
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

        var gameTag = GenHotkeysConstants.GetGameTag(profile.TargetGame);
        var manifestDisplayName = $"Hotkeys - {profile.Name} ({gameTag})";

        return await localContentService.CreateLocalContentManifestAsync(
            packageDir,
            manifestDisplayName,
            ContentType.Addon,
            profile.TargetGame,
            cancellationToken: cancellationToken);
    }
}
