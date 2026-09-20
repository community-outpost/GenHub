using GenHub.Core.Constants;
using GenHub.Features.Tools.Services.Hosting;
using System;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="DropboxOAuthService"/> pure helpers.
/// </summary>
public sealed class DropboxOAuthServiceTests
{
    /// <summary>
    /// The authorize URL must carry the fixed redirect URI, offline access (refresh tokens),
    /// and the exact permission scopes the setup guide tells users to enable.
    /// </summary>
    [Fact]
    public void BuildAuthorizeUrl_IncludesRedirectScopeAndOfflineAccess()
    {
        var url = DropboxOAuthService.BuildAuthorizeUrl("test-app-key", "test-challenge", HostingConstants.DropboxOAuthRedirectUri);

        Assert.StartsWith(HostingConstants.DropboxOAuthAuthorizeUrl, url, StringComparison.Ordinal);
        Assert.Contains("response_type=code", url, StringComparison.Ordinal);
        Assert.Contains("client_id=test-app-key", url, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(HostingConstants.DropboxOAuthRedirectUri), url, StringComparison.Ordinal);
        Assert.Contains("code_challenge=test-challenge", url, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", url, StringComparison.Ordinal);
        Assert.Contains("token_access_type=offline", url, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(HostingConstants.DropboxOAuthScopes), url, StringComparison.Ordinal);
    }

    /// <summary>
    /// The redirect URI must be a fixed loopback URI so users can pre-register it exactly;
    /// Dropbox rejects ephemeral ports with "Invalid redirect_uri".
    /// </summary>
    [Fact]
    public void DropboxOAuthRedirectUri_IsFixedLoopbackUri()
    {
        var redirect = new Uri(HostingConstants.DropboxOAuthRedirectUri);

        Assert.Equal(Uri.UriSchemeHttp, redirect.Scheme);
        Assert.Equal(HostingConstants.OAuthLoopbackHost, redirect.Host);
        Assert.Equal(HostingConstants.DropboxOAuthLoopbackPort, redirect.Port);
        Assert.EndsWith("/", HostingConstants.DropboxOAuthRedirectUri, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requested scopes must stay in sync with the Dropbox endpoints GenHub calls.
    /// </summary>
    /// <param name="scope">The OAuth permission scope to verify.</param>
    [Theory]
    [InlineData("account_info.read")]
    [InlineData("files.metadata.read")]
    [InlineData("files.metadata.write")]
    [InlineData("files.content.write")]
    [InlineData("sharing.read")]
    [InlineData("sharing.write")]
    public void DropboxOAuthScopes_ContainsRequiredPermission(string scope)
    {
        Assert.Contains(scope, HostingConstants.DropboxOAuthScopes.Split(' '));
    }

    /// <summary>
    /// Authorization codes are extracted from the loopback callback query string.
    /// </summary>
    [Fact]
    public void ExtractAuthorizationCode_ParsesCodeAndError()
    {
        var (code, error) = DropboxOAuthService.ExtractAuthorizationCode(new Uri("http://localhost:51239/?code=auth-code-123"));

        Assert.Equal("auth-code-123", code);
        Assert.Null(error);

        var (deniedCode, deniedError) = DropboxOAuthService.ExtractAuthorizationCode(new Uri("http://localhost:51239/?error=access_denied"));

        Assert.Null(deniedCode);
        Assert.Equal("access_denied", deniedError);
    }

    /// <summary>
    /// Stored credential payloads round-trip so refresh tokens survive restarts.
    /// </summary>
    [Fact]
    public void CredentialPayload_RoundTripsThroughStorage()
    {
        var expiresAt = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var payload = DropboxOAuthService.SerializeCredential(new DropboxOAuthCredential("app-key", "access-token", "refresh-token", expiresAt));

        var parsed = DropboxOAuthService.TryParseCredential(payload, out var credential);

        Assert.True(parsed);
        Assert.NotNull(credential);
        Assert.Equal("app-key", credential.AppKey);
        Assert.Equal("access-token", credential.AccessToken);
        Assert.Equal("refresh-token", credential.RefreshToken);
        Assert.Equal(expiresAt, credential.ExpiresAtUtc);
    }

    /// <summary>
    /// Legacy plain access tokens are not mistaken for credential payloads.
    /// </summary>
    [Fact]
    public void TryParseCredential_RejectsLegacyToken()
    {
        var parsed = DropboxOAuthService.TryParseCredential("sl.legacy-token", out var credential);

        Assert.False(parsed);
        Assert.Null(credential);
    }
}
