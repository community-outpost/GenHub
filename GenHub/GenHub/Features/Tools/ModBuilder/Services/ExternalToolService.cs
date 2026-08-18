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
public sealed class ExternalToolService(ILogger<ExternalToolService> logger) : IExternalToolService
{
    private readonly SemaphoreSlim _processPool = new(Environment.ProcessorCount, Environment.ProcessorCount);
    private bool _disposed;

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
        var resolvedPath = FindToolInPath(toolPath) ?? toolPath;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            logger.LogInformation("Executing tool: {ToolPath} {Arguments}", resolvedPath, arguments);
            progress?.Report($"Executing: {resolvedPath} {arguments}\n");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = resolvedPath,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory ?? string.Empty,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

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

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore failure killing already exited process
                }

                throw;
            }

            var exitCode = process.ExitCode;
            var success = exitCode == 0;

            if (!success)
            {
                logger.LogWarning("Tool exited with code {ExitCode}", exitCode);
                return ToolOperationResult.CreateFailure($"Tool exited with code {exitCode}", exitCode);
            }

            return ToolOperationResult.CreateSuccess(exitCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute tool: {ToolPath}", toolPath);
            return ToolOperationResult.CreateFailure(ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<ToolOperationResult<bool>> ValidateToolAsync(
        string toolPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = System.IO.File.Exists(toolPath) || FindToolInPath(toolPath) != null;

            if (!exists)
            {
                logger.LogWarning("Tool not found: {ToolPath}", toolPath);
                return Task.FromResult(ToolOperationResult<bool>.CreateFailure($"Tool not found: {toolPath}"));
            }

            return Task.FromResult(ToolOperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate tool: {ToolPath}", toolPath);
            return Task.FromResult(ToolOperationResult<bool>.CreateFailure(ex.Message));
        }
    }

    private static string? FindToolInPath(string toolName)
    {
        if (System.IO.File.Exists(toolName))
        {
            return toolName;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? new[] { string.Empty, ".exe", ".cmd", ".bat" }
            : new[] { string.Empty };

        foreach (var path in pathEnv.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var fullPath = System.IO.Path.Combine(path, toolName + ext);
                if (System.IO.File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
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
