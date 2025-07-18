using System;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Models.GameInstallations;

/// <summary>
/// Represents a game installation.
/// </summary>
public class GameInstallation : IGameInstallation
{
    private readonly ILogger<GameInstallation>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameInstallation"/> class.
    /// </summary>
    /// <param name="installationPath">The installation path.</param>
    /// <param name="installationType">The installation type.</param>
    /// <param name="logger">Optional logger instance.</param>
    public GameInstallation(
        string installationPath,
        GameInstallationType installationType,
        ILogger<GameInstallation>? logger = null)
    {
        Id = Guid.NewGuid().ToString();
        InstallationPath = installationPath;
        InstallationType = installationType;
        DetectedAt = DateTime.UtcNow;
        _logger = logger;

        _logger?.LogDebug(
            "Created GameInstallation: Path={InstallationPath}, Type={InstallationType}",
            InstallationPath,
            InstallationType);
    }

    /// <summary>
    /// Gets or sets the unique identifier for this installation.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <inheritdoc/>
    public GameInstallationType InstallationType { get; set; }

    /// <inheritdoc/>
    public string InstallationPath { get; set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasGenerals { get; set; }

    /// <inheritdoc/>
    public string GeneralsPath { get; set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasZeroHour { get; set; }

    /// <inheritdoc/>
    public string ZeroHourPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date and time when this installation was detected/registered.
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets a value indicating whether this installation is currently valid/accessible.
    /// </summary>
    public bool IsValid =>
        (!this.HasGenerals || System.IO.Directory.Exists(this.GeneralsPath)) &&
        (!this.HasZeroHour || System.IO.Directory.Exists(this.ZeroHourPath));

    /// <inheritdoc/>
    public void Fetch()
    {
        try
        {
            _logger?.LogDebug("Fetching game installations for {InstallationPath}", InstallationPath);

            // Check for Generals installation
            var generalsPath = System.IO.Path.Combine(InstallationPath, "Command and Conquer Generals");
            if (System.IO.Directory.Exists(generalsPath))
            {
                HasGenerals = true;
                GeneralsPath = generalsPath;
                _logger?.LogDebug("Found Generals installation at {GeneralsPath}", GeneralsPath);
            }

            // Check for Zero Hour installation
            var zeroHourPath = System.IO.Path.Combine(InstallationPath, "Command and Conquer Generals Zero Hour");
            if (System.IO.Directory.Exists(zeroHourPath))
            {
                HasZeroHour = true;
                ZeroHourPath = zeroHourPath;
                _logger?.LogDebug("Found Zero Hour installation at {ZeroHourPath}", ZeroHourPath);
            }

            _logger?.LogInformation(
                "Installation fetch completed for {InstallationPath}: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                InstallationPath,
                HasGenerals,
                HasZeroHour);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to fetch installations for {InstallationPath}", InstallationPath);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{InstallationType}: {InstallationPath}";

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is GameInstallation other)
        {
            return string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return Id?.GetHashCode() ?? 0;
    }
}
