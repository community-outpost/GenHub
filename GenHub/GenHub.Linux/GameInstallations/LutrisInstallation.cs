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
    public bool HasGenerals { get; }

    /// <inheritdoc/>
    public string GeneralsPath { get; }

    /// <inheritdoc/>
    public bool HasZeroHour { get; }

    /// <inheritdoc/>
    public string ZeroHourPath { get; }

    /// <summary>
    /// Shows how is Steam installed.
    /// </summary>
    public LinuxPackageInstallationType PackageInstallationType { get; private set; }

    /// <inheritdoc/>
    public void Fetch()
    {
        throw new NotImplementedException();
    }
}