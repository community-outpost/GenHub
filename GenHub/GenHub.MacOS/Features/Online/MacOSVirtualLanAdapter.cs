using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.MacOS.Features.Online;

/// <summary>
/// macOS virtual LAN adapter backed by the overlay sidecar host.
/// </summary>
/// <param name="host">The overlay sidecar host.</param>
/// <param name="locator">The macOS sidecar locator.</param>
/// <param name="logger">The logger.</param>
[SupportedOSPlatform("macos")]
public sealed class MacOSVirtualLanAdapter(
    IOverlaySidecarHost host,
    IOverlaySidecarLocator locator,
    ILogger<MacOSVirtualLanAdapter> logger) : IVirtualLanAdapter
{
    /// <inheritdoc/>
    public event EventHandler<OnlineAdapterState>? StateChanged;

    /// <inheritdoc/>
    public OnlineAdapterState State { get; private set; } = OnlineAdapterState.Down;

    /// <inheritdoc/>
    public string? OverlayIp { get; private set; }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> BringUpAsync(
        string adapterConfig,
        string overlayIp,
        CancellationToken cancellationToken = default)
    {
        if (OverlayConfigInspector.TryGetOverlayName(adapterConfig) == OnlineConstants.OverlayPendingSelection)
        {
            logger.LogWarning("Overlay selection is pending; bring-up unavailable.");
            return Fail("Virtual LAN overlay is not available yet.");
        }

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

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> TearDownAsync(CancellationToken cancellationToken = default)
    {
        SetState(OnlineAdapterState.Stopping);
        await host.StopAsync(cancellationToken);
        OverlayIp = null;
        SetState(OnlineAdapterState.Down);
        return OperationResult<bool>.CreateSuccess(true);
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
