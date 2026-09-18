using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameInstallations;

/// <summary>
/// Provides services for resolving and validating game installation paths.
/// </summary>
public class InstallationPathResolver(
    ILogger<InstallationPathResolver> logger,
    IInstallationSearchPathProvider? searchPathProvider = null) : IInstallationPathResolver
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
            var resolvedPath = searchResult.Data;
            _logger.LogInformation(
                "Successfully resolved installation path to: {ResolvedPath}",
                resolvedPath);

            // Update installation paths
            var updatedInstallation = new GameInstallation(
                resolvedPath,
                installation.InstallationType,
                _logger as ILogger<GameInstallation>)
            {
                Id = installation.Id,
                DisplayName = installation.DisplayName,
            };

            // Set game-specific paths based on original installation
            string? generalsPath = null;
            string? zeroHourPath = null;

            if (installation.HasGenerals)
            {
                generalsPath = Path.Combine(resolvedPath, "Generals");
                if (!Directory.Exists(generalsPath))
                {
                    generalsPath = resolvedPath;
                }
            }

            if (installation.HasZeroHour)
            {
                zeroHourPath = Path.Combine(resolvedPath, "Command and Conquer Generals Zero Hour");
                if (!Directory.Exists(zeroHourPath))
                {
                    zeroHourPath = Path.Combine(resolvedPath, "ZeroHour");
                    if (!Directory.Exists(zeroHourPath))
                    {
                        zeroHourPath = resolvedPath;
                    }
                }
            }

            updatedInstallation.SetPaths(generalsPath, zeroHourPath);
            updatedInstallation.PopulateGameClients(installation.AvailableGameClients);

            return OperationResult<GameInstallation>.CreateSuccess(updatedInstallation);
        }

        _logger.LogWarning(
            "Failed to resolve installation path for: {Path}",
            installation.InstallationPath);

        return OperationResult<GameInstallation>.CreateFailure(
            $"Could not resolve installation path: {installation.InstallationPath}");
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ValidateInstallationPathAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        if (string.IsNullOrEmpty(installation.InstallationPath))
        {
            return OperationResult<bool>.CreateFailure("Installation path is null or empty");
        }

        if (!Directory.Exists(installation.InstallationPath))
        {
            return OperationResult<bool>.CreateSuccess(false);
        }

        // Check for game executables or known game data files
        var hasValidFiles = false;

        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) && Directory.Exists(installation.GeneralsPath))
        {
            var generalsExe = Path.Combine(installation.GeneralsPath, GameClientConstants.GeneralsExecutable);
            var gameDat = Path.Combine(installation.GeneralsPath, GameClientConstants.SteamGameDatExecutable);
            if (generalsExe.FileExistsCaseInsensitive() || gameDat.FileExistsCaseInsensitive())
            {
                hasValidFiles = true;
            }
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath) && Directory.Exists(installation.ZeroHourPath))
        {
            var zhExe = Path.Combine(installation.ZeroHourPath, GameClientConstants.GeneralsExecutable);
            var zhGameDat = Path.Combine(installation.ZeroHourPath, GameClientConstants.SteamGameDatExecutable);
            if (zhExe.FileExistsCaseInsensitive() || zhGameDat.FileExistsCaseInsensitive())
            {
                hasValidFiles = true;
            }
        }

        // Fallback: Check root installation directory for any valid game executable
        if (!hasValidFiles && InstallationExtensions.HasValidGameExecutable(installation.InstallationPath))
        {
            hasValidFiles = true;
        }

        if (!hasValidFiles)
        {
            _logger.LogDebug(
                "Installation path {Path} does not contain valid game files",
                installation.InstallationPath);
            return OperationResult<bool>.CreateSuccess(false);
        }

        return await Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> SearchForInstallationAsync(
        GameInstallation installation,
        string? searchRootDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        _logger.LogInformation(
            "Searching for {InstallationType} installation...",
            installation.InstallationType);

        var searchPaths = new List<string>();

        if (!string.IsNullOrEmpty(searchRootDirectory))
        {
            searchPaths.Add(searchRootDirectory);
        }
        else
        {
            // Add default search paths based on installation type and platform
            searchPaths.AddRange(GetSearchPaths(installation.InstallationType));
        }

        // Compute hash of existing game.dat if available for verification
        string? gameDatHash = null;
        if (!string.IsNullOrEmpty(installation.InstallationPath) &&
            Directory.Exists(installation.InstallationPath))
        {
            var gameDatPath = Path.Combine(installation.InstallationPath, GameClientConstants.SteamGameDatExecutable);
            if (gameDatPath.TryGetFileCaseInsensitive(out var resolvedGameDatPath))
            {
                try
                {
                    gameDatHash = await ComputeFileHashAsync(resolvedGameDatPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not compute hash of game.dat at {Path}", resolvedGameDatPath);
                }
            }
        }

        // Search each path
        foreach (var searchPath in searchPaths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OperationResult<string>.CreateFailure("Search cancelled");
            }

            if (!Directory.Exists(searchPath))
            {
                continue;
            }

            _logger.LogDebug("Searching in: {SearchPath}", searchPath);

            var foundPath = await SearchDirectoryForInstallationAsync(
                searchPath,
                installation,
                gameDatHash,
                cancellationToken);

            if (!string.IsNullOrEmpty(foundPath))
            {
                _logger.LogInformation("Found installation at: {FoundPath}", foundPath);
                return OperationResult<string>.CreateSuccess(foundPath);
            }
        }

        _logger.LogWarning(
            "Could not find {InstallationType} installation in any common location",
            installation.InstallationType);

        return OperationResult<string>.CreateFailure(
            "Installation not found in common locations");
    }

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = IoConstants.FileHashBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        };

        await using var stream = new FileStream(filePath, options);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private List<string> GetSearchPaths(GameInstallationType installationType)
    {
        var paths = new List<string>();

        if (searchPathProvider != null)
        {
            paths.AddRange(searchPathProvider.GetSearchPaths(installationType));
        }

        // Also search user's Documents and Desktop as fallback
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(documents))
        {
            paths.Add(documents);
        }

        if (!string.IsNullOrEmpty(desktop))
        {
            paths.Add(desktop);
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
            // Check for valid game executable
            if (!InstallationExtensions.HasValidGameExecutable(directory))
            {
                return false;
            }

            // If we have a game.dat hash to match, verify it
            if (!string.IsNullOrEmpty(gameDatHash))
            {
                var gameDatCandidate = Path.Combine(directory, GameClientConstants.SteamGameDatExecutable);
                if (gameDatCandidate.TryGetFileCaseInsensitive(out var resolvedGameDatPath))
                {
                    var hash = await ComputeFileHashAsync(resolvedGameDatPath, cancellationToken);
                    if (!string.Equals(hash, gameDatHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            // Check for game type specific files
            if (installation.HasZeroHour)
            {
                // Zero Hour has DbgHelp.dll or Zero Hour signature files
                var dbgHelpDll = Path.Combine(directory, "DbgHelp.dll");
                if (dbgHelpDll.FileExistsCaseInsensitive() ||
                    Path.Combine(directory, GameClientConstants.ZeroHourIniBig).FileExistsCaseInsensitive() ||
                    Path.Combine(directory, GameClientConstants.ZeroHourPatchBig).FileExistsCaseInsensitive())
                {
                    return true;
                }
            }

            if (installation.HasGenerals)
            {
                // Having a valid executable is enough for Generals
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
