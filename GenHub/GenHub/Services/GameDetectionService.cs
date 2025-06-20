using GenHub.Core;

namespace GenHub.Services;

/// <summary>
/// Service for a <see cref="IGameDetector"/>.
/// </summary>
/// <param name="gameDetector">The <see cref="IGameDetector"/> to serve.</param>
public class GameDetectionService(IGameDetector gameDetector)
{
    /// <inheritdoc cref="IGameInstallation.IsVanillaInstalled"/>
    public bool IsVanillaInstalled => gameDetector.Installations[0].IsVanillaInstalled;

    /// <inheritdoc cref="IGameInstallation.VanillaGamePath"/>
    public string VanillaGamePath => gameDetector.Installations[0].VanillaGamePath;

    /// <inheritdoc cref="IGameInstallation.IsZeroHourInstalled"/>
    public bool IsZeroHourInstalled => gameDetector.Installations[0].IsZeroHourInstalled;

    /// <inheritdoc cref="IGameInstallation.ZeroHourGamePath"/>
    public string ZerHourGamePath => gameDetector.Installations[0].ZeroHourGamePath;

    /// <summary>
    /// Detects several <see cref="IGameInstallation"/>s and adds them to the internal list of the <see cref="IGameDetector"/>.
    /// </summary>
    public void DetectGames()
    {
        gameDetector.Detect();
    }
}