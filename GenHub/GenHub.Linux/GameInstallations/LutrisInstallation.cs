using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.GameInstallations;

/// <summary>
/// Lutris installation detector and manager for Linux.
/// </summary>
public class LutrisInstallation(ILogger<SteamInstallation>? logger = null) : IGameInstallation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SteamInstallation"/> class.
    /// </summary>
    /// <param name="fetch">Value indicating whether <see cref="Fetch"/> should be called while instantiation.</param>
    /// <param name="logger">Optional logger instance.</param>
    public LutrisInstallation(bool fetch, ILogger<SteamInstallation>? logger = null) : this(logger)
    {
        if (fetch)
        {
            Fetch();
        }
    }

    /// <inheritdoc/>
    public GameInstallationType InstallationType { get; }

    /// <inheritdoc/>
    public string InstallationPath { get; }

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
    /// Shows how is Lutris installed.
    /// </summary>
    public LinuxPackageInstallationType PackageInstallationType { get; private set; }

    /// <summary>
    /// Gets the value of Lutris Version.
    /// </summary>
    public string LutrisVersion { get; private set; } = string.Empty;

    private Regex LutrisVersionRegex = new Regex(@"l^lutris-([\\d\\.]*)$")

    /// <inheritdoc/>
    public void Fetch()
    {
        throw new NotImplementedException();
    }
}