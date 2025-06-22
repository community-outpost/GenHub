namespace GenHub.Core.Interfaces.GameVersions;


public interface IGameDetector
{
    public List<IGameInstallation> Installations { get; }

    public void Detect();
}
