using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for generating complete project structure with folders and config files.
/// </summary>
public interface IProjectStructureGenerator
{
    /// <summary>
    /// Generates complete project structure including folders, config files, and README files.
    /// </summary>
    /// <param name="projectPath">Path to the .mbproj file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task GenerateProjectStructureAsync(string projectPath, CancellationToken cancellationToken);
}
