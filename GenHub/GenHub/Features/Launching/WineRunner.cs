using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace GenHub.Features.Launching;

/// <summary>
/// Launches Windows executables through Wine on Linux and macOS.
/// </summary>
public class WineRunner(
    WineRunnerOptions options,
    ILogger<WineRunner> logger) : IGameLaunchRunner
{
    /// <inheritdoc/>
    public string Name => "Wine";

    /// <summary>Gets the configured runner options.</summary>
    public WineRunnerOptions Options => options;

    /// <inheritdoc/>
    public bool CanLaunchWindowsExecutables()
    {
        return TryFindWineBinary(out _);
    }

    /// <inheritdoc/>
    public OperationResult<RunnerCommand> ResolveCommand(GameLaunchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!CommandLineHelper.IsWindowsExecutable(configuration.ExecutablePath))
        {
            logger.LogDebug("Target {ExecutablePath} is not a Windows executable; passing through directly", configuration.ExecutablePath);
            return OperationResult<RunnerCommand>.CreateSuccess(
                new RunnerCommand(configuration.ExecutablePath, string.Empty, new Dictionary<string, string>()));
        }

        if (!TryFindWineBinary(out var wineBinary))
        {
            logger.LogWarning("Wine binary not found; cannot launch {ExecutablePath}", configuration.ExecutablePath);
            return OperationResult<RunnerCommand>.CreateFailure(
                $"Wine binary not found (searched {string.Join(", ", options.BinaryNames)}). Install Wine to launch Windows games.");
        }

        EnsurePrefix();
        MirrorOptionsIni(configuration);

        logger.LogInformation("Launching {ExecutablePath} through Wine ({WineBinary})", configuration.ExecutablePath, wineBinary);
        var environment = new Dictionary<string, string>(configuration.EnvironmentVariables)
        {
            [WineConstants.PrefixEnvironmentVariable] = options.PrefixPath,
        };

        ConfigureDirect3DOverride(configuration, environment);

        return OperationResult<RunnerCommand>.CreateSuccess(
            new RunnerCommand(wineBinary, CommandLineHelper.QuoteArgument(configuration.ExecutablePath), environment));
    }

    private static string SanitizeUserName(string userName)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .ToHashSet();
        var clean = new string(userName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? WineConstants.FallbackPrefixUserName : clean;
    }

    /// <summary>
    /// Copies the native Options.ini into one prefix user shell folder when the source is newer.
    /// </summary>
    /// <param name="sourcePath">The native Options.ini path.</param>
    /// <param name="userDirectory">The prefix user profile directory.</param>
    /// <param name="documentsDirectoryName">The shell folder name ("Documents" or "My Documents").</param>
    /// <param name="dataDirectoryName">The game data directory name.</param>
    /// <returns><c>true</c> when the destination was written; otherwise, <c>false</c>.</returns>
    private static bool MirrorOptionsIniToShellFolder(string sourcePath, string userDirectory, string documentsDirectoryName, string dataDirectoryName)
    {
        var userDocuments = Path.Combine(userDirectory, documentsDirectoryName, dataDirectoryName);
        Directory.CreateDirectory(userDocuments);

        var destinationPath = Path.Combine(userDocuments, Path.GetFileName(sourcePath));
        if (File.Exists(destinationPath) && File.GetLastWriteTimeUtc(sourcePath) <= File.GetLastWriteTimeUtc(destinationPath))
        {
            return false;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
        return true;
    }

    /// <summary>
    /// Determines whether a WINEDLLOVERRIDES value already configures the Direct3D 8 DLL.
    /// Entries are separated by semicolons, and each entry names one or more comma-separated
    /// DLLs before the load order, so only an exact DLL-name match counts: a substring check
    /// would mistake d3d8proxy for d3d8 and leave the wrapper override unset.
    /// </summary>
    /// <param name="existingOverrides">The current WINEDLLOVERRIDES value.</param>
    /// <returns><c>true</c> when an entry already names the Direct3D 8 DLL; otherwise, <c>false</c>.</returns>
    private static bool HasDirect3D8Override(string existingOverrides) =>
        existingOverrides
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(entry => entry
                .Split('=', 2)[0]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(key => string.Equals(key, WineConstants.Direct3D8DllName, StringComparison.OrdinalIgnoreCase));

    private void ConfigureDirect3DOverride(GameLaunchConfiguration configuration, Dictionary<string, string> environment)
    {
        var workingDir = configuration.WorkingDirectory ?? Path.GetDirectoryName(configuration.ExecutablePath);
        if (string.IsNullOrEmpty(workingDir) || !File.Exists(Path.Combine(workingDir, GameClientConstants.Direct3D8WrapperDll)))
        {
            return;
        }

        if (environment.TryGetValue(WineConstants.DllOverridesEnvironmentVariable, out var existingOverrides))
        {
            if (!HasDirect3D8Override(existingOverrides))
            {
                environment[WineConstants.DllOverridesEnvironmentVariable] = $"{existingOverrides};{WineConstants.Direct3D8DllOverride}";
                logger.LogInformation("Configured Wine DLL override for {Dll} (appended to existing overrides)", GameClientConstants.Direct3D8WrapperDll);
            }
        }
        else
        {
            environment[WineConstants.DllOverridesEnvironmentVariable] = WineConstants.Direct3D8DllOverride;
            logger.LogInformation("Configured Wine DLL override for {Dll}", GameClientConstants.Direct3D8WrapperDll);
        }
    }

    private bool TryFindWineBinary([NotNullWhen(true)] out string? wineBinary)
    {
        wineBinary = options.AbsoluteBinaryPaths
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));

        if (wineBinary is not null)
        {
            return true;
        }

        var pathVariable = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        var pathDirectories = string.IsNullOrEmpty(pathVariable)
            ? []
            : pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var searchDirectories = options.ExtraSearchDirectories
            .Concat(pathDirectories)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToArray();

        foreach (var binaryName in options.BinaryNames)
        {
            var match = searchDirectories
                .Select(d => Path.Combine(d, binaryName))
                .FirstOrDefault(File.Exists);

            if (match is not null)
            {
                wineBinary = match;
                return true;
            }
        }

        return false;
    }

    private void EnsurePrefix()
    {
        try
        {
            Directory.CreateDirectory(options.PrefixPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(ex, "Failed to create Wine prefix directory {PrefixPath}; Wine will attempt first-run initialization", options.PrefixPath);
        }
    }

    private void MirrorOptionsIni(GameLaunchConfiguration configuration)
    {
        if (configuration.GameType is null || string.IsNullOrWhiteSpace(configuration.NativeOptionsIniPath))
        {
            return;
        }

        var sourcePath = configuration.NativeOptionsIniPath;
        if (!File.Exists(sourcePath))
        {
            return;
        }

        try
        {
            var dataDirectoryName = configuration.GameType == GameType.ZeroHour
                ? MapManagerConstants.ZeroHourDataDirectoryName
                : MapManagerConstants.GeneralsDataDirectoryName;
            var userDirectory = Path.Combine(
                options.PrefixPath,
                WineConstants.DriveCDirectoryName,
                WineConstants.PrefixUsersDirectoryName,
                SanitizeUserName(Environment.UserName));

            // Plain Wine resolves the personal shell folder to "My Documents" while Proton
            // uses "Documents", and on a fresh managed prefix neither directory exists yet to
            // disambiguate. Guessing one risks the game silently ignoring the mirrored
            // Options.ini — including the profile's resolution — with no error anywhere, and
            // the guess can never self-heal because the first run creates the guessed
            // directory itself. Mirror into both: the game reads exactly one, and the other
            // copy stays inert.
            var mirroredAny = false;
            foreach (var documentsDirectoryName in new[] { WineConstants.DocumentsDirectoryName, WineConstants.MyDocumentsDirectoryName })
            {
                mirroredAny |= MirrorOptionsIniToShellFolder(sourcePath, userDirectory, documentsDirectoryName, dataDirectoryName);
            }

            if (mirroredAny)
            {
                logger.LogInformation("Mirrored Options.ini into Wine prefix for {GameType}", configuration.GameType);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(ex, "Failed to mirror Options.ini into Wine prefix; continuing launch with prefix defaults");
        }
    }
}
