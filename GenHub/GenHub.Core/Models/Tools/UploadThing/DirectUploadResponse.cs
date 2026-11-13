namespace GenHub.Core.Models.Tools.UploadThing;

/// <summary>
/// Gateway response containing upload result and cryptographic deletion token.
/// </summary>
/// <param name="PublicUrl">Publicly accessible share URL.</param>
/// <param name="FileKey">Unique file key in cloud storage.</param>
/// <param name="DeleteToken">Cryptographic HMAC deletion receipt.</param>
public sealed record DirectUploadResponse(
    string? PublicUrl,
    string? FileKey,
    string? DeleteToken);
