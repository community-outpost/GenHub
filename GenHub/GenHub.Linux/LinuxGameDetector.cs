using System.Collections.Generic;
using GenHub.Core;
using GenHub.Linux.Installations;

namespace GenHub.Linux;

/// <inheritdoc/>
public class LinuxGameDetector : IGameDetector
{
    /// <inheritdoc/>
    public List<IGameInstallation> Installations { get; private set; } = [];

    /// <inheritdoc/>
    public void Detect()
    {
        Installations.Clear();
        
        Installations.Add(new SteamInstallation(true));
    }
}