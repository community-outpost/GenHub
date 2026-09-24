using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.Services;

/// <summary>
/// HTTP client for the Online edge control plane (directory, sessions, join grants).
/// The edge only handles trust and control; game traffic stays on the P2P overlay.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="adapter">The platform virtual LAN adapter.</param>
/// <param name="presence">The presence channel service.</param>
/// <param name="p2p">The P2P connection service for endpoint discovery.</param>
/// <param name="logger">The logger.</param>
public sealed class OnlineNetworkService(
    IHttpClientFactory httpClientFactory,
    IVirtualLanAdapter adapter,
    IOnlinePresenceService presence,
    IP2PConnectionService p2p,
    ILogger<OnlineNetworkService> logger) : IOnlineNetworkService
{
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly SemaphoreSlim _failoverLock = new(1, 1);
    private readonly AsyncLocal<bool> _isFailingOver = new();
    private readonly object _joinLock = new();
    private string? _sessionToken;
    private bool _presenceSubscribed;
    private OnlineJoinResult? _currentJoin;
    private IReadOnlyList<OnlineMember> _latestRoster = [];

    /// <inheritdoc/>
    public OnlineJoinResult? CurrentJoin
    {
        get
        {
            lock (_joinLock)
            {
                return _currentJoin;
            }
        }

        private set
        {
            lock (_joinLock)
            {
                _currentJoin = value;
            }
        }
    }

    /// <inheritdoc/>
    public string LocalEndpoint { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public OnlineAdapterState AdapterState => adapter.State;

    /// <inheritdoc/>
    public event EventHandler<IReadOnlyList<OnlineMember>>? RosterChanged;

    /// <inheritdoc/>
    public event EventHandler? ConnectionLost;

    /// <inheritdoc/>
    public event EventHandler<OnlineExpectedProfile>? ExpectedProfileChanged;

    /// <inheritdoc/>
    public async Task<OperationResult<IReadOnlyList<OnlineNetworkSummary>>> GetNetworksAsync(
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = ApiConstants.OnlineNetworksEndpoint;
            if (!string.IsNullOrWhiteSpace(search))
            {
                url += string.Format(ApiConstants.OnlineNetworksSearchFormat, Uri.EscapeDataString(search.Trim()));
            }

            using var response = await SendWithSessionRetryAsync(
                (client, ct) => client.GetAsync(url, ct),
                cancellationToken);
            if (response is null)
            {
                return OperationResult<IReadOnlyList<OnlineNetworkSummary>>.CreateFailure(
                    OnlineConstants.ErrorServiceUnavailable);
            }

            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<IReadOnlyList<OnlineNetworkSummary>>.CreateFailure(
                    await ReadErrorAsync(response, cancellationToken));
            }

            var networks = await response.Content.ReadFromJsonAsync<IReadOnlyList<OnlineNetworkSummary>>(
                cancellationToken) ?? [];
            return OperationResult<IReadOnlyList<OnlineNetworkSummary>>.CreateSuccess(DropStaleEntries(networks));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Online directory unreachable.");
            return OperationResult<IReadOnlyList<OnlineNetworkSummary>>.CreateFailure(
                OnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Online directory request timed out.");
            return OperationResult<IReadOnlyList<OnlineNetworkSummary>>.CreateFailure(
                OnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Online directory response was malformed.");
            return OperationResult<IReadOnlyList<OnlineNetworkSummary>>.CreateFailure(
                OnlineConstants.ErrorServiceUnavailable);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<OnlineNetworkDetail>> GetNetworkDetailAsync(
        string networkId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(networkId))
        {
            return OperationResult<OnlineNetworkDetail>.CreateFailure(OnlineConstants.ErrorNetworkNotFound);
        }

        try
        {
            var url = string.Format(ApiConstants.OnlineNetworkByIdFormat, Uri.EscapeDataString(networkId));
            using var response = await SendWithSessionRetryAsync(
                (client, ct) => client.GetAsync(url, ct),
                cancellationToken);
            if (response is null)
            {
                return OperationResult<OnlineNetworkDetail>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
            }

            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<OnlineNetworkDetail>.CreateFailure(
                    await ReadErrorAsync(response, cancellationToken));
            }

            var detail = await response.Content.ReadFromJsonAsync<OnlineNetworkDetail>(cancellationToken);
            if (detail is null)
            {
                return OperationResult<OnlineNetworkDetail>.CreateFailure(OnlineConstants.ErrorNetworkNotFound);
            }

            return OperationResult<OnlineNetworkDetail>.CreateSuccess(detail);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Online detail request failed.");
            return OperationResult<OnlineNetworkDetail>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Online detail request timed out.");
            return OperationResult<OnlineNetworkDetail>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Online detail response was malformed.");
            return OperationResult<OnlineNetworkDetail>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<OnlineJoinResult>> CreateNetworkAsync(
        OnlineCreateNetworkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = ValidateCreateRequest(request);
        if (!validation.Success)
        {
            return OperationResult<OnlineJoinResult>.CreateFailure(validation);
        }

        var activated = false;
        try
        {
            var endpoint = await ResolvePublicEndpointAsync(request.PreferRelay, cancellationToken);
            using var response = await SendWithSessionRetryAsync(
                (client, ct) => client.PostAsJsonAsync(
                    ApiConstants.OnlineNetworksEndpoint, request with { Endpoint = endpoint }, ct),
                cancellationToken);
            if (response is null)
            {
                return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
            }

            var result = await ActivateJoinFromResponseAsync(response, cancellationToken);
            activated = result.Success;
            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Online network creation failed.");
            return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Online network creation timed out.");
            return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Online creation response was malformed.");
            return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        finally
        {
            if (!activated)
            {
                await ReleaseEndpointAsync();
            }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<OnlineJoinResult>> JoinNetworkAsync(
        string networkId,
        string password,
        bool preferRelay = true,
        string profileFingerprint = "",
        string profileName = "",
        string displayName = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(networkId))
        {
            return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorNetworkNotFound);
        }

        var activated = false;
        try
        {
            var endpoint = await ResolvePublicEndpointAsync(preferRelay, cancellationToken);
            var url = string.Format(ApiConstants.OnlineNetworkJoinFormat, Uri.EscapeDataString(networkId));
            using var response = await SendWithSessionRetryAsync(
                (client, ct) => client.PostAsJsonAsync(
                    url, new { password, preferRelay, endpoint, profileFingerprint, profileName, displayName }, ct),
                cancellationToken);
            if (response is null)
            {
                return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // A second consecutive session rejection (retry exhausted)
                // must not surface as a wrong password.
                var code = await ReadUnauthorizedCodeAsync(response, cancellationToken);
                var error = string.Equals(code, OnlineConstants.ErrorSessionRequired, StringComparison.Ordinal)
                    ? OnlineConstants.ErrorServiceUnavailable
                    : OnlineConstants.ErrorWrongPassword;
                return OperationResult<OnlineJoinResult>.CreateFailure(error);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorNetworkFull);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorNetworkBanned);
            }

            var joinResult = await ActivateJoinFromResponseAsync(response, cancellationToken);
            activated = joinResult.Success;
            return joinResult;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Online join request failed.");
            return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Online join request timed out.");
            return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Online join response was malformed.");
            return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        finally
        {
            if (!activated)
            {
                await ReleaseEndpointAsync();
            }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> LeaveNetworkAsync(CancellationToken cancellationToken = default)
    {
        var join = TakeJoin();
        _latestRoster = [];
        LocalEndpoint = string.Empty;

        if (join is not null)
        {
            await NotifyLeaveAsync(join, cancellationToken);
        }

        await presence.DisconnectAsync(cancellationToken);
        UnsubscribePresence();
        await p2p.StopListeningAsync(cancellationToken);

        var teardown = await adapter.TearDownAsync(cancellationToken);
        if (!teardown.Success)
        {
            logger.LogWarning("Adapter teardown reported errors: {Errors}", OnlineLogScrubber.Scrub(string.Join("; ", teardown.Errors)));
        }

        RosterChanged?.Invoke(this, []);
        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> UpdateNetworkAsync(
        string? description,
        OnlineExpectedProfile? expectedProfile,
        CancellationToken cancellationToken = default)
    {
        var join = CurrentJoin;
        if (join is null)
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure(OnlineConstants.ErrorServiceUnavailable));
        }

        var url = string.Format(ApiConstants.OnlineNetworkByIdFormat, Uri.EscapeDataString(join.NetworkId));
        object payload = expectedProfile is null
            ? new { description }
            : new
            {
                description,
                expectedProfileId = expectedProfile.ExpectedProfileId,
                expectedProfileFingerprint = expectedProfile.ExpectedProfileFingerprint,
                expectedProfileName = expectedProfile.ExpectedProfileName,
                expectedGameClientId = expectedProfile.ExpectedGameClientId,
                expectedContentIds = expectedProfile.ExpectedContentIds,
            };
        return SendGrantMutationAsync(join.Grant, url, HttpMethod.Patch, payload, cancellationToken);
    }

    /// <inheritdoc/>
    public void SetLocalProfileAdvertisement(string fingerprint, string profileName, string displayName = "")
    {
        presence.UpdateAdvertisedProfile(fingerprint, profileName, displayName);
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> ReportMemberAsync(
        string overlayIp,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var join = CurrentJoin;
        if (join is null || string.IsNullOrWhiteSpace(overlayIp) || string.IsNullOrWhiteSpace(reason))
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure(OnlineConstants.ErrorServiceUnavailable));
        }

        var url = string.Format(ApiConstants.OnlineReportFormat, Uri.EscapeDataString(join.NetworkId));
        return SendGrantMutationAsync(join.Grant, url, HttpMethod.Post, new { targetIp = overlayIp, reason }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> BanMemberAsync(
        string overlayIp,
        CancellationToken cancellationToken = default)
    {
        var join = CurrentJoin;
        if (join is null || string.IsNullOrWhiteSpace(overlayIp))
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure(OnlineConstants.ErrorServiceUnavailable));
        }

        var url = string.Format(ApiConstants.OnlineBanFormat, Uri.EscapeDataString(join.NetworkId));
        return SendGrantMutationAsync(join.Grant, url, HttpMethod.Post, new { targetIp = overlayIp }, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<OnlineMeshCheckResult>> RunMeshCheckAsync(CancellationToken cancellationToken = default)
    {
        var join = CurrentJoin;
        if (join is null)
        {
            return OperationResult<OnlineMeshCheckResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }

        // Relay members publish no endpoint and ourselves need no probe; only
        // direct peers prove the full mesh.
        var peers = _latestRoster
            .Where(m => !string.IsNullOrWhiteSpace(m.Endpoint) && !string.Equals(m.Endpoint, LocalEndpoint, StringComparison.Ordinal))
            .ToList();
        if (peers.Count == 0)
        {
            return OperationResult<OnlineMeshCheckResult>.CreateSuccess(new OnlineMeshCheckResult { Peers = [] });
        }

        // Relay joins never start the UDP listener, so probes would fail with
        // "Listener is not started" and report reachable peers as failed.
        // Start one for the check; without it there is nothing honest to report.
        if (!p2p.IsListening)
        {
            var listen = await p2p.StartListeningAsync(0, cancellationToken);
            if (listen is null || !listen.Success)
            {
                logger.LogInformation("Mesh check skipped: no UDP listener available for probing.");
                return OperationResult<OnlineMeshCheckResult>.CreateSuccess(new OnlineMeshCheckResult { Peers = [] });
            }
        }

        var results = await Task.WhenAll(peers.Select(m => ProbeMemberAsync(m, cancellationToken)));
        await Task.WhenAll(results.Select(r => ReportConnectionOutcomeAsync(join, r, cancellationToken)));
        return OperationResult<OnlineMeshCheckResult>.CreateSuccess(new OnlineMeshCheckResult { Peers = results });
    }

    private static IReadOnlyList<OnlineNetworkSummary> DropStaleEntries(IReadOnlyList<OnlineNetworkSummary> networks)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-OnlineConstants.DirectoryStaleSeconds);
        var fresh = networks.Where(n =>
        {
            if (n.LastHeartbeatUtc == default)
            {
                return true;
            }

            var utc = n.LastHeartbeatUtc.Kind switch
            {
                DateTimeKind.Utc => n.LastHeartbeatUtc,
                DateTimeKind.Local => n.LastHeartbeatUtc.ToUniversalTime(),
                _ => DateTime.SpecifyKind(n.LastHeartbeatUtc, DateTimeKind.Utc),
            };
            return utc >= cutoff;
        }).ToList();
        return fresh.Count == networks.Count ? networks : fresh;
    }

    private static OperationResult<bool> ValidateCreateRequest(OnlineCreateNetworkRequest request)
    {
        if (request.Name.Length < OnlineConstants.MinNetworkNameLength ||
            request.Name.Length > OnlineConstants.MaxNetworkNameLength)
        {
            return OperationResult<bool>.CreateFailure(OnlineConstants.ErrorInvalidName);
        }

        if (request.SlotsMax < 2 || request.SlotsMax > OnlineConstants.MaxSlotCap)
        {
            return OperationResult<bool>.CreateFailure(OnlineConstants.ErrorInvalidSlots);
        }

        if (request.Password.Length > OnlineConstants.MaxPasswordLength)
        {
            return OperationResult<bool>.CreateFailure(OnlineConstants.ErrorPasswordTooLong);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return OnlineConstants.ErrorNetworkNotFound;
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
            if (problem is not null)
            {
                if (problem.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code))
                {
                    return code;
                }

                if (problem.TryGetValue("error", out var errorMsg) && !string.IsNullOrWhiteSpace(errorMsg))
                {
                    return errorMsg;
                }

                if (problem.TryGetValue("message", out var message) && !string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            // Fall through to the generic error below.
        }

        return OnlineConstants.ErrorServiceUnavailable;
    }

    private static async Task<string?> ReadUnauthorizedCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return null;
        }

        // Restore the consumed body so the caller's error reader sees the same bytes.
        response.Content.Dispose();
        response.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("code", out var code))
            {
                return code.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsNetworkFailoverCandidate(Exception ex, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (ex is HttpRequestException or TimeoutException ||
         (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested));

    private static async Task<bool> IsSessionExpiredResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return false;
        }

        var code = await ReadUnauthorizedCodeAsync(response, cancellationToken);
        return string.Equals(code, OnlineConstants.ErrorSessionRequired, StringComparison.Ordinal);
    }

    private async Task<HttpResponseMessage?> SendWithSessionRetryAsync(
        Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        var first = await CreateAuthenticatedClientAsync(cancellationToken);
        if (first.Client is null)
        {
            return null;
        }

        using (first.Client)
        {
            var response = await TrySendInitialAsync(send, first.Client, first.Token, cancellationToken);
            if (response is null)
            {
                return null;
            }

            if (!await IsSessionExpiredResponseAsync(response, cancellationToken))
            {
                return response;
            }

            response.Dispose();
            return await RetrySendWithNewSessionAsync(send, first.Token, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage?> TrySendInitialAsync(
        Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> send,
        HttpClient client,
        string? token,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send(client, cancellationToken);
        }
        catch (Exception ex) when (IsNetworkFailoverCandidate(ex, cancellationToken))
        {
            var fallbackResponse = await TryFallbackSendAsync(send, token, cancellationToken);
            if (fallbackResponse is not null)
            {
                return fallbackResponse;
            }

            logger.LogWarning(ex, "Initial online request failed and fallback attempt was unsuccessful: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<HttpResponseMessage?> RetrySendWithNewSessionAsync(
        Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> send,
        string? expiredToken,
        CancellationToken cancellationToken)
    {
        await InvalidateSessionAsync(expiredToken, cancellationToken);
        var second = await CreateAuthenticatedClientAsync(cancellationToken);
        if (second.Client is null)
        {
            return null;
        }

        using (second.Client)
        {
            return await send(second.Client, cancellationToken);
        }
    }

    private async Task<(HttpClient? Client, string? Token)> CreateAuthenticatedClientAsync(CancellationToken cancellationToken)
    {
        var token = await EnsureSessionAsync(cancellationToken);
        if (token is null)
        {
            return (null, null);
        }

        var client = httpClientFactory.CreateClient(nameof(OnlineNetworkService));
        client.BaseAddress = new Uri(ApiConstants.OnlineEdgeBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(OnlineConstants.HttpTimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, token);
    }

    private async Task InvalidateSessionAsync(string? tokenUsed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tokenUsed))
        {
            return;
        }

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            // Only clear when nobody already rotated past the failed token.
            if (string.Equals(_sessionToken, tokenUsed, StringComparison.Ordinal))
            {
                _sessionToken = null;
            }
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private async Task<string?> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_sessionToken))
        {
            return _sessionToken;
        }

        // The fallback attempt runs outside the session lock: the shared
        // failover helper owns its own lock, and nesting them in opposite
        // order across the send and session paths could deadlock.
        Exception? failoverCause = null;
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_sessionToken))
            {
                return _sessionToken;
            }

            try
            {
                _sessionToken = await RequestSessionTokenAsync(ApiConstants.OnlineEdgeBaseUrl, cancellationToken);
                return _sessionToken;
            }
            catch (Exception ex) when (IsNetworkFailoverCandidate(ex, cancellationToken))
            {
                failoverCause = ex;
            }
        }
        finally
        {
            _sessionLock.Release();
        }

        var fallbackToken = await TryFallbackSessionAsync(cancellationToken);
        if (fallbackToken is not null)
        {
            await _sessionLock.WaitAsync(cancellationToken);
            try
            {
                _sessionToken = fallbackToken;
                return fallbackToken;
            }
            finally
            {
                _sessionLock.Release();
            }
        }

        logger.LogWarning(failoverCause, "Primary session request failed and fallback attempt was unsuccessful: {Message}", failoverCause.Message);
        return null;
    }

    private async Task<HttpResponseMessage?> TryFallbackSendAsync(
        Func<HttpClient, CancellationToken, Task<HttpResponseMessage>> send,
        string? priorToken,
        CancellationToken cancellationToken)
    {
        return await TryEdgeFailoverAsync(AttemptSendAsync, cancellationToken);

        async Task<HttpResponseMessage?> AttemptSendAsync(string fallbackUrl, CancellationToken ct)
        {
            await InvalidateSessionAsync(priorToken, ct);
            var fallbackClient = await CreateAuthenticatedClientAsync(ct);
            if (fallbackClient.Client is null)
            {
                return null;
            }

            using (fallbackClient.Client)
            {
                return await send(fallbackClient.Client, ct);
            }
        }
    }

    private async Task<string?> TryFallbackSessionAsync(CancellationToken cancellationToken)
    {
        return await TryEdgeFailoverAsync(RequestSessionTokenAsync, cancellationToken);
    }

    /// <summary>
    /// Runs one attempt against the fallback edge when a distinct fallback is
    /// configured. Owns the active-edge switch and its reset under a dedicated
    /// lock so concurrent failovers cannot interleave set and reset, and
    /// propagates caller cancellation instead of reporting it as an outage.
    /// A successful attempt keeps the sticky fallback; only failures reset.
    /// </summary>
    /// <typeparam name="T">The attempt result type.</typeparam>
    /// <param name="attempt">The operation to run against the fallback base URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempt result, or null when no distinct fallback exists or the attempt failed.</returns>
    private async Task<T?> TryEdgeFailoverAsync<T>(
        Func<string, CancellationToken, Task<T?>> attempt,
        CancellationToken cancellationToken)
        where T : class
    {
        // Compare against the primary, not the active URL: once failed over,
        // the active URL equals the fallback, and that must still retry.
        if (string.Equals(ApiConstants.PrimaryOnlineEdgeBaseUrl, ApiConstants.FallbackOnlineEdgeBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_isFailingOver.Value)
        {
            // Already inside a failover attempt on this async context (e.g. EnsureSessionAsync
            // failing over while AttemptSendAsync is executing under _failoverLock).
            // Avoid self-deadlock on _failoverLock and execute directly against the active fallback URL.
            try
            {
                return await attempt(ApiConstants.FallbackOnlineEdgeBaseUrl, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Nested fallback edge attempt failed: {Message}", ex.Message);
                return null;
            }
        }

        await _failoverLock.WaitAsync(cancellationToken);
        try
        {
            _isFailingOver.Value = true;
            ApiConstants.ActiveOnlineEdgeBaseUrl = ApiConstants.FallbackOnlineEdgeBaseUrl;
            try
            {
                return await attempt(ApiConstants.FallbackOnlineEdgeBaseUrl, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ApiConstants.ResetActiveOnlineEdgeBaseUrl();
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Fallback edge attempt failed: {Message}", ex.Message);
                ApiConstants.ResetActiveOnlineEdgeBaseUrl();
                return null;
            }
        }
        finally
        {
            _isFailingOver.Value = false;
            _failoverLock.Release();
        }
    }

    private async Task<string?> RequestSessionTokenAsync(string baseUrl, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(nameof(OnlineNetworkService));
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(OnlineConstants.HttpTimeoutSeconds);
        using var response = await client.PostAsync(ApiConstants.OnlineSessionsEndpoint, null, cancellationToken);
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(cancellationToken);
        if (session is null || !session.TryGetValue("token", out var token) || string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Session issuance returned no token.");
            return null;
        }

        return token;
    }

    private async Task<OperationResult<OnlineJoinResult>> ActivateJoinFromResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return OperationResult<OnlineJoinResult>.CreateFailure(
                await ReadErrorAsync(response, cancellationToken));
        }

        var join = await response.Content.ReadFromJsonAsync<OnlineJoinResult>(cancellationToken);
        if (join is null)
        {
            return OperationResult<OnlineJoinResult>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }

        return await ActivateJoinAsync(join, cancellationToken);
    }

    private async Task<OperationResult<OnlineJoinResult>> ActivateJoinAsync(
        OnlineJoinResult join,
        CancellationToken cancellationToken)
    {
        var bringUp = await adapter.BringUpAsync(join.AdapterConfig, join.OverlayIp, cancellationToken);
        if (!bringUp.Success)
        {
            logger.LogWarning("Adapter bring-up failed for network {NetworkId}; staying joined without tunneling.", join.NetworkId);
            await adapter.TearDownAsync(cancellationToken);
        }

        CurrentJoin = join;
        _latestRoster = join.Members;
        RosterChanged?.Invoke(this, join.Members);
        SubscribePresence();

        var presenceResult = await presence.ConnectAsync(join.NetworkId, join.Grant, join.GrantExpiresUtc, cancellationToken);
        if (!presenceResult.Success)
        {
            logger.LogWarning("Presence channel failed; continuing with the join-time roster.");
        }

        return OperationResult<OnlineJoinResult>.CreateSuccess(join);
    }

    private OnlineJoinResult? TakeJoin()
    {
        lock (_joinLock)
        {
            var join = _currentJoin;
            _currentJoin = null;
            return join;
        }
    }

    private void SubscribePresence()
    {
        if (_presenceSubscribed)
        {
            return;
        }

        _presenceSubscribed = true;
        presence.RosterUpdated += OnPresenceRoster;
        presence.ConnectionLost += OnPresenceLost;
        presence.GrantRefreshed += OnGrantRefreshed;
        presence.ExpectedProfileChanged += OnExpectedProfileChanged;
    }

    private void UnsubscribePresence()
    {
        if (!_presenceSubscribed)
        {
            return;
        }

        _presenceSubscribed = false;
        presence.RosterUpdated -= OnPresenceRoster;
        presence.ConnectionLost -= OnPresenceLost;
        presence.GrantRefreshed -= OnGrantRefreshed;
        presence.ExpectedProfileChanged -= OnExpectedProfileChanged;
    }

    private void OnPresenceRoster(object? sender, IReadOnlyList<OnlineMember> members)
    {
        _latestRoster = members;
        RosterChanged?.Invoke(this, members);
    }

    private void OnGrantRefreshed(object? sender, OnlineCertResult cert)
    {
        var join = CurrentJoin;
        if (join is not null)
        {
            // The refreshed adapter config carries fresh TURN credentials;
            // keep it even while tunneling waits for the overlay selection.
            var adapterConfig = string.IsNullOrWhiteSpace(cert.AdapterConfig) ? join.AdapterConfig : cert.AdapterConfig;
            CurrentJoin = join with { Grant = cert.Grant, AdapterConfig = adapterConfig };
        }
    }

    private void OnExpectedProfileChanged(object? sender, OnlineExpectedProfile expected)
    {
        var join = CurrentJoin;
        if (join is not null)
        {
            CurrentJoin = join with
            {
                ExpectedProfileId = expected.ExpectedProfileId,
                ExpectedProfileFingerprint = expected.ExpectedProfileFingerprint,
                ExpectedProfileName = expected.ExpectedProfileName,
                ExpectedGameClientId = expected.ExpectedGameClientId,
                ExpectedContentIds = expected.ExpectedContentIds,
            };
        }

        ExpectedProfileChanged?.Invoke(this, expected);
    }

    private async void OnPresenceLost(object? sender, EventArgs e)
    {
        try
        {
            if (CurrentJoin is null)
            {
                return;
            }

            logger.LogWarning("Presence lost; leaving the network.");
            await LeaveNetworkAsync();
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-leave after presence loss failed.");
        }
    }

    private async Task ReleaseEndpointAsync()
    {
        // A failed join or create must not keep the UDP listener bound:
        // LeaveNetworkAsync never runs for joins that were never activated.
        // Endpoint release is never cancellable, hence CancellationToken.None.
        LocalEndpoint = string.Empty;
        await p2p.StopListeningAsync(CancellationToken.None);
    }

    private async Task<OnlineMeshPeerResult> ProbeMemberAsync(OnlineMember member, CancellationToken cancellationToken)
    {
        var reachable = false;
        if (IPEndPoint.TryParse(member.Endpoint, out var target))
        {
            var probe = await p2p.ProbePeerAsync(target.Address.ToString(), target.Port, cancellationToken);
            reachable = probe.Success && probe.Data;
        }

        if (!reachable)
        {
            logger.LogWarning("Mesh probe found no route to a lobby member.");
        }

        return new OnlineMeshPeerResult { OverlayIp = member.OverlayIp, Reachable = reachable };
    }

    private async Task ReportConnectionOutcomeAsync(
        OnlineJoinResult join,
        OnlineMeshPeerResult peer,
        CancellationToken cancellationToken)
    {
        var url = string.Format(ApiConstants.OnlineOutcomeFormat, Uri.EscapeDataString(join.NetworkId));
        var outcome = peer.Reachable ? OnlineConstants.OutcomeDirect : OnlineConstants.OutcomeFailed;
        var reported = await SendGrantMutationAsync(
            join.Grant, url, HttpMethod.Post, new { targetIp = peer.OverlayIp, direct = true, outcome }, cancellationToken);
        if (!reported.Success)
        {
            logger.LogWarning("Connection-outcome report was dropped.");
        }
    }

    private async Task<string> ResolvePublicEndpointAsync(bool preferRelay, CancellationToken cancellationToken)
    {
        if (preferRelay)
        {
            return string.Empty;
        }

        try
        {
            var listen = await p2p.StartListeningAsync(0, cancellationToken);
            if (!listen.Success)
            {
                logger.LogInformation("No UDP listener; falling back to relay.");
                return string.Empty;
            }

            var endpoints = await p2p.GetLocalAndPublicEndpointsAsync(cancellationToken);
            if (!endpoints.Success || endpoints.Data?.Public is null)
            {
                logger.LogInformation("STUN discovery failed; falling back to relay.");
                return string.Empty;
            }

            LocalEndpoint = endpoints.Data.Public.ToString();
            return LocalEndpoint;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException)
        {
            logger.LogWarning(ex, "Endpoint discovery failed; joining without a published endpoint.");
            return string.Empty;
        }
    }

    private HttpClient CreateGrantClient(string grant)
    {
        var client = httpClientFactory.CreateClient(nameof(OnlineNetworkService));
        client.BaseAddress = new Uri(ApiConstants.OnlineEdgeBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(OnlineConstants.HttpTimeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", grant);
        return client;
    }

    private async Task NotifyLeaveAsync(OnlineJoinResult join, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateGrantClient(join.Grant);
            var url = string.Format(ApiConstants.OnlineLeaveFormat, Uri.EscapeDataString(join.NetworkId));
            using var response = await client.PostAsync(url, null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Server leave returned {Status}.", (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Server leave notification failed.");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Server leave notification timed out.");
        }
    }

    private async Task<OperationResult<bool>> SendGrantMutationAsync(
        string grant,
        string url,
        HttpMethod method,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateGrantClient(grant);
            using var request = new HttpRequestMessage(method, url)
            {
                Content = JsonContent.Create(payload),
            };
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return OperationResult<bool>.CreateFailure(await ReadErrorAsync(response, cancellationToken));
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Online network mutation failed.");
            return OperationResult<bool>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Online network mutation timed out.");
            return OperationResult<bool>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Online mutation response was malformed.");
            return OperationResult<bool>.CreateFailure(OnlineConstants.ErrorServiceUnavailable);
        }
    }
}
