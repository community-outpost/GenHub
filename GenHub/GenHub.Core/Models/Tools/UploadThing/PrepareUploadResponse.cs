namespace GenHub.Core.Models.Tools.UploadThing;

/// <summary>
/// Gateway response containing presigned upload instructions and cryptographic deletion token.
/// </summary>
/// <param name="UploadUrl">Presigned storage PUT URL.</param>
/// <param name="FileKey">Unique file key in the cloud storage bucket.</param>
/// <param name="DeleteToken">Cryptographic HMAC deletion receipt.</param>
/// <param name="PublicUrl">Publicly accessible share URL.</param>
public sealed record PrepareUploadResponse(
    string? UploadUrl,
    string? FileKey,
    string? DeleteToken,
    string? PublicUrl);
