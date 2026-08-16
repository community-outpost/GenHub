using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results.ModBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service for executing external tools (crunch, gametextcompiler, blender, etc.).
/// Uses process pooling to limit concurrent external tool execution.
/// </summary>
public sealed class ExternalToolService : IExternalToolService
{
    private readonly ILogger<ExternalToolService> _logger;
    private readonly SemaphoreSlim _processPool;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalToolService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ExternalToolService(ILogger<ExternalToolService> logger)
    {
        _logger = logger;
        _processPool = new SemaphoreSlim(
            Environment.ProcessorCount,
            Environment.ProcessorCount);
    }

    /// <inheritdoc />
    public async Task<ToolOperationResult> ExecuteToolAsync(
        string toolPath,
        string arguments,
        string? workingDirectory = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _processPool.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteToolInternalAsync(
                toolPath,
                arguments,
                workingDirectory,
                progress,
                cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _processPool.Release();
        }
    }

    /// <summary>
    /// Internal method that performs the actual tool execution.
    /// </summary>
    /// <param name="toolPath">The path to the tool executable.</param>
    /// <param name="arguments">The command-line arguments.</param>
    /// <param name="workingDirectory">The working directory for the process.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tool operation result.</returns>
    private async Task<ToolOperationResult> ExecuteToolInternalAsync(
        string toolPath,
        string arguments,
        string? workingDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Executing tool: {ToolPath} {Arguments}", toolPath, arguments);
            progress?.Report($"Executing: {toolPath} {arguments}\n");

            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    progress?.Report(e.Data + "\n");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    progress?.Report($"ERROR: {e.Data}\n");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var exitCode = process.ExitCode;
            var success = exitCode == 0;

            if (!success)
            {
                _logger.LogWarning("Tool exited with code {ExitCode}", exitCode);
            }

            return new ToolOperationResult
            {
                Success = success,
                ExitCode = exitCode,
                Errors = success ? [] : [$"Tool exited with code {exitCode}"],
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute tool: {ToolPath}", toolPath);
            return new ToolOperationResult
            {
                Success = false,
                ExitCode = -1,
                Errors = [ex.Message],
            };
        }
    }

    /// <inheritdoc />
    public Task<ToolOperationResult<bool>> ValidateToolAsync(
        string toolPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = System.IO.File.Exists(toolPath);

            if (!exists)
            {
                _logger.LogWarning("Tool not found: {ToolPath}", toolPath);
            }

            return Task.FromResult(new ToolOperationResult<bool>
            {
                Success = exists,
                Data = exists,
                Errors = exists ? [] : [$"Tool not found: {toolPath}"],
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate tool: {ToolPath}", toolPath);
            return Task.FromResult(new ToolOperationResult<bool>
            {
                Success = false,
                Data = false,
                Errors = [ex.Message],
            });
        }
    }

    /// <summary>
    /// Disposes the service and releases the process pool.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _processPool?.Dispose();
        _disposed = true;
    }
}
