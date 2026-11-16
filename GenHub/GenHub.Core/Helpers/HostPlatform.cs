using GenHub.Core.Models.Enums;
using System;

namespace GenHub.Core.Helpers;

/// <summary>
/// Identifies the operating system GenHub is running on.
/// </summary>
public static class HostPlatform
{
    /// <summary>
    /// Gets the platform of the running host, or <see cref="ExecutablePlatform.Unknown"/> on any
    /// operating system GenHub does not target.
    /// </summary>
    public static ExecutablePlatform Current
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return ExecutablePlatform.Windows;
            }

            if (OperatingSystem.IsMacOS())
            {
                return ExecutablePlatform.MacOS;
            }

            return OperatingSystem.IsLinux() ? ExecutablePlatform.Linux : ExecutablePlatform.Unknown;
        }
    }
}
