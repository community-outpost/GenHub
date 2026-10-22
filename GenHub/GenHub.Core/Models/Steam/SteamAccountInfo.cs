namespace GenHub.Core.Models.Steam;

/// <summary>
/// Represents authentication information for a connected Steam account.
/// </summary>
/// <param name="AccountName">The Steam account username.</param>
/// <param name="SteamId">The optional 64-bit Steam ID (reserved for future identity linkage).</param>
/// <param name="RefreshToken">The persistent OAuth/SteamKit refresh token.</param>
public sealed record SteamAccountInfo(
    string AccountName,
    ulong SteamId,
    string RefreshToken);
