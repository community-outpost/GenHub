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

    /// <summary>
    /// Detects Lutris installation and games asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task FetchAsync(CancellationToken cancellationToken = default)
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
                cancellationToken.ThrowIfCancellationRequested();

                var (hasVersion, version) = await TryLutrisAsync(entry.Key, cancellationToken).ConfigureAwait(false);
                if (!hasVersion)
                {
                    continue;
                }

                var (hasZH, directory) = await TryLutrisHasZHAsync(entry.Key, cancellationToken).ConfigureAwait(false);
                if (!hasZH)
                {
                    continue;
                }

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
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            logger?.LogDebug(ex, "Lutris installation detection was canceled");
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger?.LogDebug(ex, "Lutris installation detection was canceled");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error occurred during Lutris installation detection on Linux");
            IsLutrisInstalled = false;
        }
    }

    /// <inheritdoc/>
    public void Fetch()
    {
        Task.Run(() => FetchAsync(CancellationToken.None)).GetAwaiter().GetResult();
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

    private static void KillLutrisProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Ignore failure to terminate already exited process
        }
    }

    private async Task<(bool Success, string Output)> RunLutrisCommandAsync(
        string installationPath,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        if (_processRunner != null)
        {
            var result = _processRunner(installationPath, args);
            return (result.Success, result.Output);
        }

        try
        {
            using var process = new Process
            {
                StartInfo = CreateLutrisStartInfo(installationPath, args),
            };

            if (!process.Start())
            {
                return (false, string.Empty);
            }

            using var timeoutCts = new CancellationTokenSource(ProcessConstants.ExternalCliTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var readOutputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillLutrisProcess(process);
                process.WaitForExit(ProcessConstants.ProcessKillWaitMs);
                return (false, string.Empty);
            }

            if (await Task.WhenAny(readOutputTask, Task.Delay(TimeSpan.FromMilliseconds(ProcessConstants.ProcessKillWaitMs), cancellationToken)).ConfigureAwait(false) != readOutputTask)
            {
                KillLutrisProcess(process);
                process.WaitForExit(ProcessConstants.ProcessKillWaitMs);
                return (false, string.Empty);
            }

            var output = await readOutputTask.ConfigureAwait(false);
            return (process.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to execute Lutris command: {InstallationPath} {Args}", installationPath, string.Join(" ", args));
            return (false, string.Empty);
        }
    }

    private async Task<(bool Success, string Version)> TryLutrisAsync(string installationPath, CancellationToken cancellationToken)
    {
        var (success, output) = await RunLutrisCommandAsync(installationPath, ["-v"], cancellationToken).ConfigureAwait(false);
        if (!success)
        {
            return (false, string.Empty);
        }

        foreach (var item in output.Split(Environment.NewLine))
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            // check for lutris, if installed version is printed
            var match = LutrisVersionRegex().Match(item);
            if (match.Success && match.Groups.Count > 1)
            {
                return (true, match.Groups[1].Value);
            }
        }

        return (false, string.Empty);
    }

    private async Task<(bool Success, string Directory)> TryLutrisHasZHAsync(string installationPath, CancellationToken cancellationToken)
    {
        var (success, output) = await RunLutrisCommandAsync(installationPath, ["-l", "-j"], cancellationToken).ConfigureAwait(false);
        if (!success)
        {
            return (false, string.Empty);
        }

        var jsonMatch = LutrisGamesRegex().Match(output);
        if (!jsonMatch.Success)
        {
            return (false, string.Empty);
        }

        // check for games on lutris, it's a json array
        try
        {
            var jsonOutputParsed = JsonSerializer.Deserialize<List<LutrisGame>>(jsonMatch.Value);
            if (jsonOutputParsed == null)
            {
                return (false, string.Empty);
            }

            var gameListFiltered = jsonOutputParsed
                .FirstOrDefault(item => item.Slug == "ea-app" && !string.IsNullOrWhiteSpace(item.Directory));

            return gameListFiltered != null ? (true, gameListFiltered.Directory) : (false, string.Empty);
        }
        catch (JsonException)
        {
            return (false, string.Empty);
        }
    }
}
