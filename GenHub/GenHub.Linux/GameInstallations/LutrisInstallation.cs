using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Linux.Model;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.GameInstallations;

/// <summary>
/// Lutris installation detector and manager for Linux.
/// </summary>
public class LutrisInstallation(ILogger<LutrisInstallation>? logger = null) : IGameInstallation
{
    private readonly Regex lutrisVersionRegex = new Regex(@"^lutris-([\d\.]*)$");
    private readonly Regex lutrisGamesRegex = new Regex(@"\[[\s\S]*\]");

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

    /// <summary>
    /// Gets a value indicating whether Lutris is installed successfully.
    /// </summary>
    public bool IsLutrisInstalled { get; private set; }

    /// <summary>
    /// Gets Lutris installation Type.
    /// </summary>
    public LinuxPackageInstallationType PackageInstallationType { get; private set; }

    /// <summary>
    /// Gets the value of Lutris Version.
    /// </summary>
    public string LutrisVersion { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public void Fetch()
    {
        logger?.LogInformation("Starting Lutris installation detection on Linux");

        try
        {
            var lutrisExecutables = new Dictionary<string, LinuxPackageInstallationType>
            {
                { "lutris", LinuxPackageInstallationType.Binary },
                { "flatpak run net.lutris.Lutris", LinuxPackageInstallationType.Flatpack },

                // TODO add snap
            };
            foreach (var entry in lutrisExecutables)
            {
                // check for lutris
                if (!TryLutris(entry.Key, out var version) ||
                    !TryLutrisHasZH(entry.Key, out var directory)) continue;
                var homeDir = Path.Combine(
                    directory,
                    "drive_c/Program Files/EA Games/Command and Conquer Generals Zero Hour/");

                // Check if EA app and Generals/ZH are installed
                if (Directory.Exists(homeDir))
                {
                    InstallationPath = homeDir;
                    LutrisVersion = version;
                    PackageInstallationType = entry.Value;
                    if (Directory.Exists(Path.Combine(homeDir, "Command and Conquer Generals Zero Hour")))
                    {
                        HasZeroHour = true;
                        ZeroHourPath = Path.Combine(homeDir, "Command and Conquer Generals Zero Hour");
                    }

                    if (Directory.Exists(Path.Combine(homeDir, "Command and Conquer Generals")))
                    {
                        HasGenerals = true;
                        GeneralsPath = Path.Combine(homeDir, "Command and Conquer Generals");
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

    private bool TryLutris(string installationPath, out string lutrisVersion)
    {
        lutrisVersion = string.Empty;
        var process = new Process();
        process.StartInfo = new ProcessStartInfo()
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            FileName = installationPath,
            Arguments = "-v",
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (!process.Start())
            return false;
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        foreach (var item in output.Split(Environment.NewLine))
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;

            // check for lutris, if installed version is printed
            var match = lutrisVersionRegex.Match(item);
            if (match is { Success: true, Groups.Count: > 1 })
                lutrisVersion = match.Groups[1].Value;

            return true;
        }

        return false;
    }

    private bool TryLutrisHasZH(string installationPath, out string directory)
    {
        directory = string.Empty;
        var process = new Process();
        process.StartInfo = new ProcessStartInfo()
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            FileName = installationPath,
            ArgumentList = { "-l", "-j" },
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (!process.Start())
            return false;
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        var jsonOutput = lutrisGamesRegex.Match(output).Value;

        // check for games on lutris, it's a json array
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
}