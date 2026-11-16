using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Launching;

/// <summary>
/// Installs Flatpak bundles on first launch so Linux game clients ship as single
/// files but run as installed applications.
/// <para>
/// The application ID comes from the bundle header (<see cref="FlatpakBundleHelper"/>),
/// so no CLI output parsing is needed: <c>flatpak info</c> detects an existing install,
/// otherwise <c>flatpak install --user</c> provisions it. Concurrent launches serialize
/// on an install lock because two installers racing on one repo fail confusingly.
/// </para>
/// </summary>
public sealed class FlatpakProvisioner(
    ILogger<FlatpakProvisioner> logger,
    ILocalizationService? localizationService = null) : IFlatpakProvisioner, IDisposable
{
    private readonly SemaphoreSlim _installLock = new(1, 1);
    private bool _disposed;

    /// <inheritdoc/>
    public async Task<OperationResult<string>> EnsureInstalledAsync(string bundlePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);

        var appId = FlatpakBundleHelper.TryExtractAppId(bundlePath);
        if (appId is null)
        {
            return OperationResult<string>.CreateFailure(
                RunnerTargetResolver.Localize(
                    localizationService,
                    LaunchMessageConstants.FlatpakRequiresInstallUnknownIdKey,
                    LaunchMessageConstants.FlatpakRequiresInstallUnknownId,
                    bundlePath));
        }

        if (!TryFindFlatpakCli(out var flatpakCli))
        {
            return OperationResult<string>.CreateFailure(
                RunnerTargetResolver.Localize(
                    localizationService,
                    LaunchMessageConstants.FlatpakCliMissingKey,
                    LaunchMessageConstants.FlatpakCliMissing,
                    bundlePath));
        }

        await _installLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await EnsureInstalledLockedAsync(bundlePath, appId, flatpakCli, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _installLock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _installLock.Dispose();
    }

    private static bool TryFindFlatpakCli([NotNullWhen(true)] out string? flatpakCli)
    {
        flatpakCli = null;
        var pathVariable = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        if (string.IsNullOrEmpty(pathVariable))
        {
            return false;
        }

        var directories = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in directories)
        {
            var candidate = Path.Combine(directory, ContentFormatConstants.FlatpakBinaryName);
            if (File.Exists(candidate))
            {
                flatpakCli = candidate;
                return true;
            }
        }

        return false;
    }

    private async Task<OperationResult<string>> EnsureInstalledLockedAsync(
        string bundlePath,
        string appId,
        string flatpakCli,
        CancellationToken cancellationToken)
    {
        if (await IsInstalledAsync(flatpakCli, appId, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<string>.CreateSuccess(appId);
        }

        logger.LogInformation("Installing Flatpak bundle {BundlePath} as {AppId}", bundlePath, appId);
        var install = await RunFlatpakAsync(
            flatpakCli,
            $"{ContentFormatConstants.FlatpakInstallCommand} {ContentFormatConstants.FlatpakUserFlag} {ContentFormatConstants.FlatpakAssumeYesFlag} {CommandLineHelper.QuoteArgument(bundlePath)}",
            cancellationToken).ConfigureAwait(false);
        if (install.ExitCode == 0)
        {
            return OperationResult<string>.CreateSuccess(appId);
        }

        if (await IsInstalledAsync(flatpakCli, appId, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<string>.CreateSuccess(appId);
        }

        logger.LogWarning("Flatpak install failed for {AppId} (exit {ExitCode}): {Error}", appId, install.ExitCode, install.StandardError);
        return OperationResult<string>.CreateFailure(
            RunnerTargetResolver.Localize(
                localizationService,
                LaunchMessageConstants.FlatpakInstallFailedKey,
                LaunchMessageConstants.FlatpakInstallFailed,
                bundlePath,
                appId,
                install.StandardError));
    }

    private async Task<bool> IsInstalledAsync(string flatpakCli, string appId, CancellationToken cancellationToken)
    {
        var info = await RunFlatpakAsync(
            flatpakCli,
            $"{ContentFormatConstants.FlatpakInfoCommand} {ContentFormatConstants.FlatpakUserFlag} {CommandLineHelper.QuoteArgument(appId)}",
            cancellationToken).ConfigureAwait(false);
        return info.ExitCode == 0;
    }

    private async Task<(int ExitCode, string StandardError)> RunFlatpakAsync(
        string flatpakCli,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo.FileName = flatpakCli;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return (-1, ex.Message.Trim());
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);

            // The process was just killed, so this wait is bounded and must not observe
            // the already-fired token.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var drainTask = Task.WhenAll(outputTask, errorTask);
        if (await Task.WhenAny(drainTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false) != drainTask)
        {
            logger.LogWarning("Flatpak process exited with code {ExitCode}, but standard output/error did not close within timeout; killing process tree", process.ExitCode);
            KillProcess(process);
        }

        var error = errorTask.IsCompletedSuccessfully ? errorTask.Result.Trim() : string.Empty;
        return (process.ExitCode, error);
    }

    private void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            logger.LogDebug(ex, "Flatpak child already exited during cancellation");
        }
    }
}
