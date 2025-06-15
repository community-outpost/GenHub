using System.Collections.Generic;
using GenHub.Core;
using GenHub.Linux.Installations;

namespace GenHub.Linux;

public class LinuxGameDetector : IGameDetector
{
    public List<IGameInstallation> Installations { get; private set; } = new();

    public void Detect()
    {
        Installations.Clear();
        
        Installations.Add(new SteamInstallation(true));
    }
}