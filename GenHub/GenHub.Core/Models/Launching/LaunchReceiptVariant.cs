namespace GenHub.Core.Models.Launching;

/// <summary>
/// The resolved variant and entry-point identity that determined what a launch started:
/// the same <c>ManifestVariantResolver</c> resolution workspace preparation applies to the
/// game client manifest, re-run against the same manifest and host runtime at receipt time.
/// </summary>
public class LaunchReceiptVariant
{
    /// <summary>Gets or sets the game client manifest the resolution ran against.</summary>
    public string GameClientManifestId { get; set; } = string.Empty;

    /// <summary>Gets or sets the host runtime identifier the resolution ran on, for example <c>osx-arm64</c>.</summary>
    public string RuntimeIdentifier { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the manifest declares variants at all.</summary>
    public bool HasVariants { get; set; }

    /// <summary>
    /// Gets or sets the runtime identifiers of the variant that matched; empty when the
    /// matched variant is platform-neutral or the manifest declares no variants.
    /// </summary>
    public List<string> VariantRuntimeIdentifiers { get; set; } = [];

    /// <summary>Gets or sets the resolved entry point, relative to the workspace, when resolution succeeded.</summary>
    public string? EntryPointRelativePath { get; set; }

    /// <summary>Gets or sets the resolver's stated reason for the outcome.</summary>
    public string? Resolution { get; set; }
}
