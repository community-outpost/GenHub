using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.Services;

/// <summary>
/// Shared virtual LAN adapter orchestration backed by the overlay sidecar host with in-process tunnel fallback.
/// Platform modules compose this class with their own sidecar locator.
/// </summary>
/// <param name="host">The overlay sidecar host.</param>
/// <param name="locator">The platform sidecar locator.</param>
/// <param name="logger">The logger.</param>
/// <param name="tunnelRunner">Optional in-process tunnel runner fallback.</param>
public sealed class SharedVirtualLanAdapter(
    IOverlaySidecarHost host,
    IOverlaySidecarLocator locator,
    ILogger<SharedVirtualLanAdapter> logger,
    ITunnelRunner? tunnelRunner = null) : IVirtualLanAdapter, IDisposable
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private bool _disposed;

    /// <inheritdoc/>
    public OnlineAdapterState State { get; private set; } = OnlineAdapterState.Down;

    /// <inheritdoc/>
    public string? OverlayIp { get; private set; }

    /// <inheritdoc/>
    public event EventHandler<OnlineAdapterState>? StateChanged;

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> BringUpAsync(
        string adapterConfig,
        string overlayIp,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            SetState(OnlineAdapterState.Starting);
            var hasSidecar = locator.LocateBinary() != null;
            var isPending = OverlayConfigInspector.TryGetOverlayName(adapterConfig) == OnlineConstants.OverlayPendingSelection;
            var start = isPending && !hasSidecar
                ? OperationResult<SidecarInfo>.CreateFailure("Overlay selection is pending.")
                : await host.StartAsync(adapterConfig, locator, cancellationToken);

            if (!start.Success)
            {
                if (tunnelRunner != null)
                {
                    logger.LogInformation(
                        "Sidecar not active ({Reason}); activating in-process virtual LAN tunnel runner.",
                        start.Errors.Count > 0 ? start.Errors[0] : "not found");

                    var runnerStart = await tunnelRunner.StartAsync(adapterConfig, overlayIp, cancellationToken);
                    if (runnerStart.Success)
                    {
                        OverlayIp = overlayIp;
                        SetState(OnlineAdapterState.Up);
                        return OperationResult<bool>.CreateSuccess(true);
                    }

                    logger.LogWarning("In-process tunnel runner start failed: {Error}", string.Join(", ", runnerStart.Errors));
                }

                if (isPending)
                {
                    logger.LogInformation("Overlay selection is pending; joined without tunneling.");
                    SetState(OnlineAdapterState.Down);
                    return OperationResult<bool>.CreateSuccess(true);
                }

                logger.LogWarning("Sidecar start failed.");
                return Fail(start.Errors.Count > 0 ? start.Errors[0] : "Sidecar start failed.");
            }

            OverlayIp = overlayIp;
            SetState(OnlineAdapterState.Up);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            try
            {
                _lifecycleLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently
            }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> TearDownAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            SetState(OnlineAdapterState.Stopping);
            var stop = await host.StopAsync(cancellationToken);
            if (tunnelRunner?.IsRunning == true)
            {
                await tunnelRunner.StopAsync(cancellationToken);
            }

            OverlayIp = null;
            SetState(OnlineAdapterState.Down);
            return stop;
        }
        finally
        {
            try
            {
                _lifecycleLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently
            }
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
        try
        {
            if (_lifecycleLock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None))
            {
                try
                {
                    TeardownSilently();
                }
                finally
                {
                    _lifecycleLock.Release();
                }
            }
            else
            {
                // An in-flight bring-up owns the lock with a live sidecar or
                // tunnel; stop it in the background so shutdown never orphans it.
                logger.LogWarning("Virtual LAN adapter disposal timed out waiting for the lifecycle lock; stopping in the background.");
                _ = StopOrphanedAsync();
            }
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }
        finally
        {
            _lifecycleLock.Dispose();
        }
    }

    private async Task StopOrphanedAsync()
    {
        try
        {
            await host.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Background host stop failed during adapter disposal: {Message}", ex.Message);
        }

        try
        {
            if (tunnelRunner?.IsRunning == true)
            {
                await tunnelRunner.StopAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Background tunnel runner stop failed during adapter disposal: {Message}", ex.Message);
        }
    }

    private void TeardownSilently()
    {
        if (State == OnlineAdapterState.Down)
        {
            return;
        }

        SetState(OnlineAdapterState.Stopping);
        StopHostSilently();
        StopTunnelRunnerSilently();
        OverlayIp = null;
        SetState(OnlineAdapterState.Down);
    }

    private void StopHostSilently()
    {
        try
        {
            // Off the calling thread so continuations inside StopAsync never
            // marshal back to a blocked UI synchronization context.
            Task.Run(() => host.StopAsync(CancellationToken.None)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Host stop failed during adapter disposal: {Message}", ex.Message);
        }
    }

    private void StopTunnelRunnerSilently()
    {
        if (tunnelRunner?.IsRunning != true)
        {
            return;
        }

        try
        {
            Task.Run(() => tunnelRunner.StopAsync(CancellationToken.None)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Tunnel runner stop failed during adapter disposal: {Message}", ex.Message);
        }
    }

    private void SetState(OnlineAdapterState next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        try
        {
            StateChanged?.Invoke(this, next);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Virtual LAN adapter state change notification threw.");
        }
    }

    private OperationResult<bool> Fail(string error)
    {
        SetState(OnlineAdapterState.Down);
        return OperationResult<bool>.CreateFailure(error);
    }
}
