using GenHub.Core.Interfaces.Online;
using GenHub.Core.Services.Online;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

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
    ILogger<MacOSVirtualLanAdapter> logger) : VirtualLanAdapterBase(host, locator, logger)
{
}
