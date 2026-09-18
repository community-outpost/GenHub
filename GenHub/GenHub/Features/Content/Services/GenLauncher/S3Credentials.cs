namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Credentials for S3 authentication.
/// </summary>
/// <param name="PublicKey">The public or access key.</param>
/// <param name="SecretKey">The secret key.</param>
public sealed record S3Credentials(string? PublicKey = null, string? SecretKey = null);
