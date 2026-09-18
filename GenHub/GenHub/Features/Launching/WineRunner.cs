using GenHub.Core.Constants;
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

        if (!IsWindowsExecutable(configuration.ExecutablePath))
        {
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
        var environment = new Dictionary<string, string>
        {
            [WineConstants.PrefixEnvironmentVariable] = options.PrefixPath,
        };

        return OperationResult<RunnerCommand>.CreateSuccess(
            new RunnerCommand(wineBinary, QuoteArgument(configuration.ExecutablePath), environment));
    }

    private static bool IsWindowsExecutable(string executablePath)
    {
        return WineConstants.WindowsExecutableExtension.Equals(Path.GetExtension(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteArgument(string value)
    {
        return value.Contains(' ') || value.Contains('\t') ? $"\"{value}\"" : value;
    }

    private static string SanitizeUserName(string userName)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .ToHashSet();
        var clean = new string(userName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? WineConstants.FallbackPrefixUserName : clean;
    }

    private bool TryFindWineBinary([NotNullWhen(true)] out string? wineBinary)
    {
        wineBinary = null;

        foreach (var absolutePath in options.AbsoluteBinaryPaths)
        {
            if (!string.IsNullOrWhiteSpace(absolutePath) && File.Exists(absolutePath))
            {
                wineBinary = absolutePath;
                return true;
            }
        }

        var pathVariable = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        var pathDirectories = string.IsNullOrEmpty(pathVariable)
            ? []
            : pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in options.ExtraSearchDirectories.Concat(pathDirectories))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (var binaryName in options.BinaryNames)
            {
                var candidate = Path.Combine(directory, binaryName);
                if (File.Exists(candidate))
                {
                    wineBinary = candidate;
                    return true;
                }
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
            var userDocuments = Path.Combine(
                options.PrefixPath,
                WineConstants.DriveCDirectoryName,
                WineConstants.PrefixUsersDirectoryName,
                SanitizeUserName(Environment.UserName),
                WineConstants.MyDocumentsDirectoryName,
                dataDirectoryName);
            Directory.CreateDirectory(userDocuments);

            var destinationPath = Path.Combine(userDocuments, Path.GetFileName(sourcePath));
            if (!File.Exists(destinationPath) || File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(destinationPath))
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
                logger.LogInformation("Mirrored Options.ini into Wine prefix for {GameType}", configuration.GameType);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(ex, "Failed to mirror Options.ini into Wine prefix; continuing launch with prefix defaults");
        }
    }
}
