using System.Collections.Generic;
using GenHub.Core;
using GenHub.Core.Interfaces.GameVersions;
using GenHub.Core.Models.Enums;
using GenHub.Windows.Installations;

namespace GenHub.Windows;

public class WindowsGameDetector : IGameDetector
{
    public List<IGameInstallation> Installations { get; private set; } = new();

    public void Detect()
    {
        Installations.Clear();

        Installations.Add(new SteamInstallation(true));
        Installations.Add(new EaAppInstallation(true));
    }
}
