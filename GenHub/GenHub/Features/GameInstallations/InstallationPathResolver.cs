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
    /// <inheritdoc/>
    public async Task<OperationResult<GameInstallation>> ResolveInstallationPathAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        cancellationToken.ThrowIfCancellationRequested();

        // First, check if current path is valid
        var validationResult = await ValidateInstallationPathAsync(installation, cancellationToken);
        if (validationResult.Success && validationResult.Data)
        {
            logger.LogDebug(
                "Installation path is valid, no resolution needed: {Path}",
                installation.InstallationPath);
            return OperationResult<GameInstallation>.CreateSuccess(installation);
        }

        logger.LogInformation(
            "Installation path is invalid: {Path}. Attempting to resolve...",
            installation.InstallationPath);

        // Try to find the installation at common locations
        var searchResult = await SearchForInstallationAsync(installation, null, cancellationToken);
        if (searchResult.Success && !string.IsNullOrEmpty(searchResult.Data))
        {
            var resolvedPath = searchResult.Data;
            logger.LogInformation(
                "Successfully resolved installation path to: {ResolvedPath}",
                resolvedPath);

            var updatedInstallation = CreateResolvedInstallation(installation, resolvedPath);
            return OperationResult<GameInstallation>.CreateSuccess(updatedInstallation);
        }

        logger.LogWarning(
            "Failed to resolve installation path for: {Path}",
            installation.InstallationPath);

        return OperationResult<GameInstallation>.CreateFailure(
            $"Could not resolve installation path: {installation.InstallationPath}");
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> ValidateInstallationPathAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(installation.InstallationPath))
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure("Installation path is null or empty"));
        }

        if (!Directory.Exists(installation.InstallationPath))
        {
            return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
        }

        // Check game-specific subdirectories and the root installation directory
        // against the shared valid executable list so all locations accept
        // every recognized edition (Steam, retail, SuperHackers, GeneralsOnline, Contra).
        var hasValidFiles =
            (installation.HasGenerals && InstallationExtensions.HasValidGameExecutable(installation.GeneralsPath)) ||
            (installation.HasZeroHour && InstallationExtensions.HasValidGameExecutable(installation.ZeroHourPath)) ||
            ((installation.HasGenerals || installation.HasZeroHour) && InstallationExtensions.HasValidGameExecutable(installation.InstallationPath));

        if (!hasValidFiles)
        {
            logger.LogDebug(
                "Installation path {Path} does not contain valid game files",
                installation.InstallationPath);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(false));
        }

        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> SearchForInstallationAsync(
        GameInstallation installation,
        string? gameDatHash = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation(
            "Searching for {InstallationType} installation...",
            installation.InstallationType);

        var searchPaths = GetSearchPaths(installation.InstallationType);

        // Compute hash of existing game.dat if available and not explicitly provided
        gameDatHash ??= await TryComputeExistingGameDatHashAsync(
            installation.InstallationPath,
            cancellationToken);

        // Search each path
        foreach (var searchPath in searchPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!Directory.Exists(searchPath))
                {
                    continue;
                }

                logger.LogDebug("Searching in: {SearchPath}", searchPath);

                var foundPath = await SearchDirectoryForInstallationAsync(
                    searchPath,
                    installation,
                    gameDatHash,
                    cancellationToken);

                if (!string.IsNullOrEmpty(foundPath))
                {
                    logger.LogInformation("Found installation at: {FoundPath}", foundPath);
                    return OperationResult<string>.CreateSuccess(foundPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Error searching directory: {SearchPath}", searchPath);
            }
        }

        logger.LogWarning(
            "Could not find {InstallationType} installation in any common location",
            installation.InstallationType);

        return OperationResult<string>.CreateFailure(
            "Installation not found in common locations");
    }

    private static GameInstallation CreateResolvedInstallation(
        GameInstallation originalInstallation,
        string resolvedPath)
    {
        var updatedInstallation = new GameInstallation(
            resolvedPath,
            originalInstallation.InstallationType)
        {
            Id = originalInstallation.Id,
            DisplayName = originalInstallation.DisplayName,
            DetectedAt = originalInstallation.DetectedAt,
        };

        var generalsPath = originalInstallation.HasGenerals
            ? ResolveGeneralsSubPath(resolvedPath)
            : null;

        var zeroHourPath = originalInstallation.HasZeroHour
            ? ResolveZeroHourSubPath(resolvedPath)
            : null;

        updatedInstallation.SetPaths(generalsPath, zeroHourPath);
        updatedInstallation.PopulateGameClients(originalInstallation.AvailableGameClients);

        return updatedInstallation;
    }

    private static string ResolveGeneralsSubPath(string resolvedPath)
    {
        return resolvedPath.TryGetDirectoryCaseInsensitive(
            GameClientConstants.GeneralsSubdirectoryName,
            out var subPath)
            ? subPath
            : resolvedPath;
    }

    private static string ResolveZeroHourSubPath(string resolvedPath)
    {
        if (resolvedPath.TryGetDirectoryCaseInsensitive(
            GameClientConstants.ZeroHourDirectoryName,
            out var standardPath))
        {
            return standardPath;
        }

        return resolvedPath.TryGetDirectoryCaseInsensitive(
            GameClientConstants.ZeroHourSubdirectoryName,
            out var shortPath)
            ? shortPath
            : resolvedPath;
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

    private async Task<string?> TryComputeExistingGameDatHashAsync(
        string? installationPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(installationPath) || !Directory.Exists(installationPath))
        {
            return null;
        }

        var gameDatPath = Path.Combine(installationPath, GameClientConstants.SteamGameDatExecutable);
        if (!gameDatPath.TryGetFileCaseInsensitive(out var resolvedGameDatPath))
        {
            return null;
        }

        try
        {
            return await ComputeFileHashAsync(resolvedGameDatPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not compute hash of game.dat at {Path}", resolvedGameDatPath);
            return null;
        }
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
                cancellationToken.ThrowIfCancellationRequested();

                // Check if this directory contains game files
                if (await IsValidGameInstallationAsync(dir, installation, gameDatHash, cancellationToken))
                {
                    return dir;
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // Skip directories we don't have access to
            logger.LogDebug(ex, "Access denied to directory: {SearchPath}", searchPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Error searching directory: {SearchPath}", searchPath);
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
                if (!gameDatCandidate.TryGetFileCaseInsensitive(out var resolvedGameDatPath))
                {
                    return false;
                }

                var hash = await ComputeFileHashAsync(resolvedGameDatPath, cancellationToken);
                if (!string.Equals(hash, gameDatHash, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Check for game type specific files
            if (installation.HasZeroHour)
            {
                // Zero Hour has DbgHelp.dll or Zero Hour signature files
                var dbgHelpDll = Path.Combine(directory, GameClientConstants.DbgHelpDll);
                if (dbgHelpDll.FileExistsCaseInsensitive() ||
                    Path.Combine(directory, GameClientConstants.ZeroHourIniBig).FileExistsCaseInsensitive() ||
                    Path.Combine(directory, GameClientConstants.ZeroHourPatchBig).FileExistsCaseInsensitive())
                {
                    return true;
                }
            }

            if (installation.HasGenerals)
            {
                // Having a Generals-specific executable is enough for Generals
                if (Path.Combine(directory, GameClientConstants.GeneralsExecutable).FileExistsCaseInsensitive() ||
                    Path.Combine(directory, GameClientConstants.SuperHackersGeneralsExecutable).FileExistsCaseInsensitive() ||
                    Path.Combine(directory, GameClientConstants.ContraExecutable).FileExistsCaseInsensitive())
                {
                    return true;
                }

                // If only a generic executable (e.g. game.exe, game.dat) was matched, require corroborating Generals files or hash
                if (Path.Combine(directory, GameClientConstants.GeneralsIniBig).FileExistsCaseInsensitive() ||
                    Path.Combine(directory, GameClientConstants.GeneralsPatchBig).FileExistsCaseInsensitive() ||
                    Path.Combine(directory, GameClientConstants.DbgHelpDll).FileExistsCaseInsensitive() ||
                    !string.IsNullOrEmpty(gameDatHash))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Error checking directory: {Directory}", directory);
            return false;
        }
    }
}
