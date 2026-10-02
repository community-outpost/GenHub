using GenHub.Core.Models.Manifest;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Service that inspects executables, packages, and directories to detect and extract GenHub build metadata.
/// </summary>
public interface IGenHubBuildInspector
{
    /// <summary>
    /// Inspects the specified path (executable, installer, zip, nupkg, or folder) and returns GenHub build metadata.
    /// </summary>
    /// <param name="path">The file or folder path to inspect.</param>
    /// <returns>Metadata describing the build, or an empty result where <see cref="GenHubBuildInfo.IsGenHubBuild"/> is false.</returns>
    GenHubBuildInfo Inspect(string path);

    /// <summary>
    /// Asynchronously inspects the specified path.
    /// </summary>
    /// <param name="path">The file or folder path to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task resolving to the build info.</returns>
    Task<GenHubBuildInfo> InspectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the given path represents a GenHub build.
    /// </summary>
    /// <param name="path">The file or folder path.</param>
    /// <returns>True if the path appears to be a GenHub build; otherwise false.</returns>
    bool IsGenHubBuildPath(string path);
}
