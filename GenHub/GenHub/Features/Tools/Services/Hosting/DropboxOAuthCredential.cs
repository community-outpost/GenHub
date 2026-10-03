using System;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Persisted Dropbox OAuth credential set.
/// </summary>
/// <param name="AppKey">The Dropbox application key.</param>
/// <param name="AccessToken">The current short-lived access token.</param>
/// <param name="RefreshToken">The long-lived refresh token.</param>
/// <param name="ExpiresAtUtc">When the access token expires (UTC).</param>
public sealed record DropboxOAuthCredential(string AppKey, string AccessToken, string? RefreshToken, DateTime ExpiresAtUtc);
