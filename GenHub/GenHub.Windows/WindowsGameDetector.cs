using System.Collections.Generic;
using GenHub.Core;
using GenHub.Windows.Installations;

namespace GenHub.Windows;

/// <inheritdoc/>
public class WindowsGameDetector : IGameDetector
{
    /// <inheritdoc/>
    public List<IGameInstallation> Installations { get; private set; } = [];

    /// <inheritdoc/>
    public void Detect()
    {
        Installations.Clear();

        Installations.Add(new SteamInstallation(true));
        Installations.Add(new EaAppInstallation(true));
    }
}