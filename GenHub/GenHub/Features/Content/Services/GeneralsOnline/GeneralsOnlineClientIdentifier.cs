using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Identifies Generals Online game client executables.
/// </summary>
public class GeneralsOnlineClientIdentifier : IGameClientIdentifier
{
    /// <inheritdoc/>
    public string PublisherId => PublisherTypeConstants.GeneralsOnline;

    /// <inheritdoc/>
    public bool CanIdentify(string executablePath) =>
        IsSupportedEntryPoint(Path.GetFileName(executablePath.Replace('\\', '/')));

    /// <inheritdoc/>
    public GameClientIdentification? Identify(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath.Replace('\\', '/'));
        if (!IsSupportedEntryPoint(fileName))
        {
            return null;
        }

        var isTestEnv = fileName.Equals(GameClientConstants.GeneralsOnlineTestEnvironmentExecutable, StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals(GameClientConstants.GeneralsOnlineDefaultExecutable, StringComparison.OrdinalIgnoreCase);

        return new GameClientIdentification(
            publisherId: PublisherTypeConstants.GeneralsOnline,
            variant: isTestEnv ? GeneralsOnlineConstants.VariantTestEnvironmentSuffix : GeneralsOnlineConstants.Variant60HzSuffix,
            displayName: isTestEnv ? GameClientConstants.GeneralsOnlineTestEnvironmentDisplayName : GameClientConstants.GeneralsOnline60HzDisplayName,
            gameType: GameType.ZeroHour,
            localVersion: null); // Don't fetch from web during detection!
    }

    /// <inheritdoc/>
    public string? ResolveDirectoryEntryPoint(IEnumerable<string> fileNames) =>
        ResolveDirectoryEntryPoint(string.Empty, fileNames);

    /// <inheritdoc/>
    public string? ResolveDirectoryEntryPoint(string directory, IEnumerable<string> fileNames)
    {
        var (eacLauncher, sixtyHertz, unixClient) = CategorizeExecutables(fileNames);
        if (eacLauncher is not null)
        {
            return ResolveEacEntryPoint(directory, eacLauncher, sixtyHertz);
        }

        return sixtyHertz ?? unixClient;
    }

    /// <inheritdoc/>
    public bool IsCompanionFile(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath.Replace('\\', '/'));
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (fileName.Equals(GameClientConstants.GeneralsOnlineEacLauncherExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        if (!HasSiblingBootstrapper(directory))
        {
            return false;
        }

        if (fileName.Equals(GameClientConstants.GeneralsOnlineDefaultExecutable, StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals(GameClientConstants.GeneralsOnlineUnixExecutable, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (GeneralsOnlineEacSettings.TryRead(directory, out var settings) && settings is not null)
        {
            var configured = GeneralsOnlineEacSettings.NormalizeExecutableName(settings.Executable);
            return fileName.Equals(configured, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Determines whether a file name is a supported Generals Online entry point.
    /// Since 060526_QFE1 the Easy Anti-Cheat bootstrapper or 60Hz binary serves the 60Hz client,
    /// while GeneralsOnlineZH_TestEnvironment.exe (or GeneralsOnlineZH.exe) serves the test environment client without Easy Anti-Cheat.
    /// </summary>
    private static bool IsSupportedEntryPoint(string fileName) =>
        GameClientConstants.GeneralsOnlineExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    private static bool HasSiblingBootstrapper(string directory)
    {
        var bootstrapper = Path.Combine(directory, GameClientConstants.GeneralsOnlineEacLauncherExecutable);
        try
        {
            if (File.Exists(bootstrapper))
            {
                return true;
            }

            return Directory.EnumerateFiles(directory)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Any(candidate => candidate.Equals(
                    GameClientConstants.GeneralsOnlineEacLauncherExecutable,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static (string? EacLauncher, string? SixtyHertz, string? UnixClient) CategorizeExecutables(IEnumerable<string> fileNames)
    {
        string? eacLauncher = null;
        string? sixtyHertz = null;
        string? unixClient = null;

        foreach (var fileName in fileNames)
        {
            if (fileName.Equals(GameClientConstants.GeneralsOnlineEacLauncherExecutable, StringComparison.OrdinalIgnoreCase))
            {
                eacLauncher = fileName;
            }
            else if (fileName.Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase))
            {
                sixtyHertz = fileName;
            }
            else if (fileName.Equals(GameClientConstants.GeneralsOnlineUnixExecutable, StringComparison.OrdinalIgnoreCase))
            {
                unixClient = fileName;
            }
        }

        return (eacLauncher, sixtyHertz, unixClient);
    }

    private static string ResolveEacEntryPoint(string directory, string eacLauncher, string? sixtyHertz)
    {
        if (!string.IsNullOrEmpty(directory)
            && GeneralsOnlineEacSettings.TryRead(directory, out var settings)
            && settings is not null)
        {
            var configured = Path.GetFileName(settings.Executable.Replace('\\', '/'));
            if (configured.Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase))
            {
                return eacLauncher;
            }

            // If EAC settings target something other than 60Hz (e.g. TestEnvironment),
            // the live 60Hz client takes precedence if present.
            if (sixtyHertz is not null)
            {
                return sixtyHertz;
            }
        }

        return eacLauncher;
    }
}
