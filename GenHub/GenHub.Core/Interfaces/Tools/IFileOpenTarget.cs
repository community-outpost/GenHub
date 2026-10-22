using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Implemented by tool plugins that can open a file on request.
/// </summary>
public interface IFileOpenTarget
{
    /// <summary>
    /// Opens a file in the tool.
    /// </summary>
    /// <param name="filePath">The full path of the file to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the file was opened successfully.</returns>
    Task<bool> OpenFileAsync(string filePath, CancellationToken cancellationToken = default);
}
