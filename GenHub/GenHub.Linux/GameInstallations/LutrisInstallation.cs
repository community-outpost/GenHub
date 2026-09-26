using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Linux.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Linux.GameInstallations;

/// <summary>
/// Lutris installation detector and manager for Linux.
/// </summary>
public partial class LutrisInstallation(ILogger<LutrisInstallation>? logger = null) : IGameInstallation
{
    private readonly Func<string, string[], (bool Success, string Output)>? _processRunner;

    [GeneratedRegex(@"^lutris-([\d\.]*)$")]
    private static partial Regex LutrisVersionRegex();

    [GeneratedRegex(@"\[[\s\S]*\]")]
    private static partial Regex LutrisGamesRegex();

    /// <summary>
    /// Initializes a new instance of the <see cref="LutrisInstallation"/> class.
    /// </summary>
    /// <param name="fetch">Value indicating whether <see cref="Fetch"/> should be called while instantiation.</param>
    /// <param name="logger">Optional logger instance.</param>
    public LutrisInstallation(bool fetch, ILogger<LutrisInstallation>? logger = null)
        : this(logger)
    {
        if (fetch)
        {
            Fetch();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LutrisInstallation"/> class with a custom command runner for testing.
    /// </summary>
    /// <param name="processRunner">Custom command runner delegate used for process isolation.</param>
    /// <param name="logger">Optional logger instance.</param>
    internal LutrisInstallation(
        Func<string, string[], (bool Success, string Output)> processRunner,
        ILogger<LutrisInstallation>? logger = null)
        : this(logger)
    {
        _processRunner = processRunner;
    }

    /// <inheritdoc/>
    public string Id => string.IsNullOrWhiteSpace(InstallationPath)
        ? "Lutris"
        : GameInstallation.CreateStableId(InstallationType, InstallationPath);

    /// <inheritdoc/>
    public GameInstallationType InstallationType => GameInstallationType.Lutris;

    /// <inheritdoc/>
    public string InstallationPath { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasGenerals { get; private set; }

    /// <inheritdoc/>
    public string GeneralsPath { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasZeroHour { get; private set; }

    /// <inheritdoc/>
    public string ZeroHourPath { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public List<GameClient> AvailableGameClients { get; private set; } = [];

    /// <summary>
    /// Gets a value indicating whether Lutris is installed successfully.
    /// </summary>
    public bool IsLutrisInstalled { get; private set; }

    /// <summary>
    /// Gets Lutris installation Type.
    /// </summary>
    public LinuxInstallationType PackageInstallationType { get; private set; }

    /// <summary>
    /// Gets the value of Lutris Version.
    /// </summary>
    public string LutrisVersion { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public void Fetch()
    {
        logger?.LogInformation("Starting Lutris installation detection on Linux");

        IsLutrisInstalled = false;
        InstallationPath = string.Empty;
        LutrisVersion = string.Empty;
        PackageInstallationType = LinuxInstallationType.Binary;
        HasZeroHour = false;
        HasGenerals = false;
        ZeroHourPath = string.Empty;
        GeneralsPath = string.Empty;

        try
        {
            var lutrisExecutables = new Dictionary<string, LinuxInstallationType>
            {
                { "lutris", LinuxInstallationType.Binary },
                { "flatpak run net.lutris.Lutris", LinuxInstallationType.Flatpack },
                { "snap run lutris", LinuxInstallationType.Snap },
            };
            foreach (var entry in lutrisExecutables)
            {
                // check for lutris
                if (!TryLutris(entry.Key, out var version) ||
                    !TryLutrisHasZH(entry.Key, out var directory)) continue;
                var homeDir = Path.Combine(
                    directory,
                    $"drive_c/Program Files/EA Games/{GameClientConstants.ZeroHourDirectoryName}/");

                // Check if EA app and Generals/ZH are installed
                if (Directory.Exists(homeDir))
                {
                    IsLutrisInstalled = true;
                    InstallationPath = homeDir;
                    LutrisVersion = version;
                    PackageInstallationType = entry.Value;
                    if (Directory.Exists(Path.Combine(homeDir, GameClientConstants.ZeroHourDirectoryName)))
                    {
                        HasZeroHour = true;
                        ZeroHourPath = Path.Combine(homeDir, GameClientConstants.ZeroHourDirectoryName);
                    }

                    if (Directory.Exists(Path.Combine(homeDir, GameClientConstants.GeneralsDirectoryName)))
                    {
                        HasGenerals = true;
                        GeneralsPath = Path.Combine(homeDir, GameClientConstants.GeneralsDirectoryName);
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error occurred during Lutris installation detection on Linux");
            IsLutrisInstalled = false;
        }
    }

    /// <inheritdoc/>
    public void SetPaths(string? generalsPath, string? zeroHourPath)
    {
        if (!string.IsNullOrEmpty(generalsPath))
        {
            GeneralsPath = generalsPath;
            HasGenerals = true;
        }

        if (!string.IsNullOrEmpty(zeroHourPath))
        {
            ZeroHourPath = zeroHourPath;
            HasZeroHour = true;
        }
    }

    /// <inheritdoc/>
    public void PopulateGameClients(IEnumerable<GameClient> clients)
    {
        AvailableGameClients.AddRange(clients);
    }

    private static ProcessStartInfo CreateLutrisStartInfo(string command, string[] extraArgs)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var fileName = parts[0];
        var psi = new ProcessStartInfo
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        for (int i = 1; i < parts.Length; i++)
        {
            psi.ArgumentList.Add(parts[i]);
        }

        foreach (var arg in extraArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    private bool RunLutrisCommand(string installationPath, string[] args, out string output)
    {
        output = string.Empty;
        if (_processRunner != null)
        {
            var result = _processRunner(installationPath, args);
            output = result.Output;
            return result.Success;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = CreateLutrisStartInfo(installationPath, args),
            };

            if (!process.Start())
            {
                return false;
            }

            // Drain standard output asynchronously with cancellation and bounded wait to avoid deadlocks, hangs, or thread leaks
            using var cts = new CancellationTokenSource(ProcessConstants.ExternalCliTimeoutMs);
            var readOutputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            if (process.WaitForExit(ProcessConstants.ExternalCliTimeoutMs) && readOutputTask.Wait(ProcessConstants.ExternalCliTimeoutMs))
            {
                output = readOutputTask.Result;
                return process.ExitCode == 0;
            }

            try
            {
                cts.Cancel();
            }
            catch (Exception)
            {
                // Ignore cancellation failure
            }

            try
            {
                process.StandardOutput.Close();
            }
            catch (Exception)
            {
                // Ignore stream close failure
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Failed to terminate timed-out Lutris process.");
            }

            try
            {
                readOutputTask.Wait(ProcessConstants.ProcessKillWaitMs);
            }
            catch (Exception)
            {
                // Bounded wait to ensure readOutputTask finishes before process is disposed
            }

            return false;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to execute Lutris command: {InstallationPath} {Args}", installationPath, string.Join(" ", args));
            return false;
        }
    }

    private bool TryLutris(string installationPath, out string lutrisVersion)
    {
        lutrisVersion = string.Empty;
        if (!RunLutrisCommand(installationPath, ["-v"], out var output))
            return false;

        foreach (var item in output.Split(Environment.NewLine))
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;

            // check for lutris, if installed version is printed
            var match = LutrisVersionRegex().Match(item);
            if (match.Success && match.Groups.Count > 1)
            {
                lutrisVersion = match.Groups[1].Value;
                return true;
            }
        }

        return false;
    }

    private bool TryLutrisHasZH(string installationPath, out string directory)
    {
        directory = string.Empty;
        if (!RunLutrisCommand(installationPath, ["-l", "-j"], out var output))
            return false;

        var jsonMatch = LutrisGamesRegex().Match(output);
        if (!jsonMatch.Success)
            return false;

        var jsonOutput = jsonMatch.Value;

        // check for games on lutris, it's a json array
        try
        {
            var jsonOutputParsed = JsonSerializer.Deserialize<List<LutrisGame>>(jsonOutput);

            if (jsonOutputParsed == null)
                return false;

            var gameListFiltered =
                jsonOutputParsed
                    .FirstOrDefault(item => item.Slug == "ea-app" && !string.IsNullOrWhiteSpace(item.Directory));

            if (gameListFiltered == null)
                return false;

            directory = gameListFiltered.Directory;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
