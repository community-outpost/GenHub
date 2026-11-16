using GenHub.Core.Models.Steam;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Steam;

/// <summary>
/// Provides Steam account authentication (QR code login, credential storage, and session lifecycle)
/// for 1-click in-app Steam Workshop map downloads.
/// </summary>
public interface ISteamWorkshopAccountAuthService
{
    /// <summary>
    /// Checks whether a valid Steam account is currently connected.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a Steam account is connected; otherwise false.</returns>
    Task<bool> IsAccountConnectedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the connected Steam account info, or <c>null</c> if not connected.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connected account info or null.</returns>
    Task<SteamAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a QR code authentication session for scanning via the Steam Mobile App.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new QR login session containing QR code image bytes and an approval task.</returns>
    Task<SteamQrLoginSession> BeginQrLoginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the connected Steam account and deletes all stored credentials.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the disconnect operation.</returns>
    Task DisconnectAccountAsync(CancellationToken cancellationToken = default);
}
