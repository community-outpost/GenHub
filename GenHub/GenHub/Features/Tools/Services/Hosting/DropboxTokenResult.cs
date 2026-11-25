namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Result of a Dropbox OAuth token request (authorization-code exchange or refresh).
/// </summary>
/// <param name="AccessToken">The short-lived access token.</param>
/// <param name="RefreshToken">The refresh token, when issued or rotated.</param>
/// <param name="ExpiresInSeconds">Lifetime of the access token in seconds.</param>
public sealed record DropboxTokenResult(string AccessToken, string? RefreshToken, long ExpiresInSeconds);
