using System.Diagnostics;
using GenHub.Core.Interfaces.Common;

namespace GenHub.Linux.Infrastructure.Services;

/// <summary>
/// Linux implementation of external link service using xdg-open.
/// </summary>
public class LinuxExternalLinkService : IExternalLinkService
{
    /// <inheritdoc />
    public bool OpenUrl(string url)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = url,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
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