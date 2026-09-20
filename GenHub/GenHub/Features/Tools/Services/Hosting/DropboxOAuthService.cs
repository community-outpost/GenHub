using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Performs Dropbox OAuth 2.0 authorization-code flow with PKCE over a loopback redirect,
/// plus refresh-grant calls that keep short-lived access tokens alive without user interaction.
/// </summary>
public class DropboxOAuthService(HttpClient httpClient, ILogger logger)
{
    private const string ExpiredAccessTokenTag = "expired_access_token";

    /// <summary>
    /// Creates a random PKCE verifier/challenge pair (S256).
    /// </summary>
    /// <returns>The verifier and its derived challenge.</returns>
    public static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(HostingConstants.OAuthPkceVerifierByteLength);
        var verifier = Base64UrlEncode(verifierBytes);
        return (verifier, ComputeCodeChallenge(verifier));
    }

    /// <summary>
    /// Derives the S256 PKCE challenge for a verifier (RFC 7636).
    /// </summary>
    /// <param name="verifier">The code verifier.</param>
    /// <returns>The base64url-encoded SHA-256 of the verifier.</returns>
    public static string ComputeCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Builds the Dropbox authorization URL the user visits in a browser.
    /// </summary>
    /// <param name="appKey">The Dropbox application key.</param>
    /// <param name="challenge">The PKCE code challenge.</param>
    /// <param name="redirectUri">The loopback redirect URI.</param>
    /// <returns>The authorization URL.</returns>
    public static string BuildAuthorizeUrl(string appKey, string challenge, string redirectUri)
    {
        var query = new StringBuilder();
        query.Append("response_type=code");
        query.Append("&client_id=").Append(Uri.EscapeDataString(appKey));
        query.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
        query.Append("&code_challenge=").Append(Uri.EscapeDataString(challenge));
        query.Append("&code_challenge_method=S256");
        query.Append("&token_access_type=offline");
        query.Append("&scope=").Append(Uri.EscapeDataString(HostingConstants.DropboxOAuthScopes));
        return $"{HostingConstants.DropboxOAuthAuthorizeUrl}?{query}";
    }

    /// <summary>
    /// Extracts the authorization code (or provider error) from a loopback callback URI.
    /// </summary>
    /// <param name="callbackUri">The URI the browser was redirected to.</param>
    /// <returns>The code and error, if any.</returns>
    public static (string? Code, string? Error) ExtractAuthorizationCode(Uri callbackUri)
    {
        var query = callbackUri.Query;
        if (string.IsNullOrEmpty(query))
        {
            return (null, null);
        }

        string? code = null;
        string? error = null;
        var pairs = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
            if (string.Equals(name, "code", StringComparison.OrdinalIgnoreCase))
            {
                code = value;
            }
            else if (string.Equals(name, "error", StringComparison.OrdinalIgnoreCase))
            {
                error = value;
            }
        }

        return (code, error);
    }

    /// <summary>
    /// Serializes an OAuth credential set for the secure credential store.
    /// </summary>
    /// <param name="credential">The credential to serialize.</param>
    /// <returns>Versioned JSON payload.</returns>
    public static string SerializeCredential(DropboxOAuthCredential credential)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", HostingConstants.DropboxCredentialPayloadVersion);
            writer.WriteString("appKey", credential.AppKey);
            writer.WriteString("access", credential.AccessToken);
            writer.WriteString("refresh", credential.RefreshToken);
            writer.WriteString("expiresAt", credential.ExpiresAtUtc.ToString("O"));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Parses a stored credential payload. Returns false for legacy bare access tokens.
    /// </summary>
    /// <param name="payload">The stored payload.</param>
    /// <param name="credential">The parsed credential, if valid.</param>
    /// <returns>True when the payload is a versioned OAuth credential.</returns>
    public static bool TryParseCredential(string? payload, out DropboxOAuthCredential? credential)
    {
        credential = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("v", out var version) ||
                version.GetInt32() != HostingConstants.DropboxCredentialPayloadVersion)
            {
                return false;
            }

            var appKey = GetStringProperty(root, "appKey");
            var access = GetStringProperty(root, "access");
            var refresh = GetStringProperty(root, "refresh");
            var expiresRaw = GetStringProperty(root, "expiresAt");
            if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(access))
            {
                return false;
            }

            var expiresAt = DateTime.MinValue;
            if (!string.IsNullOrWhiteSpace(expiresRaw))
            {
                DateTime.TryParse(expiresRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out expiresAt);
            }

            credential = new DropboxOAuthCredential(appKey, access, refresh, expiresAt);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Detects Dropbox expired/revoked access-token failures.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="body">The response body.</param>
    /// <returns>True when the failure indicates an expired or invalid token.</returns>
    public static bool IsExpiredTokenError(HttpStatusCode statusCode, string? body)
    {
        return statusCode == HttpStatusCode.Unauthorized &&
            !string.IsNullOrEmpty(body) &&
            (body.Contains(ExpiredAccessTokenTag, StringComparison.OrdinalIgnoreCase) ||
                body.Contains("invalid_access_token", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Starts a loopback listener on the fixed OAuth redirect port.
    /// The caller owns the listener and must dispose it.
    /// </summary>
    /// <returns>The listener and its redirect URI.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5332:Using http protocol is insecure", Justification = "OAuth 2.0 desktop loopback redirects require plain http; traffic never leaves the machine.")]
    public static (HttpListener Listener, string RedirectUri) StartLoopbackListener()
    {
        var listener = new HttpListener();
        var redirectUri = HostingConstants.DropboxOAuthRedirectUri;
        listener.Prefixes.Add(redirectUri);
        listener.Prefixes.Add($"http://{HostingConstants.OAuthLoopbackIpv4Host}:{HostingConstants.DropboxOAuthLoopbackPort}/");
        try
        {
            listener.Start();
        }
        catch (Exception ex) when (ex is HttpListenerException or SocketException)
        {
            listener.Close();
            throw new InvalidOperationException(
                $"GenHub could not listen on {redirectUri} because port {HostingConstants.DropboxOAuthLoopbackPort} is already in use. Close the app using that port and try again.",
                ex);
        }

        return (listener, redirectUri);
    }

    /// <summary>
    /// Waits for the browser to redirect back with an authorization code.
    /// </summary>
    /// <param name="listener">The running loopback listener.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization code, or a failure describing the outcome.</returns>
    public async Task<OperationResult<string>> WaitForAuthorizationCodeAsync(
        HttpListener listener,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(HostingConstants.BrowserAuthTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            var context = await listener.GetContextAsync().WaitAsync(linkedCts.Token);
            await RespondToBrowserAsync(context.Response, linkedCts.Token);
            if (context.Request.Url == null)
            {
                return OperationResult<string>.CreateFailure("Dropbox sign-in returned an empty response. Please try again.");
            }

            var (code, error) = ExtractAuthorizationCode(context.Request.Url);
            if (!string.IsNullOrEmpty(code))
            {
                return OperationResult<string>.CreateSuccess(code);
            }

            var detail = string.IsNullOrWhiteSpace(error) ? "Authorization was denied or no code was returned." : $"Dropbox authorization failed: {error}.";
            return OperationResult<string>.CreateFailure(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return OperationResult<string>.CreateFailure("Dropbox sign-in timed out waiting for the browser. Please try again.");
        }
        catch (HttpListenerException ex)
        {
            logger.LogWarning(ex, "Dropbox OAuth loopback listener failed");
            return OperationResult<string>.CreateFailure("Could not listen for the Dropbox sign-in response on this machine.");
        }
    }

    /// <summary>
    /// Exchanges an authorization code for access and refresh tokens (PKCE, no client secret).
    /// </summary>
    /// <param name="appKey">The Dropbox application key.</param>
    /// <param name="code">The authorization code.</param>
    /// <param name="verifier">The PKCE code verifier.</param>
    /// <param name="redirectUri">The redirect URI used in the authorize step.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token result.</returns>
    public async Task<OperationResult<DropboxTokenResult>> ExchangeCodeForTokensAsync(
        string appKey,
        string code,
        string verifier,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(new[]
        {
            new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "authorization_code"),
            new System.Collections.Generic.KeyValuePair<string, string>("code", code),
            new System.Collections.Generic.KeyValuePair<string, string>("client_id", appKey),
            new System.Collections.Generic.KeyValuePair<string, string>("redirect_uri", redirectUri),
            new System.Collections.Generic.KeyValuePair<string, string>("code_verifier", verifier),
        });
        return await RequestTokensAsync(form, "authorization code exchange", cancellationToken);
    }

    /// <summary>
    /// Refreshes a short-lived access token using a stored refresh token.
    /// </summary>
    /// <param name="appKey">The Dropbox application key.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new token result (refresh token may be rotated).</returns>
    public async Task<OperationResult<DropboxTokenResult>> RefreshAccessTokenAsync(
        string appKey,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var form = new FormUrlEncodedContent(new[]
        {
            new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "refresh_token"),
            new System.Collections.Generic.KeyValuePair<string, string>("refresh_token", refreshToken),
            new System.Collections.Generic.KeyValuePair<string, string>("client_id", appKey),
        });
        return await RequestTokensAsync(form, "token refresh", cancellationToken);
    }

    /// <summary>
    /// Opens a URL in the system browser for interactive OAuth consent.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    /// <returns>True when the browser launch was accepted.</returns>
    public bool TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogWarning(ex, "Failed to open browser for Dropbox sign-in");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Failed to open browser for Dropbox sign-in");
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string? GetStringProperty(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static async Task RespondToBrowserAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        const string page = "<html><body style=\"font-family:sans-serif;text-align:center;padding-top:60px\">" +
            "<h2>GenHub connected to Dropbox</h2><p>You can close this tab and return to GenHub.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(page);
        response.ContentType = "text/html";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private async Task<OperationResult<DropboxTokenResult>> RequestTokensAsync(
        FormUrlEncodedContent form,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(HostingConstants.DropboxOAuthTokenUrl, form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Dropbox {Operation} failed: {Status} {Body}", operation, response.StatusCode, body);
                return OperationResult<DropboxTokenResult>.CreateFailure(
                    $"Dropbox sign-in failed during {operation} ({response.StatusCode}). Please try connecting again.");
            }

            return ParseTokenResponse(body, operation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Dropbox {Operation} request failed", operation);
            return OperationResult<DropboxTokenResult>.CreateFailure($"Could not reach Dropbox during {operation}. Check your connection and try again.");
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Dropbox {Operation} timed out", operation);
            return OperationResult<DropboxTokenResult>.CreateFailure($"Dropbox did not respond during {operation}. Please try again.");
        }
    }

    private OperationResult<DropboxTokenResult> ParseTokenResponse(string body, string operation)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var accessToken = GetStringProperty(root, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return OperationResult<DropboxTokenResult>.CreateFailure($"Dropbox did not return an access token during {operation}.");
            }

            var refreshToken = GetStringProperty(root, "refresh_token");
            var expiresIn = root.TryGetProperty("expires_in", out var expiresElement) && expiresElement.TryGetInt64(out var seconds)
                ? seconds
                : 14400L;
            return OperationResult<DropboxTokenResult>.CreateSuccess(new DropboxTokenResult(accessToken, refreshToken, expiresIn));
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Dropbox {Operation} returned an unreadable response", operation);
            return OperationResult<DropboxTokenResult>.CreateFailure($"Dropbox returned an unreadable response during {operation}.");
        }
    }
}
