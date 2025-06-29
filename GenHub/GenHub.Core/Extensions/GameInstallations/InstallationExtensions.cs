using GenHub.Core.Models.GameInstallations;

namespace GenHub.Core.Extensions.GameInstallations;

/// <summary>
/// Extension methods for converting IGameInstallation to GameInstallation domain model.
/// </summary>
public static class InstallationExtensions
{
    /// <summary>
    /// Converts an <see cref="IGameInstallation"/> to a <see cref="GameInstallation"/> domain model.
    /// </summary>
    /// <param name="src">The source <see cref="IGameInstallation"/>.</param>
    /// <returns>A new <see cref="GameInstallation"/> instance.</returns>
    public static GameInstallation ToDomain(this IGameInstallation src)
    {
        return new GameInstallation
        {
            InstallationType = src.InstallationType,
            InstallationPath = src.IsZeroHourInstalled ? src.ZeroHourGamePath : src.VanillaGamePath,
            HasGenerals = src.IsVanillaInstalled,
            GeneralsPath = src.VanillaGamePath,
            HasZeroHour = src.IsZeroHourInstalled,
            ZeroHourPath = src.ZeroHourGamePath,
        };
    }
}
