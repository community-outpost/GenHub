using GenHub.Core.Models.Results;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Provisions the OS virtual LAN interface (create, address, bring up) that
/// the tunnel runner then attaches to. Only Linux needs this: interface
/// creation requires privileges the app does not have, so the implementation
/// escalates through the platform helper (polkit/sudo) or reports the exact
/// manual commands when escalation is unavailable.
/// </summary>
public interface ITunInterfaceSetup
{
    /// <summary>
    /// Creates the interface when missing, assigns the overlay address, and
    /// brings the link up. The interface is left persistent across joins, so
    /// re-running setup for a new overlay IP just re-addresses it.
    /// </summary>
    /// <param name="interfaceName">The interface name (e.g., "genhub0").</param>
    /// <param name="overlayIp">The overlay IPv4 address to assign.</param>
    /// <param name="prefixLength">The subnet prefix length (e.g., 20).</param>
    /// <param name="mtu">The MTU to configure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the interface exists with the overlay address and is up.</returns>
    Task<OperationResult<bool>> SetupAsync(
        string interfaceName,
        IPAddress overlayIp,
        int prefixLength,
        int mtu,
        CancellationToken cancellationToken = default);
}
