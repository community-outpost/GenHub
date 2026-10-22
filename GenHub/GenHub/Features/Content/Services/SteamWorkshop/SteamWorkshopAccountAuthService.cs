using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Steam;
using Microsoft.Extensions.Logging;
using QRCoder;
using SteamKit2;
using SteamKit2.Authentication;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Provides Steam account authentication (QR code login, credential storage, and session lifecycle)
/// for in-app 1-click Steam Workshop map downloads.
/// </summary>
public sealed class SteamWorkshopAccountAuthService : ISteamWorkshopAccountAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHostingCredentialStore _credentialStore;
    private readonly ILogger<SteamWorkshopAccountAuthService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamWorkshopAccountAuthService"/> class.
    /// </summary>
    /// <param name="credentialStore">The secure credential store.</param>
    /// <param name="logger">The logger.</param>
    public SteamWorkshopAccountAuthService(
        IHostingCredentialStore credentialStore,
        ILogger<SteamWorkshopAccountAuthService> logger)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<bool> IsAccountConnectedAsync(CancellationToken cancellationToken = default)
    {
        var info = await GetAccountInfoAsync(cancellationToken);
        return info != null && !string.IsNullOrWhiteSpace(info.RefreshToken);
    }

    /// <inheritdoc/>
    public async Task<SteamAccountInfo?> GetAccountInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var raw = await _credentialStore.GetCredentialAsync(
                SteamWorkshopConstants.SteamAccountCredentialProviderId,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SteamAccountInfo>(raw, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retrieve or deserialize stored Steam account credentials.");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<SteamQrLoginSession> BeginQrLoginAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Beginning Steam QR code authentication session.");
        var steamClient = new SteamClient();
        var manager = new CallbackManager(steamClient);

        var tcsConnect = new TaskCompletionSource<bool>();
        manager.Subscribe<SteamClient.ConnectedCallback>(_ => tcsConnect.TrySetResult(true));
        manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
        {
            if (!tcsConnect.Task.IsCompleted)
            {
                tcsConnect.TrySetException(new IOException("Failed to connect to Steam network."));
            }
        });

        var (pumpCts, pumpTask) = StartCallbackPump(manager);

        try
        {
            await ConnectSteamClientAsync(steamClient, tcsConnect, cancellationToken).ConfigureAwait(false);

            var authSession = await steamClient.Authentication.BeginAuthSessionViaQRAsync(new AuthSessionDetails()).ConfigureAwait(false);
            var qrCodeBytes = GenerateQrBytes(authSession.ChallengeURL);

            async Task<SteamAccountInfo?> WaitForApprovalInternalAsync(CancellationToken ct)
            {
                try
                {
                    return await WaitForQrApprovalAsync(authSession, ct).ConfigureAwait(false);
                }
                finally
                {
                    await TeardownSessionAsync(pumpCts, pumpTask, steamClient).ConfigureAwait(false);
                }
            }

            var session = new SteamQrLoginSession(
                authSession.ChallengeURL,
                qrCodeBytes,
                WaitForApprovalInternalAsync);

            authSession.ChallengeURLChanged = () => OnChallengeUrlRenewed(session, authSession);

            return session;
        }
        catch
        {
            await TeardownSessionAsync(pumpCts, pumpTask, steamClient).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAccountAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Disconnecting Steam account credentials.");
        await _credentialStore.DeleteCredentialAsync(
            SteamWorkshopConstants.SteamAccountCredentialProviderId,
            cancellationToken);
    }

    private static (CancellationTokenSource Cts, Task Task) StartCallbackPump(CallbackManager manager)
    {
        var pumpCts = new CancellationTokenSource();
        var pumpTask = Task.Run(
            async () =>
            {
                while (!pumpCts.Token.IsCancellationRequested)
                {
                    manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(50));
                    try
                    {
                        await Task.Delay(20, pumpCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            },
            pumpCts.Token);

        return (pumpCts, pumpTask);
    }

    private static byte[] GenerateQrBytes(string url)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }

    private static async Task ConnectSteamClientAsync(
        SteamClient steamClient,
        TaskCompletionSource<bool> tcsConnect,
        CancellationToken cancellationToken)
    {
        steamClient.Connect();

        using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctsTimeout.Token);

        try
        {
            await tcsConnect.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ctsTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out connecting to the Steam network.");
        }
    }

    private async Task<SteamAccountInfo?> WaitForQrApprovalAsync(
        QrAuthSession authSession,
        CancellationToken ct)
    {
        AuthPollResult? pollResult;
        try
        {
            _logger.LogInformation("Waiting for user to approve Steam QR code in mobile app...");
            pollResult = await authSession.PollingWaitForResultAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error while awaiting Steam QR code approval.");
            return null;
        }

        if (pollResult == null || string.IsNullOrWhiteSpace(pollResult.RefreshToken))
        {
            _logger.LogWarning("Steam QR login poll returned no refresh token.");
            return null;
        }

        var steamId = SteamWorkshopHelper.ExtractSteamIdFromToken(pollResult.AccessToken);
        if (steamId == 0UL)
        {
            steamId = SteamWorkshopHelper.ExtractSteamIdFromToken(pollResult.RefreshToken);
        }

        var accountInfo = new SteamAccountInfo(
            pollResult.AccountName ?? "SteamUser",
            steamId,
            pollResult.RefreshToken);

        var json = JsonSerializer.Serialize(accountInfo, JsonOptions);
        await _credentialStore.SaveCredentialAsync(
            SteamWorkshopConstants.SteamAccountCredentialProviderId,
            json,
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Steam account '{AccountName}' successfully connected and stored securely.",
            accountInfo.AccountName);

        return accountInfo;
    }

    private void OnChallengeUrlRenewed(SteamQrLoginSession session, QrAuthSession authSession)
    {
        try
        {
            _logger.LogInformation("Steam challenge URL renewed; regenerating QR code image.");
            var updatedBytes = GenerateQrBytes(authSession.ChallengeURL);
            session.UpdateQrCode(authSession.ChallengeURL, updatedBytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to regenerate Steam QR code after challenge renewal.");
        }
    }

    private async Task TeardownSessionAsync(
        CancellationTokenSource pumpCts,
        Task pumpTask,
        SteamClient steamClient)
    {
        try
        {
            if (!pumpCts.IsCancellationRequested)
            {
                await pumpCts.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cancelling pump CTS failed during teardown.");
        }

        try
        {
            await pumpTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Background callback pump shutdown threw an exception.");
        }

        try
        {
            steamClient.Disconnect();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Steam client disconnect threw an exception during teardown.");
        }

        try
        {
            pumpCts.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disposing pump CTS threw an exception during teardown.");
        }
    }
}
