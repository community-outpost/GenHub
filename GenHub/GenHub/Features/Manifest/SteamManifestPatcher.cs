using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Implementation of <see cref="ISteamManifestPatcher"/>.
/// </summary>
public class SteamManifestPatcher(
    ILogger<SteamManifestPatcher> logger,
    IConfigurationProviderService configurationProvider) : ISteamManifestPatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <inheritdoc/>
    public async Task PatchManifestAsync(string manifestId, bool useSteamLaunch)
    {
        try
        {
            logger.LogInformation("Patching manifest {ManifestId} for Steam launch: {UseSteamLaunch}", manifestId, useSteamLaunch);

            // Locate the manifest file
            var manifestsDir = configurationProvider.GetManifestsPath();

            if (!Directory.Exists(manifestsDir))
            {
                logger.LogInformation("Manifests directory not found, creating: {Dir}", manifestsDir);
                Directory.CreateDirectory(manifestsDir);
                return;
            }

            var (targetFile, manifest) = await FindManifestAsync(manifestsDir, manifestId);

            if (targetFile == null || manifest == null)
            {
                logger.LogWarning("Manifest {ManifestId} not found in cache", manifestId);
                return;
            }

            var changed = ApplyLaunchMode(manifest, useSteamLaunch, manifestId, logger);

            if (changed)
            {
                // Save back to disk
                await using var writeStream = File.Create(targetFile);
                await JsonSerializer.SerializeAsync(writeStream, manifest, JsonOptions);
                logger.LogInformation("Successfully patched manifest {ManifestId} at {Path}", manifestId, targetFile);
            }
            else
            {
                logger.LogDebug("No changes needed for manifest {ManifestId}", manifestId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error patching manifest {ManifestId}", manifestId);
            throw;
        }
    }

    private static async Task<(string? TargetFile, ContentManifest? Manifest)> FindManifestAsync(string manifestsDir, string manifestId)
    {
        var manifestFiles = Directory.EnumerateFiles(manifestsDir, "*.json", SearchOption.AllDirectories);

        foreach (var file in manifestFiles)
        {
            try
            {
                // Read small JSON manifests safely
                await using var stream = File.OpenRead(file);
                var candidate = await JsonSerializer.DeserializeAsync<ContentManifest>(stream, JsonOptions);

                if (candidate?.Id == manifestId)
                {
                    return (file, candidate);
                }
            }
            catch (IOException)
            {
                // Ignore read errors for invalid or inaccessible files
            }
            catch (JsonException)
            {
                // Ignore JSON deserialization errors for non-manifest files
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore access permission errors
            }
        }

        return (null, null);
    }

    private static bool ApplyLaunchMode(ContentManifest manifest, bool useSteamLaunch, string manifestId, ILogger logger)
    {
        var generalsExe = manifest.Files.FirstOrDefault(f => f.RelativePath.Equals(GameClientConstants.GeneralsExecutable, StringComparison.OrdinalIgnoreCase));
        var gameDat = manifest.Files.FirstOrDefault(f => f.RelativePath.Equals(GameClientConstants.SteamGameDatExecutable, StringComparison.OrdinalIgnoreCase));

        if (generalsExe == null && gameDat == null)
        {
            logger.LogDebug("Manifest {ManifestId} does not contain generals.exe or game.dat, skipping patch", manifestId);
            return false;
        }

        return useSteamLaunch
            ? ApplySteamMode(manifest, generalsExe, gameDat)
            : ApplyStandaloneMode(manifest, generalsExe, gameDat);
    }

    private static bool ApplySteamMode(ContentManifest manifest, ManifestFile? generalsExe, ManifestFile? gameDat)
    {
        var changed = false;

        // Steam Mode: generals.exe = true, game.dat = false (if it exists)
        if (generalsExe is { IsExecutable: false })
        {
            generalsExe.IsExecutable = true;
            changed = true;
        }

        if (gameDat is { IsExecutable: true })
        {
            gameDat.IsExecutable = false;
            changed = true;
        }

        if (manifest.EntryPoint != null && generalsExe != null && !string.Equals(manifest.EntryPoint, generalsExe.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            manifest.EntryPoint = generalsExe.RelativePath;
            changed = true;
        }

        return changed;
    }

    private static bool ApplyStandaloneMode(ContentManifest manifest, ManifestFile? generalsExe, ManifestFile? gameDat)
    {
        var changed = false;

        // Standalone Mode: generals.exe = false (if game.dat exists), game.dat = true
        if (gameDat != null)
        {
            if (!gameDat.IsExecutable)
            {
                gameDat.IsExecutable = true;
                changed = true;
            }

            if (generalsExe is { IsExecutable: true })
            {
                generalsExe.IsExecutable = false;
                changed = true;
            }

            if (manifest.EntryPoint != null && !string.Equals(manifest.EntryPoint, gameDat.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                manifest.EntryPoint = gameDat.RelativePath;
                changed = true;
            }
        }
        else if (generalsExe is { IsExecutable: false })
        {
            // If no game.dat, generals.exe must be the executable
            generalsExe.IsExecutable = true;
            changed = true;

            if (manifest.EntryPoint != null && !string.Equals(manifest.EntryPoint, generalsExe.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                manifest.EntryPoint = generalsExe.RelativePath;
                changed = true;
            }
        }

        return changed;
    }
}
