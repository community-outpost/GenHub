namespace GenHub.Core.Models.Tools.UploadThing;

/// <summary>
/// Request to delete a cloud upload using a cryptographic deletion token.
/// </summary>
/// <param name="FileKey">Unique file key in cloud storage.</param>
/// <param name="DeleteToken">Cryptographic HMAC deletion receipt.</param>
public sealed record DeleteUploadRequest(string FileKey, string DeleteToken);
