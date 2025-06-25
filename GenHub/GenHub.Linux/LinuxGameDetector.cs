using System.Collections.Generic;
using GenHub.Core;
using GenHub.Core.Interfaces.GameVersions;
using GenHub.Core.Models.Enums;

namespace GenHub.Linux;

public class LinuxGameDetector : IGameDetector
{
    public List<IGameInstallation> Installations { get; private set; } = new();

    public void Detect()
    {
        throw new System.NotImplementedException();
    }
}
