using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Locates and launches the native WorldBuilder sidecar for full 3D editing.
/// Launching reuses the game-runner abstraction so Linux/macOS run through
/// Wine/Proton runners while Windows starts the executable directly.
/// </summary>
public sealed class WorldBuilderSidecarService(IEnumerable<IGameLaunchRunner> runners, ILogger<WorldBuilderSidecarService> logger) : IWorldBuilderSidecarService
{
    /// <inheritdoc />
    public Task<OperationResult<string>> FindNativeAsync(string gameFolder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameFolder);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var name in WorldBuilderConstants.FileNames.SidecarExecutables)
        {
            var candidate = Path.Combine(gameFolder, name);
            if (File.Exists(candidate))
            {
                return Task.FromResult(OperationResult<string>.CreateSuccess(candidate));
            }
        }

        return Task.FromResult(OperationResult<string>.CreateFailure("Native WorldBuilder executable not found in the game folder."));
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> LaunchAsync(string executablePath, string? mapPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(executablePath))
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure("Native WorldBuilder executable not found."));
        }

        var runner = runners.FirstOrDefault(r => r.CanLaunchWindowsExecutables());
        if (runner == null)
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure("No launcher available for Windows executables on this platform."));
        }

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
        };
        var resolved = runner.ResolveCommand(configuration);
        if (!resolved.Success)
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure(resolved.FirstError ?? "Failed to resolve the launch command."));
        }

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = resolved.Data.FileName;
            process.StartInfo.WorkingDirectory = configuration.WorkingDirectory ?? string.Empty;
            foreach (var variable in resolved.Data.EnvironmentVariables)
            {
                process.StartInfo.EnvironmentVariables[variable.Key] = variable.Value;
            }

            if (!string.IsNullOrEmpty(resolved.Data.ArgumentPrefix))
            {
                process.StartInfo.ArgumentList.Add(resolved.Data.ArgumentPrefix);
            }

            if (!string.IsNullOrEmpty(mapPath))
            {
                process.StartInfo.ArgumentList.Add(mapPath);
            }

            process.StartInfo.UseShellExecute = false;
            if (!process.Start())
            {
                return Task.FromResult(OperationResult<bool>.CreateFailure("Failed to start the native WorldBuilder."));
            }

            // Intentional fire-and-forget: the native builder is an interactive GUI
            // session owned by the user, so GenHub must not wait on, drain, or kill
            // it like a managed child process. Disposing our handle leaves it running.
            logger.LogInformation("Launched native WorldBuilder from {ExecutablePath}.", executablePath);
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Failed to launch native WorldBuilder from {ExecutablePath}.", executablePath);
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Failed to launch: {ex.Message}"));
        }
    }
}
