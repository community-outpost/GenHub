using System.Diagnostics;
using GenHub.Core.Interfaces.Common;

namespace GenHub.Windows.Infrastructure.Services;

/// <summary>
/// Windows implementation of external link service using Process.Start.
/// </summary>
public class WindowsExternalLinkService : IExternalLinkService
{
    /// <inheritdoc />
    public bool OpenUrl(string url)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            };

            var process = Process.Start(startInfo);
            return process != null;
        }
        catch
        {
            return false;
        }
    }
}