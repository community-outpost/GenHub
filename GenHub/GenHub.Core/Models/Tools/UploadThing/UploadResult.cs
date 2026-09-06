namespace GenHub.Core.Models.Tools.UploadThing;

/// <summary>
/// Result of a successful cloud upload operation.
/// </summary>
/// <param name="PublicUrl">Public share URL for the uploaded file.</param>
/// <param name="FileKey">Unique file key in cloud storage.</param>
/// <param name="DeleteToken">Cryptographic HMAC deletion receipt.</param>
public sealed record UploadResult(string PublicUrl, string FileKey, string DeleteToken);
