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
/// Shared virtual LAN adapter orchestration backed by the overlay sidecar host.
/// Platform modules compose this class with their own sidecar locator.
/// </summary>
/// <param name="host">The overlay sidecar host.</param>
/// <param name="locator">The platform sidecar locator.</param>
/// <param name="logger">The logger.</param>
public sealed class SharedVirtualLanAdapter(
    IOverlaySidecarHost host,
    IOverlaySidecarLocator locator,
    ILogger<SharedVirtualLanAdapter> logger) : IVirtualLanAdapter, IDisposable
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
        if (OverlayConfigInspector.TryGetOverlayName(adapterConfig) == OnlineConstants.OverlayPendingSelection)
        {
            // Expected pre-overlay state, not an error: the lobby (roster and
            // presence) works while tunneling waits for the overlay selection.
            logger.LogInformation("Overlay selection is pending; joined without tunneling.");
            return OperationResult<bool>.CreateSuccess(true);
        }

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            SetState(OnlineAdapterState.Starting);
            var start = await host.StartAsync(adapterConfig, locator, cancellationToken);
            if (!start.Success)
            {
                logger.LogWarning("Sidecar start failed.");
                return Fail(start.Errors.Count > 0 ? start.Errors[0] : "Sidecar start failed.");
            }

            OverlayIp = overlayIp;
            SetState(OnlineAdapterState.Up);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> TearDownAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            SetState(OnlineAdapterState.Stopping);
            await host.StopAsync(cancellationToken);
            OverlayIp = null;
            SetState(OnlineAdapterState.Down);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _lifecycleLock.Release();
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
        _lifecycleLock.Dispose();
    }

    private OperationResult<bool> Fail(string message)
    {
        SetState(OnlineAdapterState.Error);
        SetState(OnlineAdapterState.Down);
        return OperationResult<bool>.CreateFailure(message);
    }

    private void SetState(OnlineAdapterState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
