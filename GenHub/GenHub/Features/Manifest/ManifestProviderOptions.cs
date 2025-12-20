namespace GenHub.Features.Manifest;

/// <summary>
/// Options to control <see cref="ManifestProvider"/> behaviour.
/// </summary>
public sealed class ManifestProviderOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether fallback manifests should be generated when none are found in CAS or embedded resources.
    /// </summary>
    public bool GenerateFallbackManifests { get; set; } = false;
}
