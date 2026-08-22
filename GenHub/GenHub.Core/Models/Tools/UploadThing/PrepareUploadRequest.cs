namespace GenHub.Core.Models.Tools.UploadThing;

/// <summary>
/// Request to prepare a cloud upload with the gateway.
/// </summary>
/// <param name="FileName">Name of the file being uploaded.</param>
/// <param name="FileSize">Size of the file in bytes.</param>
/// <param name="ContentType">MIME content type (e.g. application/zip).</param>
public sealed record PrepareUploadRequest(string FileName, long FileSize, string ContentType);
