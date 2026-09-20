using GenHub.Core.Interfaces.Online;
using GenHub.Core.Services.Online;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace GenHub.Linux.Features.Online;

/// <summary>
/// Linux virtual LAN adapter backed by the overlay sidecar host.
/// </summary>
/// <param name="host">The overlay sidecar host.</param>
/// <param name="locator">The Linux sidecar locator.</param>
/// <param name="logger">The logger.</param>
[SupportedOSPlatform("linux")]
public sealed class LinuxVirtualLanAdapter(
    IOverlaySidecarHost host,
    IOverlaySidecarLocator locator,
    ILogger<LinuxVirtualLanAdapter> logger) : VirtualLanAdapterBase(host, locator, logger)
{
}
