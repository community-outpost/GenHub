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
using GenHub.Features.Tools.GenHotkeys.Data;
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

            // 1. Build and modify CSF asynchronously to avoid blocking the UI thread
            await Task.Run(() => ApplyCsfModifications(profile, stagingDir), cancellationToken);

            // 2. Process Icon Overlays if enabled
            if (profile.OverlayEnabled)
            {
                await GenerateOverlayTexturesAsync(profile, stagingDir, progress, cancellationToken);
            }

            // 3. Pack into .big archive
            progress?.Report("Packing files into .big archive...");
            await PackBigArchiveAsync(profile, stagingDir, packageDir, cancellationToken);

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create hotkey addon for profile '{Name}'", profile.Name);
            return OperationResult<ContentManifest>.CreateFailure(
                $"Failed to build hotkeys addon: {ex.Message}");
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
        baseCsf.Save(csfOutputPath);
    }

    private static void StripLabelAndAliases(CsfFile csf, string label)
    {
        var existing = csf.GetString(label);
        if (!string.IsNullOrEmpty(existing))
        {
            csf.SetString(label, CsfFile.StripHotkey(existing));
        }

        if (GenHotkeysConstants.ShortcutLabelAliases.TryGetValue(label, out var aliases))
        {
            foreach (var alias in aliases)
            {
                var aliasExisting = csf.GetString(alias);
                if (!string.IsNullOrEmpty(aliasExisting))
                {
                    csf.SetString(alias, CsfFile.StripHotkey(aliasExisting));
                }
            }
        }
    }

    private static void SetLabelAndAliases(CsfFile csf, string label, char key)
    {
        var existing = csf.GetString(label);
        if (!string.IsNullOrEmpty(existing))
        {
            csf.SetString(label, CsfFile.SetHotkey(existing, key));
        }

        if (GenHotkeysConstants.ShortcutLabelAliases.TryGetValue(label, out var aliases))
        {
            foreach (var alias in aliases)
            {
                var aliasExisting = csf.GetString(alias);
                if (!string.IsNullOrEmpty(aliasExisting))
                {
                    csf.SetString(alias, CsfFile.SetHotkey(aliasExisting, key));
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
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sanitizedName = SafeFileNameRegex.Replace(profile.Name, "_");
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = "Hotkeys";
        }

        var gameTag = profile.TargetGame == GameType.Generals ? "Gen" : "ZH";
        var bigFileName = string.Format(GenHotkeysConstants.BigFileNamePattern, sanitizedName, gameTag);
        var bigFilePath = Path.Combine(packageDir, bigFileName);

        await BigFilePacker.PackAsync(stagingDir, bigFilePath);
        cancellationToken.ThrowIfCancellationRequested();
        return bigFilePath;
    }

    /// <summary>
    /// Loads the base CSF string table for the specified profile.
    /// Non-Legionnaire presets (including Vanilla/Default) use the bundled LeikezeEN string table,
    /// which provides the reference English layout used across the editor.
    /// </summary>
    private static CsfFile LoadBaseCsf(HotkeyProfile profile)
    {
        var presetFile = profile.BasePreset?.Contains(GenHotkeysConstants.PresetLegionnaire, StringComparison.OrdinalIgnoreCase) == true
            ? GenHotkeysConstants.PresetsLegionnaireRu
            : GenHotkeysConstants.PresetsLeikezeEn;

        var stream = TryOpenAssetStream(presetFile);
        if (stream != null)
        {
            using (stream)
            {
                return CsfFile.Load(stream);
            }
        }

        throw new FileNotFoundException($"Base CSF preset '{presetFile}' could not be loaded. Ensure GenHotkeys assets are present.");
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
