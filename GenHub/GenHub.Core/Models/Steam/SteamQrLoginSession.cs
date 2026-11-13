using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Models.Steam;

/// <summary>
/// Represents an active Steam QR code login session.
/// </summary>
public sealed class SteamQrLoginSession
{
    /// <summary>
    /// Occurs when the QR code image is refreshed due to a Steam challenge renewal.
    /// </summary>
    public event EventHandler<ReadOnlyMemory<byte>>? QrCodeRefreshed;

    /// <summary>
    /// Gets the Steam challenge URL encoded in the QR code.
    /// </summary>
    public string ChallengeUrl { get; private set; }

    /// <summary>
    /// Gets the PNG image bytes of the generated QR code.
    /// </summary>
    public ReadOnlyMemory<byte> QrCodePngBytes { get; private set; }

    /// <summary>
    /// Gets the callback to wait for user approval in the Steam Mobile App.
    /// </summary>
    public Func<CancellationToken, Task<SteamAccountInfo?>> WaitForApprovalAsync { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamQrLoginSession"/> class.
    /// </summary>
    /// <param name="challengeUrl">The initial challenge URL.</param>
    /// <param name="qrCodePngBytes">The initial QR code PNG image bytes.</param>
    /// <param name="waitForApprovalAsync">The callback to wait for user approval.</param>
    public SteamQrLoginSession(
        string challengeUrl,
        ReadOnlyMemory<byte> qrCodePngBytes,
        Func<CancellationToken, Task<SteamAccountInfo?>> waitForApprovalAsync)
    {
        ChallengeUrl = challengeUrl;
        QrCodePngBytes = qrCodePngBytes;
        WaitForApprovalAsync = waitForApprovalAsync;
    }

    /// <summary>
    /// Updates the session with a renewed challenge URL and regenerated QR code bytes.
    /// </summary>
    /// <param name="challengeUrl">The renewed challenge URL.</param>
    /// <param name="qrCodePngBytes">The regenerated QR code PNG image bytes.</param>
    public void UpdateQrCode(string challengeUrl, ReadOnlyMemory<byte> qrCodePngBytes)
    {
        ChallengeUrl = challengeUrl;
        QrCodePngBytes = qrCodePngBytes;
        QrCodeRefreshed?.Invoke(this, qrCodePngBytes);
    }
}
