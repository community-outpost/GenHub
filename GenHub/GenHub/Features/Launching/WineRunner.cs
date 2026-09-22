using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
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
    ILogger<WineRunner> logger,
    ILocalizationService? localizationService = null) : IGameLaunchRunner
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

        var executablePath = configuration.ExecutablePath;
        if (executablePath.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<RunnerCommand>.CreateFailure(RunnerTargetResolver.FlatpakGuidance(executablePath, logger, localizationService));
        }

        var mismatch = LaunchGuardMessages.GetCrossOsError(ExecutableFileClassifier.DetectPlatform(executablePath), localizationService);
        if (mismatch is not null)
        {
            logger.LogWarning("Launch blocked by OS guard: {Error} ({ExecutablePath})", mismatch, executablePath);
            return OperationResult<RunnerCommand>.CreateFailure(mismatch);
        }

        executablePath = RunnerTargetResolver.ResolveBundleTarget(executablePath, logger);
        if (executablePath is null)
        {
            return OperationResult<RunnerCommand>.CreateFailure(
                RunnerTargetResolver.Localize(localizationService, LaunchMessageConstants.BundleUnresolvableKey, LaunchMessageConstants.BundleUnresolvable, configuration.ExecutablePath));
        }

        mismatch = LaunchGuardMessages.GetCrossOsError(ExecutableFileClassifier.DetectPlatform(executablePath), localizationService);
        if (mismatch is not null)
        {
            logger.LogWarning("Launch blocked by OS guard: {Error} ({ExecutablePath})", mismatch, executablePath);
            return OperationResult<RunnerCommand>.CreateFailure(mismatch);
        }

        // Extension decides first; content decides second. A Windows binary without the
        // .exe extension (Steam launches game.dat, which is a PE) must still run under
        // Wine: passing it through to a native exec can only fail with ENOEXEC.
        if (!CommandLineHelper.IsWindowsExecutable(executablePath)
            && ExecutableFileClassifier.DetectPlatform(executablePath) != ExecutablePlatform.Windows)
        {
            logger.LogDebug("Target {ExecutablePath} is not a Windows executable; passing through directly", executablePath);
            return OperationResult<RunnerCommand>.CreateSuccess(
                new RunnerCommand(executablePath, string.Empty, new Dictionary<string, string>()));
        }

        if (!TryFindWineBinary(out var wineBinary))
        {
            logger.LogWarning("Wine binary not found; cannot launch {ExecutablePath}", executablePath);
            return OperationResult<RunnerCommand>.CreateFailure(
                $"Wine binary not found (searched {string.Join(", ", options.BinaryNames)}). Install Wine to launch Windows games.");
        }

        EnsurePrefix();
        MirrorOptionsIni(configuration);

        logger.LogInformation("Launching {ExecutablePath} through Wine ({WineBinary})", executablePath, wineBinary);
        var environment = new Dictionary<string, string>(configuration.EnvironmentVariables)
        {
            [WineConstants.PrefixEnvironmentVariable] = options.PrefixPath,
        };

        ConfigureDirect3DOverride(configuration, environment);

        return OperationResult<RunnerCommand>.CreateSuccess(
            new RunnerCommand(wineBinary, CommandLineHelper.QuoteArgument(executablePath), environment));
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
    /// Ensures that IPAddress = 0.0.0.0 is present in the native Options.ini to prevent Winsock hang under Wine.
    /// </summary>
    /// <param name="iniPath">The Options.ini file path.</param>
    private static void EnsureNetworkIpAddress(string iniPath)
    {
        try
        {
            if (!File.Exists(iniPath))
            {
                return;
            }

            var text = File.ReadAllText(iniPath);
            if (!text.Contains("IPAddress", StringComparison.OrdinalIgnoreCase))
            {
                File.AppendAllText(iniPath, "\r\nIPAddress = 0.0.0.0\r\n");
            }
        }
        catch
        {
            // Best-effort to prevent Winsock adapter enumeration hang under Wine.
        }
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

    private static bool IsExcludedPrefixUser(string dirName) =>
        WineConstants.ExcludedPrefixUserNames.Contains(dirName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Collects the prefix user profiles targeted when mirroring the native Options.ini:
    /// the current login plus every existing non-system profile directory.
    /// </summary>
    /// <param name="usersRoot">The prefix users directory.</param>
    /// <returns>The candidate user profile names, compared case-insensitively.</returns>
    private static HashSet<string> CollectCandidateUsernames(string usersRoot)
    {
        var candidateUsernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SanitizeUserName(Environment.UserName),
        };

        if (!Directory.Exists(usersRoot))
        {
            return candidateUsernames;
        }

        foreach (var dir in Directory.EnumerateDirectories(usersRoot))
        {
            var dirName = Path.GetFileName(dir);
            if (!string.IsNullOrWhiteSpace(dirName) && !IsExcludedPrefixUser(dirName))
            {
                candidateUsernames.Add(dirName);
            }
        }

        return candidateUsernames;
    }

    /// <summary>
    /// Mirrors the native Options.ini into every candidate user shell folder.
    /// </summary>
    /// <remarks>
    /// Plain Wine resolves the personal shell folder to "My Documents" while Proton
    /// uses "Documents", and on a fresh managed prefix neither directory exists yet to
    /// disambiguate. Guessing one risks the game silently ignoring the mirrored
    /// Options.ini — including the profile's resolution — with no error anywhere, and
    /// the guess can never self-heal because the first run creates the guessed
    /// directory itself. Mirror into both: the game reads exactly one, and the other
    /// copy stays inert.
    /// </remarks>
    /// <param name="sourcePath">The native Options.ini path.</param>
    /// <param name="usersRoot">The prefix users directory.</param>
    /// <param name="candidateUsernames">The user profiles to mirror into.</param>
    /// <param name="dataDirectoryName">The game data directory name.</param>
    /// <returns><c>true</c> when at least one destination was written; otherwise, <c>false</c>.</returns>
    private static bool MirrorToCandidateUsers(string sourcePath, string usersRoot, HashSet<string> candidateUsernames, string dataDirectoryName)
    {
        var mirroredAny = false;
        foreach (var userName in candidateUsernames)
        {
            var userDirectory = Path.Combine(usersRoot, userName);
            foreach (var documentsDirectoryName in new[] { WineConstants.DocumentsDirectoryName, WineConstants.MyDocumentsDirectoryName })
            {
                mirroredAny |= MirrorOptionsIniToShellFolder(sourcePath, userDirectory, documentsDirectoryName, dataDirectoryName);
            }
        }

        return mirroredAny;
    }

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

    /// <summary>
    /// Determines whether the configured prefix is Proton-managed from the steamuser
    /// profile, a compatdata path, or Proton's tracked-files marker.
    /// </summary>
    /// <remarks>
    /// <c>tracked_files</c> lives in the compatdata root, next to the <c>pfx</c> prefix
    /// directory rather than inside it, so the parent directory is probed as well as
    /// the prefix itself (the latter covers prefix paths pointing at a compatdata root).
    /// </remarks>
    /// <param name="usersRoot">The prefix users directory.</param>
    /// <returns><c>true</c> when the prefix looks Proton-managed; otherwise, <c>false</c>.</returns>
    private bool IsProtonPrefix(string usersRoot)
    {
        if (Directory.Exists(Path.Combine(usersRoot, WineConstants.ProtonUserName)))
        {
            return true;
        }

        if (options.PrefixPath.Contains(WineConstants.CompatDataDirectoryMarker, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parent = Directory.GetParent(options.PrefixPath);
        return File.Exists(Path.Combine(options.PrefixPath, WineConstants.ProtonTrackedFilesMarker))
            || (parent is not null && File.Exists(Path.Combine(parent.FullName, WineConstants.ProtonTrackedFilesMarker)));
    }

    /// <summary>
    /// Writes a baseline native Options.ini so a first launch has settings to mirror.
    /// </summary>
    /// <param name="sourcePath">The native Options.ini path to create.</param>
    /// <param name="args">The launch arguments, used for the bootstrap resolution when present.</param>
    /// <returns><c>true</c> when the baseline was written; otherwise, <c>false</c>.</returns>
    private bool BootstrapNativeOptionsIni(string sourcePath, Dictionary<string, string>? args)
    {
        try
        {
            var dir = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var resolutionLine = $"{GameSettingsIniConstants.ResolutionKey} = {WineConstants.BootstrapResolutionWidth} {WineConstants.BootstrapResolutionHeight}";
            if (args is { Count: > 0 }
                && args.TryGetValue(GameClientConstants.XResolutionArgument, out var xres) && !string.IsNullOrWhiteSpace(xres)
                && args.TryGetValue(GameClientConstants.YResolutionArgument, out var yres) && !string.IsNullOrWhiteSpace(yres))
            {
                resolutionLine = $"{GameSettingsIniConstants.ResolutionKey} = {xres} {yres}";
            }

            File.WriteAllText(sourcePath, $"{resolutionLine}\r\n{GameSettingsIniConstants.IdealStaticGameLODKey} = {WineConstants.BootstrapIdealStaticGameLOD}\r\nIPAddress = 0.0.0.0\r\n");
            logger.LogInformation("Bootstrapped baseline Options.ini at {SourcePath}", sourcePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogDebug(ex, "Could not bootstrap missing native Options.ini at {SourcePath}", sourcePath);
            return false;
        }
    }

    private void MirrorOptionsIni(GameLaunchConfiguration configuration)
    {
        if (configuration.GameType is null || string.IsNullOrWhiteSpace(configuration.NativeOptionsIniPath))
        {
            return;
        }

        var sourcePath = configuration.NativeOptionsIniPath;
        if (!File.Exists(sourcePath) && !BootstrapNativeOptionsIni(sourcePath, configuration.Arguments))
        {
            return;
        }

        EnsureNetworkIpAddress(sourcePath);

        try
        {
            var dataDirectoryName = configuration.GameType == GameType.ZeroHour
                ? MapManagerConstants.ZeroHourDataDirectoryName
                : MapManagerConstants.GeneralsDataDirectoryName;

            var usersRoot = Path.Combine(
                options.PrefixPath,
                WineConstants.DriveCDirectoryName,
                WineConstants.PrefixUsersDirectoryName);

            var candidateUsernames = CollectCandidateUsernames(usersRoot);
            if (IsProtonPrefix(usersRoot))
            {
                candidateUsernames.Add(WineConstants.ProtonUserName);
            }

            if (MirrorToCandidateUsers(sourcePath, usersRoot, candidateUsernames, dataDirectoryName))
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
