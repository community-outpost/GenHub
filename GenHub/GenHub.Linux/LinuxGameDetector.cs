using System.Collections.Generic;
using GenHub.Core;

namespace GenHub.Linux;

/// <inheritdoc/>
public class LinuxGameDetector : IGameDetector
{
    /// <inheritdoc/>
    public List<IGameInstallation> Installations { get; private set; } = [];

    /// <inheritdoc/>
    public void Detect()
    {
        throw new System.NotImplementedException();
    }
}