using GenHub.Core.Interfaces.GameVersions;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Helpers;

public class DummyGameDetector : IGameDetector
{
    public List<IGameInstallation> Installations => null;

    public void Detect()
    {
        throw new System.NotImplementedException();
    }
}
