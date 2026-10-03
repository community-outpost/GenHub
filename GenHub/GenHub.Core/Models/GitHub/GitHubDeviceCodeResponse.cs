namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents an in-progress GitHub OAuth device authorization flow (RFC 8628).
/// Display <see cref="UserCode"/> and <see cref="VerificationUri"/> to the user and poll
/// with <see cref="DeviceCode"/> until the user approves or denies the request.
/// </summary>
/// <param name="DeviceCode">The device verification code used when polling for completion. Treat as a secret and never log it.</param>
/// <param name="UserCode">The short code the user types at the verification URI (for example, WDAS-5678).</param>
/// <param name="VerificationUri">The URI where the user enters the user code.</param>
/// <param name="ExpiresInSeconds">How long the device code stays valid, in seconds.</param>
/// <param name="PollingIntervalSeconds">Minimum delay between authorization polls, in seconds.</param>
public sealed record GitHubDeviceCodeResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int PollingIntervalSeconds);
