using GenHub.Core;
using GenHub.Core.Interfaces.GameVersions;

namespace GenHub.Features.GameVersions.Services;

public class GameDetectionService(IGameDetector gameDetector)
{
    public bool IsVanillaInstalled => gameDetector.Installations[0].IsVanillaInstalled;
    public string VanillaGamePath => gameDetector.Installations[0].VanillaGamePath;
    public bool IsZeroHourInstalled => gameDetector.Installations[0].IsZeroHourInstalled;
    public string ZerHourGamePath => gameDetector.Installations[0].ZeroHourGamePath;

    public void DetectGames()
    {
        gameDetector.Detect();
    }
}
