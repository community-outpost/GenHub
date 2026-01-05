using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Implementation of <see cref="ISteamManifestPatcher"/>.
/// </summary>
public class SteamManifestPatcher(ILogger<SteamManifestPatcher> logger) : ISteamManifestPatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <inheritdoc/>
    public async Task PatchManifestAsync(string manifestId, bool useSteamLaunch)
    {
        try
        {
            logger.LogInformation("Patching manifest {ManifestId} for Steam launch: {UseSteamLaunch}", manifestId, useSteamLaunch);

            // Locate the manifest file
            var manifestsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppConstants.AppName,
                FileTypes.ManifestsDirectory);

            if (!Directory.Exists(manifestsDir))
            {
                logger.LogInformation("Manifests directory not found, creating: {Dir}", manifestsDir);
                Directory.CreateDirectory(manifestsDir);
                return;
            }

            // Scan for the manifest file (naive scan since we don't know the exact filename)
            // Optimization: Assuming file extension is .json
            var manifestFiles = Directory.EnumerateFiles(manifestsDir, "*.json", SearchOption.AllDirectories);
            string? targetFile = null;
            ContentManifest? manifest = null;

            foreach (var file in manifestFiles)
            {
                try
                {
                    // Quick check: does filename contain ID? (Optimization if naming convention holds)
                    // If not, we have to read it.
                    // To be safe and fast, we read all small JSONs. Manifests are small.
                    await using var stream = File.OpenRead(file);
                    var candidate = await JsonSerializer.DeserializeAsync<ContentManifest>(stream, JsonOptions);

                    if (candidate != null && candidate.Id == manifestId)
                    {
                        targetFile = file;
                        manifest = candidate;
                        break;
                    }
                }
                catch
                {
                    // Ignore read errors
                }
            }

            if (targetFile == null || manifest == null)
            {
                logger.LogWarning("Manifest {ManifestId} not found in cache", manifestId);
                return;
            }

            // Apply changes
            var generalsExe = manifest.Files.FirstOrDefault(f => f.RelativePath.Equals(GameClientConstants.GeneralsExecutable, StringComparison.OrdinalIgnoreCase));
            var gameDat = manifest.Files.FirstOrDefault(f => f.RelativePath.Equals(GameClientConstants.SteamGameDatExecutable, StringComparison.OrdinalIgnoreCase));

            if (generalsExe == null && gameDat == null)
            {
                logger.LogDebug("Manifest {ManifestId} does not contain generals.exe or game.dat, skipping patch", manifestId);
                return;
            }

            bool changed = false;

            if (useSteamLaunch)
            {
                // Steam Mode: generals.exe = true, game.dat = false (if it exists)
                if (generalsExe != null && !generalsExe.IsExecutable)
                {
                    generalsExe.IsExecutable = true;
                    changed = true;
                }

                if (gameDat != null && gameDat.IsExecutable)
                {
                    gameDat.IsExecutable = false;
                    changed = true;
                }
            }
            else
            {
                // Standalone Mode: generals.exe = false (if game.dat exists), game.dat = true
                if (gameDat != null)
                {
                    if (!gameDat.IsExecutable)
                    {
                        gameDat.IsExecutable = true;
                        changed = true;
                    }

                    if (generalsExe != null && generalsExe.IsExecutable)
                    {
                        generalsExe.IsExecutable = false;
                        changed = true;
                    }
                }
                else if (generalsExe != null && !generalsExe.IsExecutable)
                {
                    // If no game.dat, generals.exe must be the executable
                    generalsExe.IsExecutable = true;
                    changed = true;
                }
            }

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
}
