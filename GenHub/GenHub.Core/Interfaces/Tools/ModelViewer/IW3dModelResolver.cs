using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Services.Tools.Checksum;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModelViewer;

/// <summary>
/// Resolves model names to parsed models with decoded textures.
/// </summary>
public interface IW3dModelResolver
{
    /// <summary>
    /// Resolves a model from game installation roots layered with a mod project directory.
    /// Implementations may execute synchronously; callers must offload with
    /// <c>Task.Run</c> instead of awaiting this directly on the UI thread.
    /// </summary>
    /// <param name="modelName">The model name without extension.</param>
    /// <param name="installationPath">The game installation root.</param>
    /// <param name="isZeroHour">Whether the installation is Zero Hour.</param>
    /// <param name="projectDirectory">The optional mod project directory layered above game files.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved model, or a failure describing the problem.</returns>
    Task<OperationResult<W3dResolvedModel>> ResolveAsync(
        string modelName,
        string installationPath,
        bool isZeroHour,
        string? projectDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a model from an explicit virtual file system.
    /// </summary>
    /// <param name="modelName">The model name without extension.</param>
    /// <param name="fileSystem">The layered file system to read from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved model, or a failure describing the problem.</returns>
    Task<OperationResult<W3dResolvedModel>> ResolveFromFileSystemAsync(
        string modelName,
        SageVirtualFileSystem fileSystem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears cached file systems so installation or project changes take effect.
    /// </summary>
    void ClearCache();
}
