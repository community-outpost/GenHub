using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.GenHotkeys;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Tools.GenHotkeys.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Packages customized hotkeys into a SAGE engine .big archive and registers it as a GenHub Addon.
/// </summary>
public class HotkeyPackageService(
    ITechTreeService techTreeService,
    IIconOverlayService iconOverlayService,
    IServiceScopeFactory scopeFactory,
    ISageMappedImageParser mappedImageParser,
    ILogger<HotkeyPackageService> logger) : IHotkeyPackageService
{
    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> CreateHotkeysAddonAsync(
        HotkeyProfile profile,
        IProgress<string>? progress = null,
        string? existingManifestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var stagingDir = Path.Combine(Path.GetTempPath(), $"GenHotkeys_Staging_{Guid.NewGuid():N}");
        var packageDir = Path.Combine(Path.GetTempPath(), $"GenHotkeys_Pkg_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingDir);
            Directory.CreateDirectory(packageDir);

            // Step 1: Generate customized generals.csf
            progress?.Report("Generating localized strings (generals.csf)...");
            await GenerateGeneralsCsfAsync(profile, stagingDir, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Step 2: Render and stamp icon overlays (if enabled or if custom cameos exist)
            var hasCustomCameos = profile.CustomCameoMappings.Any(kvp => !string.IsNullOrWhiteSpace(kvp.Value) && File.Exists(kvp.Value));
            if (profile.OverlayEnabled || hasCustomCameos)
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
            var manifestResult = await RegisterAddonManifestAsync(profile, packageDir, existingManifestId, cancellationToken);

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or InvalidDataException or NotSupportedException or SixLabors.ImageSharp.ImageFormatException)
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

        // Apply customized display titles
        foreach (var (label, title) in profile.TitleMappings)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (profile.KeyMappings.TryGetValue(label, out var key))
            {
                baseCsf.SetString(label, CsfFile.SetHotkey(title, key));
            }
            else
            {
                var existing = baseCsf.GetString(label);
                var existingHk = !string.IsNullOrEmpty(existing) ? CsfFile.ExtractHotkey(existing) : null;
                baseCsf.SetString(label, existingHk.HasValue ? CsfFile.SetHotkey(title, existingHk.Value) : title);
            }
        }

        // Apply customized tooltip descriptions
        foreach (var (label, description) in profile.TooltipMappings)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            var targetLabel = GenHotkeysConstants.RetailActionToTooltipMap.TryGetValue(label, out var mapped)
                ? mapped
                : label;

            baseCsf.SetString(targetLabel, description);
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

        return action.Hotkey ?? action.DefaultHotkey;
    }

    private static async Task<string> PackBigArchiveAsync(
        HotkeyProfile profile,
        string stagingDir,
        string packageDir,
        CancellationToken cancellationToken)
    {
        var bigFileName = GenHotkeysConstants.GetBigFileName(profile.Name, profile.TargetGame, profile.Id);
        var bigFilePath = Path.Combine(packageDir, bigFileName);

        await BigFilePacker.PackAsync(stagingDir, bigFilePath, cancellationToken).ConfigureAwait(false);
        return bigFilePath;
    }

    /// <summary>
    /// Loads the base CSF template for a given profile.
    /// Uses an English reference preset matching the base preset layout (Vanilla, Legionnaire, or Leikeze)
    /// as the base string table so the game language remains English in Data/English/generals.csf.
    /// </summary>
    private static CsfFile LoadBaseCsf(HotkeyProfile profile)
    {
        string presetFile = string.Empty;
        if (profile.BasePreset?.Contains(GenHotkeysConstants.PresetLegionnaire, StringComparison.OrdinalIgnoreCase) == true)
        {
            presetFile = GenHotkeysConstants.PresetsLegionnaireEn;
        }
        else if (profile.BasePreset?.Contains(GenHotkeysConstants.PresetLeikeze, StringComparison.OrdinalIgnoreCase) == true)
        {
            presetFile = GenHotkeysConstants.PresetsLeikezeEn;
        }
        else
        {
            presetFile = GenHotkeysConstants.GetVanillaPresetCsfPath(profile.TargetGame);
        }

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup
        }
    }

    private static void AppendIconMappedImages(List<MappedImageDefinition> images, string icon, HashSet<string> written)
    {
        void AddEntry(string name)
        {
            if (written.Add(name))
            {
                images.Add(new MappedImageDefinition(
                    name,
                    $"{icon}.tga",
                    TextureEditorConstants.CameoSmallWidth,
                    TextureEditorConstants.CameoSmallHeight,
                    0,
                    0,
                    TextureEditorConstants.CameoSmallWidth,
                    TextureEditorConstants.CameoSmallHeight));
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
                "USA" => new[] { "SAC", "SA", "SS" },
                "PRC" => new[] { "SN", "SNC", "SS" },
                "GLA" => new[] { "SU", "SUC", "SS" },
                _ => Array.Empty<string>(),
            };

            foreach (var prefix in prefixes)
            {
                AddEntry($"{prefix}{baseName}");
                AddEntry($"{prefix}{baseName}_L");
            }
        }
    }

    private async Task GenerateMappedImagesIniAsync(
        HashSet<string> processedIcons,
        string stagingDir,
        CancellationToken cancellationToken)
    {
        var images = new List<MappedImageDefinition>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var icon in processedIcons)
        {
            AppendIconMappedImages(images, icon, written);
        }

        string headerComment = string.Join(
            '\n',
            GenHotkeysConstants.MappedImagesIniHeaderSeparator,
            GenHotkeysConstants.MappedImagesIniHeaderTitle,
            GenHotkeysConstants.MappedImagesIniHeaderDescription,
            GenHotkeysConstants.MappedImagesIniHeaderSeparator);
        var iniContent = mappedImageParser.Serialize(images, headerComment);

        // 1. Write to Data/INI/MappedImages/HandCreated/Hotkeys.ini (scanned last by SAGE ImageCollection::load)
        var handCreatedDir = Path.Combine(stagingDir, Path.Combine(GenHotkeysConstants.MappedImagesHandCreatedDirectory.Split('/')));
        Directory.CreateDirectory(handCreatedDir);
        await File.WriteAllTextAsync(Path.Combine(handCreatedDir, GenHotkeysConstants.HandCreatedHotkeysIniFileName), iniContent, cancellationToken);

        // 2. Write to Data/INI/MappedImages/TextureSize_512/zzHotkeys.ini (alphabetically sorts after retail SA/SN/SU in std::set)
        var textureSizeDir = Path.Combine(stagingDir, Path.Combine(GenHotkeysConstants.MappedImagesTextureSize512Directory.Split('/')));
        Directory.CreateDirectory(textureSizeDir);
        await File.WriteAllTextAsync(Path.Combine(textureSizeDir, GenHotkeysConstants.TextureSize512HotkeysIniFileName), iniContent, cancellationToken);
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
        var writtenIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var faction in factions)
        {
            foreach (var obj in faction.GameObjects)
            {
                await ProcessGameObjectOverlaysAsync(
                    obj,
                    profile,
                    texturesDir,
                    processedIcons,
                    writtenIcons,
                    cancellationToken);
            }
        }

        // Process any standalone or unlisted custom cameo mappings in the profile
        foreach (var (iconName, customPath) in profile.CustomCameoMappings)
        {
            if (string.IsNullOrWhiteSpace(iconName) || processedIcons.Contains(iconName))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                processedIcons.Add(iconName);
                char? hotkey = null;
                if (profile.OverlayEnabled && profile.KeyMappings.TryGetValue(iconName, out var mappedKey))
                {
                    hotkey = mappedKey;
                }

                var success = await TryRenderCustomCameoTgaAsync(
                    iconName,
                    customPath,
                    hotkey,
                    profile,
                    texturesDir,
                    cancellationToken);

                if (success)
                {
                    writtenIcons.Add(iconName);
                }
            }
        }

        if (writtenIcons.Count > 0)
        {
            // Generate MappedImages INIs so SAGE engine binds cameos to our overlaid textures
            await GenerateMappedImagesIniAsync(writtenIcons, stagingDir, cancellationToken);
        }
    }

    private async Task ProcessGameObjectOverlaysAsync(
        HotkeyGameObject obj,
        HotkeyProfile profile,
        string texturesDir,
        HashSet<string> processedIcons,
        HashSet<string> writtenIcons,
        CancellationToken cancellationToken)
    {
        // 1. Process object's own icon if it has a custom cameo
        if (!string.IsNullOrWhiteSpace(obj.IconName) &&
            !processedIcons.Contains(obj.IconName) &&
            profile.CustomCameoMappings.TryGetValue(obj.IconName, out var objCustomPath) &&
            File.Exists(objCustomPath))
        {
            processedIcons.Add(obj.IconName);
            var success = await TryRenderCustomCameoTgaAsync(
                obj.IconName,
                objCustomPath,
                null,
                profile,
                texturesDir,
                cancellationToken);

            if (success)
            {
                writtenIcons.Add(obj.IconName);
            }
        }

        // 2. Process actions in all keyboard layouts
        foreach (var layout in obj.KeyboardLayouts)
        {
            foreach (var action in layout)
            {
                if (string.IsNullOrWhiteSpace(action.IconName) || !processedIcons.Add(action.IconName))
                {
                    continue;
                }

                var assignedHotkey = ResolveActionHotkey(action, profile);
                var hasCustomCameo = profile.CustomCameoMappings.TryGetValue(action.IconName, out var customPath) && File.Exists(customPath);

                var success = false;
                if (hasCustomCameo)
                {
                    success = await TryRenderCustomCameoTgaAsync(
                        action.IconName,
                        customPath!,
                        profile.OverlayEnabled ? assignedHotkey : null,
                        profile,
                        texturesDir,
                        cancellationToken);
                }
                else if (profile.OverlayEnabled && assignedHotkey.HasValue)
                {
                    success = await TryRenderOverlayTgaAsync(
                        action.IconName,
                        assignedHotkey.Value,
                        profile,
                        texturesDir,
                        cancellationToken);
                }

                if (success)
                {
                    writtenIcons.Add(action.IconName);
                }
            }
        }
    }

    private async Task<bool> TryRenderOverlayTgaAsync(
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
            return false;
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
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or InvalidDataException or NotSupportedException or SixLabors.ImageSharp.ImageFormatException)
        {
            logger.LogWarning(ex, "Failed to stamp hotkey overlay on icon {Icon}", iconName);
            return false;
        }
    }

    private async Task<bool> TryRenderCustomCameoTgaAsync(
        string iconName,
        string customImagePath,
        char? hotkey,
        HotkeyProfile profile,
        string texturesDir,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(customImagePath))
            {
                logger.LogWarning("Custom cameo image does not exist at '{Path}' for icon '{Icon}'", customImagePath, iconName);
                return false;
            }

            var customBytes = await File.ReadAllBytesAsync(customImagePath, cancellationToken);
            byte[] tgaBytes;
            if (hotkey.HasValue)
            {
                tgaBytes = await iconOverlayService.GenerateOverlayTgaAsync(
                    customBytes,
                    hotkey.Value,
                    profile.OverlayCorner,
                    cancellationToken);
            }
            else
            {
                tgaBytes = await iconOverlayService.ConvertToTgaAsync(
                    customBytes,
                    cancellationToken);
            }

            var tgaPath = Path.Combine(texturesDir, $"{iconName}.tga");
            await File.WriteAllBytesAsync(tgaPath, tgaBytes, cancellationToken);

            // Also write duplicate TGA for all known retail ButtonImage aliases in Art/Textures
            // so any direct loose texture lookups or mods immediately resolve to the custom cameo!
            if (HotkeyRetailCameoMappings.Mappings.TryGetValue(iconName, out var retailImages))
            {
                foreach (var retailImage in retailImages)
                {
                    var aliasPath = Path.Combine(texturesDir, $"{retailImage}.tga");
                    if (!File.Exists(aliasPath))
                    {
                        await File.WriteAllBytesAsync(aliasPath, tgaBytes, cancellationToken);
                    }
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or InvalidDataException or NotSupportedException or SixLabors.ImageSharp.ImageFormatException)
        {
            logger.LogWarning(ex, "Failed to export custom cameo TGA for '{Icon}' from '{Path}'", iconName, customImagePath);
            return false;
        }
    }

    private async Task<OperationResult<ContentManifest>> RegisterAddonManifestAsync(
        HotkeyProfile profile,
        string packageDir,
        string? existingManifestId,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var localContentService = scope.ServiceProvider.GetRequiredService<ILocalContentService>();

        var manifestDisplayName = GenHotkeysConstants.GetManifestDisplayName(profile.Name, profile.TargetGame);
        var manifestIdToUpdate = !string.IsNullOrWhiteSpace(existingManifestId)
            ? existingManifestId
            : profile.AddonManifestId;

        if (!string.IsNullOrWhiteSpace(manifestIdToUpdate))
        {
            logger.LogInformation(
                "Updating existing hotkeys addon manifest '{ManifestId}' for profile '{Name}'",
                manifestIdToUpdate,
                profile.Name);

            var updateResult = await localContentService.UpdateLocalContentManifestAsync(
                manifestIdToUpdate,
                manifestDisplayName,
                packageDir,
                ContentType.Addon,
                profile.TargetGame,
                cancellationToken: cancellationToken);

            if (updateResult.Success && updateResult.Data is not null)
            {
                profile.AddonManifestId = updateResult.Data.Id.Value;
                return updateResult;
            }

            logger.LogWarning(
                "Failed to update existing manifest '{ManifestId}': {Error}",
                manifestIdToUpdate,
                updateResult.FirstError);
            return updateResult;
        }

        var createResult = await localContentService.CreateLocalContentManifestAsync(
            packageDir,
            manifestDisplayName,
            ContentType.Addon,
            profile.TargetGame,
            cancellationToken: cancellationToken);

        if (createResult.Success && createResult.Data is not null)
        {
            profile.AddonManifestId = createResult.Data.Id.Value;
        }

        return createResult;
    }
}
