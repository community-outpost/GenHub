using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.Services;

/// <summary>
/// Fallback virtual LAN adapter used when no platform implementation is composed.
/// Bring-up always reports unavailable; teardown is a no-op success.
/// </summary>
public sealed class NullVirtualLanAdapter : IVirtualLanAdapter
{
    /// <inheritdoc/>
    public event EventHandler<OnlineAdapterState>? StateChanged;

    /// <inheritdoc/>
    public OnlineAdapterState State { get; private set; } = OnlineAdapterState.Down;

    /// <inheritdoc/>
    public string? OverlayIp => null;

    /// <inheritdoc/>
    public Task<OperationResult<bool>> BringUpAsync(
        string adapterConfig,
        string overlayIp,
        CancellationToken cancellationToken = default)
    {
        SetState(OnlineAdapterState.Error);
        SetState(OnlineAdapterState.Down);
        return Task.FromResult(OperationResult<bool>.CreateFailure("Virtual LAN adapter is not available on this platform."));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> TearDownAsync(CancellationToken cancellationToken = default)
    {
        SetState(OnlineAdapterState.Down);
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    private void SetState(OnlineAdapterState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
