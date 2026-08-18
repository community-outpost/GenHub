using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service for converting between CSF (game string table) and STR (text) formats using gametextcompiler.
/// </summary>
public sealed class StringTableConversionService(
    ILogger<StringTableConversionService> logger) : IStringTableConversionService
{
    private const string ToolName = "gametextcompiler";

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ConvertStrToCsfAsync(
        string sourceStrPath,
        string targetCsfPath,
        string? language = null,
        string? swapAndSetLanguage = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!File.Exists(sourceStrPath))
            {
                logger.LogError("Source STR file not found: {Path}", sourceStrPath);
                return OperationResult<bool>.CreateFailure($"Source STR file not found: {sourceStrPath}");
            }

            var toolPath = FindToolPath();
            if (toolPath == null)
            {
                logger.LogError("{Tool} not found in PATH or current directory", ToolName);
                return OperationResult<bool>.CreateFailure($"{ToolName} not found. Please ensure it is installed and available in PATH.");
            }

            var arguments = new StringBuilder();
            arguments.Append($"-LOAD_STR \"{sourceStrPath}\" -SAVE_CSF \"{targetCsfPath}\"");

            if (!string.IsNullOrEmpty(language))
            {
                arguments.Append($" -LOAD_STR_LANGUAGES {language}");
            }

            if (!string.IsNullOrEmpty(swapAndSetLanguage))
            {
                arguments.Append($" -SWAP_AND_SET_LANGUAGE {swapAndSetLanguage}");
            }

            logger.LogInformation("Converting STR to CSF: {Source} -> {Target}", sourceStrPath, targetCsfPath);
            logger.LogDebug("Executing: {Tool} {Args}", toolPath, arguments);

            var result = await ExecuteToolAsync(toolPath, arguments.ToString(), cancellationToken);

            if (result.Success)
            {
                if (!File.Exists(targetCsfPath))
                {
                    logger.LogError("Conversion completed but target CSF file was not created: {Path}", targetCsfPath);
                    return OperationResult<bool>.CreateFailure("Conversion failed: target file was not created");
                }

                logger.LogInformation("Successfully converted STR to CSF: {Target}", targetCsfPath);
                return OperationResult<bool>.CreateSuccess(true);
            }

            return OperationResult<bool>.CreateFailure(result.FirstError ?? "Conversion failed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error converting STR to CSF: {Source} -> {Target}", sourceStrPath, targetCsfPath);
            return OperationResult<bool>.CreateFailure($"Error converting STR to CSF: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> ConvertCsfToStrAsync(
        string sourceCsfPath,
        string targetStrPath,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!File.Exists(sourceCsfPath))
            {
                logger.LogError("Source CSF file not found: {Path}", sourceCsfPath);
                return OperationResult<bool>.CreateFailure($"Source CSF file not found: {sourceCsfPath}");
            }

            var toolPath = FindToolPath();
            if (toolPath == null)
            {
                logger.LogError("{Tool} not found in PATH or current directory", ToolName);
                return OperationResult<bool>.CreateFailure($"{ToolName} not found. Please ensure it is installed and available in PATH.");
            }

            var arguments = new StringBuilder();
            arguments.Append($"-LOAD_CSF \"{sourceCsfPath}\" -SAVE_STR \"{targetStrPath}\"");

            if (!string.IsNullOrEmpty(language))
            {
                arguments.Append($" -SAVE_STR_LANGUAGES {language}");
            }

            logger.LogInformation("Converting CSF to STR: {Source} -> {Target}", sourceCsfPath, targetStrPath);
            logger.LogDebug("Executing: {Tool} {Args}", toolPath, arguments);

            var result = await ExecuteToolAsync(toolPath, arguments.ToString(), cancellationToken);

            if (result.Success)
            {
                if (!File.Exists(targetStrPath))
                {
                    logger.LogError("Conversion completed but target STR file was not created: {Path}", targetStrPath);
                    return OperationResult<bool>.CreateFailure("Conversion failed: target file was not created");
                }

                logger.LogInformation("Successfully converted CSF to STR: {Target}", targetStrPath);
                return OperationResult<bool>.CreateSuccess(true);
            }

            return OperationResult<bool>.CreateFailure(result.FirstError ?? "Conversion failed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error converting CSF to STR: {Source} -> {Target}", sourceCsfPath, targetStrPath);
            return OperationResult<bool>.CreateFailure($"Error converting CSF to STR: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes the external tool with the given arguments.
    /// </summary>
    /// <param name="toolPath">The path to the tool executable.</param>
    /// <param name="arguments">The command-line arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result.</returns>
    private async Task<OperationResult<bool>> ExecuteToolAsync(string toolPath, string arguments, CancellationToken cancellationToken)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(toolPath) ?? Environment.CurrentDirectory,
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        try
        {
            using var process = new Process { StartInfo = processStartInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    logger.LogDebug("[{Tool}] {Output}", ToolName, e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    logger.LogWarning("[{Tool}] {Error}", ToolName, e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
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

            if (exitCode != 0)
            {
                var errorMessage = errorBuilder.Length > 0 ? errorBuilder.ToString() : $"Process exited with code {exitCode}";
                logger.LogError("{Tool} failed with exit code {ExitCode}: {Error}", ToolName, exitCode, errorMessage);
                return OperationResult<bool>.CreateFailure(errorMessage);
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing {Tool}", ToolName);
            return OperationResult<bool>.CreateFailure($"Error executing {ToolName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds the path to the gametextcompiler tool.
    /// </summary>
    /// <returns>The tool path if found; otherwise, null.</returns>
    private string? FindToolPath()
    {
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", string.Empty }
            : new[] { string.Empty, ".exe" };

        // Check if tool exists in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                foreach (var ext in extensions)
                {
                    var toolPath = Path.Combine(path, ToolName + ext);
                    if (File.Exists(toolPath))
                    {
                        return toolPath;
                    }
                }
            }
        }

        // Check current directory
        foreach (var ext in extensions)
        {
            var currentDirTool = Path.Combine(Environment.CurrentDirectory, ToolName + ext);
            if (File.Exists(currentDirTool))
            {
                return currentDirTool;
            }
        }

        // Check common tool locations
        var commonPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GeneralsTools", ToolName + ".exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GeneralsTools", ToolName + ".exe"),
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
