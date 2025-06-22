namespace GenHub.Core.Interfaces.GameVersions;

using GenHub.Core.Models.Enums;

public interface IGameInstallation
{
    public GameInstallationType InstallationType { get; }
    public bool IsVanillaInstalled { get; }
    public string VanillaGamePath { get; }
    public bool IsZeroHourInstalled { get; }
    public string ZeroHourGamePath { get; }

    public void Fetch();
}
