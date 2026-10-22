namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Message sent when a manifest ID has been replaced by a new one globally.
/// Any services or ViewModels holding onto the old ID should update to the new one.
/// </summary>
/// <param name="OldId">The original manifest ID.</param>
/// <param name="NewId">The replacement manifest ID.</param>
public record ManifestReplacedMessage(string OldId, string NewId);
