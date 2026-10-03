using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for executing external tools (crunch, gametextcompiler, blender, etc.).
/// </summary>
public interface IExternalToolService : IDisposable
{
    /// <summary>
    /// Executes an external tool with the specified arguments.
    /// </summary>
    /// <param name="toolPath">The path to the tool executable.</param>
    /// <param name="arguments">The command-line arguments.</param>
    /// <param name="workingDirectory">Optional working directory.</param>
    /// <param name="progress">Optional progress reporter for output.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<ToolOperationResult> ExecuteToolAsync(
        string toolPath,
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a tool exists and is executable.
    /// </summary>
    /// <param name="toolPath">The path to the tool executable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating whether the tool is valid.</returns>
    Task<ToolOperationResult<bool>> ValidateToolAsync(
        string toolPath,
        CancellationToken cancellationToken = default);
}
