using GenHub.Core;

namespace GenHub.Linux.Installations;

public class SteamInstallation : IGameInstallation
{
    // IGameInstallation
    public GameInstallationType InstallationType => GameInstallationType.Steam;
    public bool IsVanillaInstalled { get; private set; }
    public string VanillaGamePath { get; private set; } = "";
    public bool IsZeroHourInstalled { get; private set; }
    public string ZeroHourGamePath { get; private set; } = "";
    public void Fetch()
    {
        throw new System.NotImplementedException();
    }
}