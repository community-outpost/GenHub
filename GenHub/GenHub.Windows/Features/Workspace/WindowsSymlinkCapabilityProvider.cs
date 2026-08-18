using System;
using System.IO;
using System.Runtime.Versioning;
using GenHub.Core.Interfaces.Workspace;

namespace GenHub.Windows.Features.Workspace;

/// <summary>
/// Symlink capability on Windows.
/// </summary>
/// <remarks>
/// Capability is determined by a real, cached creation probe rather than Administrator
/// membership. Developer Mode can permit unelevated symlink creation, while policy can
/// deny it to an otherwise elevated process.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsSymlinkCapabilityProvider : ISymlinkCapabilityProvider
{
    private static readonly Lazy<bool> _cachedCapability = new(ProbeCapability);

    /// <inheritdoc/>
    public bool CanCreateSymlinks => _cachedCapability.Value;

    private static bool ProbeCapability()
    {
        var probeId = Guid.NewGuid().ToString("N");
        var targetPath = Path.Combine(Path.GetTempPath(), $"genhub-symlink-target-{probeId}.tmp");
        var linkPath = Path.Combine(Path.GetTempPath(), $"genhub-symlink-link-{probeId}.tmp");

        try
        {
            File.WriteAllText(targetPath, string.Empty);
            File.CreateSymbolicLink(linkPath, targetPath);
            return new FileInfo(linkPath).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            DeleteIfExists(linkPath);
            DeleteIfExists(targetPath);
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of a uniquely named temporary probe.
        }
    }
}
