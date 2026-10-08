using GenHub.Core.Models.Results;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Launching;

/// <summary>
/// Provisions Flatpak bundles so Linux game clients launch as installed applications.
/// </summary>
public interface IFlatpakProvisioner
{
    /// <summary>
    /// Ensures the bundle's application is installed, installing it on first use.
    /// </summary>
    /// <param name="bundlePath">Absolute path of the <c>.flatpak</c> bundle file.</param>
    /// <param name="cancellationToken">Cancels a running install.</param>
    /// <returns>The installed application ID, or a failure describing why provisioning is impossible.</returns>
    Task<OperationResult<string>> EnsureInstalledAsync(string bundlePath, CancellationToken cancellationToken = default);
}
