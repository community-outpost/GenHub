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
    IExternalToolService externalToolService,
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

            var workingDir = Path.GetDirectoryName(toolPath) ?? Environment.CurrentDirectory;
            var result = await externalToolService.ExecuteToolAsync(toolPath, arguments.ToString(), workingDir, null, cancellationToken).ConfigureAwait(false);

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

            var workingDir = Path.GetDirectoryName(toolPath) ?? Environment.CurrentDirectory;
            var result = await externalToolService.ExecuteToolAsync(toolPath, arguments.ToString(), workingDir, null, cancellationToken).ConfigureAwait(false);

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
    /// Finds the path to the gametextcompiler tool.
    /// </summary>
    /// <returns>The tool path if found; otherwise, null.</returns>
    private static string? FindToolPath()
    {
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", string.Empty }
            : new[] { string.Empty, ".exe" };

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

        foreach (var ext in extensions)
        {
            var currentDirTool = Path.Combine(Environment.CurrentDirectory, ToolName + ext);
            if (File.Exists(currentDirTool))
            {
                return currentDirTool;
            }
        }

        return null;
    }
}
