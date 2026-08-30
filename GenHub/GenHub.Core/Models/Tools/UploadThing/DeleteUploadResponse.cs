namespace GenHub.Core.Models.Tools.UploadThing;

/// <summary>
/// Response from the gateway after requesting deletion.
/// </summary>
/// <param name="Success">Whether the deletion succeeded upstream.</param>
public sealed record DeleteUploadResponse(bool Success);
