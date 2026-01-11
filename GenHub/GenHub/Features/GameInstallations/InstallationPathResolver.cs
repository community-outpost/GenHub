using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameInstallations;

/// <summary>
/// Provides services for resolving and validating game installation paths.
/// </summary>
public class InstallationPathResolver(
    ILogger<InstallationPathResolver> logger) : IInstallationPathResolver
{
    private readonly ILogger<InstallationPathResolver> _logger = logger;

    /// <inheritdoc/>
    public async Task<OperationResult<GameInstallation>> ResolveInstallationPathAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        // First, check if current path is valid
        var validationResult = await ValidateInstallationPathAsync(installation, cancellationToken);
        if (validationResult.Success && validationResult.Data)
        {
            _logger.LogDebug(
                "Installation path is valid, no resolution needed: {Path}",
                installation.InstallationPath);
            return OperationResult<GameInstallation>.CreateSuccess(installation);
        }

        _logger.LogInformation(
            "Installation path is invalid: {Path}. Attempting to resolve...",
            installation.InstallationPath);

        // Try to find the installation at common locations
        var searchResult = await SearchForInstallationAsync(installation, null, cancellationToken);
        if (searchResult.Success && !string.IsNullOrEmpty(searchResult.Data))
        {
            var newPath = searchResult.Data;
            _logger.LogInformation(
                "Resolved installation path from {OldPath} to {NewPath}",
                installation.InstallationPath,
                newPath);

            // Create a new installation with the updated path
            var resolvedInstallation = new GameInstallation(newPath, installation.InstallationType)
            {
                Id = installation.Id,
                DetectedAt = installation.DetectedAt,
            };

            // Populate paths
            resolvedInstallation.Fetch();

            return OperationResult<GameInstallation>.CreateSuccess(resolvedInstallation);
        }

        _logger.LogWarning(
            "Could not resolve installation path for {InstallationType} installation (ID: {Id})",
            installation.InstallationType,
            installation.Id);

        return OperationResult<GameInstallation>.CreateFailure(
            $"Could not resolve installation path. Original path '{installation.InstallationPath}' no longer exists.");
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> ValidateInstallationPathAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        try
        {
            // Check if installation directory exists
            if (!Directory.Exists(installation.InstallationPath))
            {
                _logger.LogDebug(
                    "Installation path does not exist: {Path}",
                    installation.InstallationPath);
                return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
            }

            // Check if it contains expected game files
            var hasValidFiles = false;

            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                var generalsExe = Path.Combine(installation.GeneralsPath, "generals.exe");
                if (File.Exists(generalsExe))
                {
                    hasValidFiles = true;
                }
            }

            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                var zhExe = Path.Combine(installation.ZeroHourPath, "generals.exe");
                if (File.Exists(zhExe))
                {
                    hasValidFiles = true;
                }
            }

            if (!hasValidFiles)
            {
                _logger.LogDebug(
                    "Installation path exists but does not contain valid game files: {Path}",
                    installation.InstallationPath);
                return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
            }

            _logger.LogDebug(
                "Installation path is valid: {Path}",
                installation.InstallationPath);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error validating installation path: {Path}",
                installation.InstallationPath);
            return Task.FromResult(
                OperationResult<bool>.CreateFailure($"Error validating path: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> SearchForInstallationAsync(
        GameInstallation installation,
        string? gameDatHash = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        _logger.LogInformation(
            "Searching for {InstallationType} installation...",
            installation.InstallationType);

        // Get common search locations based on installation type
        var searchPaths = GetSearchPaths(installation.InstallationType);

        foreach (var searchPath in searchPaths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (!Directory.Exists(searchPath))
                {
                    continue;
                }

                _logger.LogDebug("Searching in: {SearchPath}", searchPath);

                // Search for game installations in this directory
                var foundPath = await SearchDirectoryForInstallationAsync(
                    searchPath,
                    installation,
                    gameDatHash,
                    cancellationToken);

                if (!string.IsNullOrEmpty(foundPath))
                {
                    _logger.LogInformation(
                        "Found installation at: {Path}",
                        foundPath);
                    return OperationResult<string>.CreateSuccess(foundPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error searching directory: {SearchPath}",
                    searchPath);
            }
        }

        _logger.LogWarning(
            "Could not find {InstallationType} installation in any common location",
            installation.InstallationType);

        return OperationResult<string>.CreateFailure(
            $"Installation not found in common locations");
    }

    private static List<string> GetSearchPaths(GameInstallationType installationType)
    {
        var paths = new List<string>();

        // Common installation locations
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        switch (installationType)
        {
            case GameInstallationType.Retail:
                paths.Add(Path.Combine(programFiles, "EA Games"));
                paths.Add(Path.Combine(programFiles64, "EA Games"));
                paths.Add(Path.Combine(programFiles, "Electronic Arts"));
                paths.Add(Path.Combine(programFiles64, "Electronic Arts"));
                break;

            case GameInstallationType.Steam:
                // Steam library locations
                paths.Add(Path.Combine(programFiles, "Steam", "steamapps", "common"));
                paths.Add(Path.Combine(programFiles64, "Steam", "steamapps", "common"));
                paths.Add(Path.Combine("C:\\", "Program Files (x86)", "Steam", "steamapps", "common"));
                paths.Add(Path.Combine("C:\\", "Program Files", "Steam", "steamapps", "common"));
                break;

            default:
                // For unknown types, search common EA Games locations
                paths.Add(Path.Combine(programFiles, "EA Games"));
                paths.Add(Path.Combine(programFiles64, "EA Games"));
                break;
        }

        // Also search user's Documents and Desktop as fallback
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        paths.Add(documents);
        paths.Add(desktop);

        return paths;
    }

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    private async Task<string?> SearchDirectoryForInstallationAsync(
        string searchPath,
        GameInstallation installation,
        string? gameDatHash,
        CancellationToken cancellationToken)
    {
        try
        {
            // Search for directories that might contain the game
            var directories = Directory.GetDirectories(searchPath, "*", SearchOption.TopDirectoryOnly);

            foreach (var dir in directories)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Check if this directory contains game files
                if (await IsValidGameInstallationAsync(dir, installation, gameDatHash, cancellationToken))
                {
                    return dir;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we don't have access to
            _logger.LogDebug("Access denied to directory: {SearchPath}", searchPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching directory: {SearchPath}", searchPath);
        }

        return null;
    }

    private async Task<bool> IsValidGameInstallationAsync(
        string directory,
        GameInstallation installation,
        string? gameDatHash,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check for generals.exe (both Generals and Zero Hour use this)
            var generalsExe = Path.Combine(directory, "generals.exe");
            if (!File.Exists(generalsExe))
            {
                return false;
            }

            // If we have a game.dat hash to match, verify it
            if (!string.IsNullOrEmpty(gameDatHash))
            {
                var gameDatPath = Path.Combine(directory, "game.dat");
                if (File.Exists(gameDatPath))
                {
                    var hash = await ComputeFileHashAsync(gameDatPath, cancellationToken);
                    if (!string.Equals(hash, gameDatHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            // Check for game type specific files
            if (installation.HasZeroHour)
            {
                // Zero Hour has DbgHelp.dll
                var dbgHelpDll = Path.Combine(directory, "DbgHelp.dll");
                if (File.Exists(dbgHelpDll))
                {
                    return true;
                }
            }

            if (installation.HasGenerals)
            {
                // Just having generals.exe is enough for Generals
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking directory: {Directory}", directory);
            return false;
        }
    }
}
