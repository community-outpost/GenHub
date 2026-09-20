using GenHub.Core.Interfaces.Online;
using GenHub.Core.Services.Online;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows.Features.Online;

/// <summary>
/// Windows virtual LAN adapter backed by the overlay sidecar host.
/// </summary>
/// <param name="host">The overlay sidecar host.</param>
/// <param name="locator">The Windows sidecar locator.</param>
/// <param name="logger">The logger.</param>
public sealed class WindowsVirtualLanAdapter(
    IOverlaySidecarHost host,
    IOverlaySidecarLocator locator,
    ILogger<WindowsVirtualLanAdapter> logger) : VirtualLanAdapterBase(host, locator, logger)
{
}
