using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// A platform-specific build within a single content release.
/// <para>
/// One release of a game client can produce several builds — win-x64, linux-x64 and
/// osx-arm64 — that share a version and a name but not a file list or an entry point.
/// A single flat file list cannot describe that: the same manifest would advertise a
/// Windows executable to a macOS host, which installs cleanly and then cannot run.
/// </para>
/// <para>
/// Variants are optional. A manifest with no variants is a single unconstrained build
/// described by <see cref="ContentManifest.Files"/>, which is what every manifest
/// written before this type existed looks like.
/// </para>
/// </summary>
public class ArtifactVariant
{
    /// <summary>
    /// Gets or sets the runtime identifiers this variant can run on, for example
    /// <c>osx-arm64</c> or <c>win-x64</c>.
    /// <para>
    /// Architecture matters, not just the operating system: an x64 build is not
    /// interchangeable with an arm64 one, and a macOS user on Apple Silicon offered an
    /// <c>osx-x64</c> build gets a launch that fails in the loader.
    /// </para>
    /// <para>
    /// An empty list means the variant is platform-neutral, which is correct for map
    /// packs, INI tweaks and <c>.big</c> content that contains no native code.
    /// </para>
    /// </summary>
    [JsonPropertyName("runtimeIdentifiers")]
    public List<string> RuntimeIdentifiers { get; set; } = [];

    /// <summary>
    /// Gets or sets the relative path of the file to launch for this variant.
    /// <para>
    /// Declared rather than inferred. Inferring it from file extensions is ambiguous
    /// the moment a variant ships more than one runnable file, and the result then
    /// depends on file enumeration order.
    /// </para>
    /// </summary>
    [JsonPropertyName("entryPoint")]
    public string? EntryPoint { get; set; }

    /// <summary>
    /// Gets or sets the files belonging to this variant.
    /// </summary>
    [JsonPropertyName("files")]
    public List<ManifestFile> Files { get; set; } = [];

    /// <summary>
    /// Determines whether this variant can run on the given runtime identifier.
    /// </summary>
    /// <param name="runtimeIdentifier">The host runtime identifier, for example <c>osx-arm64</c>.</param>
    /// <returns><c>true</c> when the variant is platform-neutral or explicitly targets the runtime.</returns>
    public bool SupportsRuntime(string runtimeIdentifier)
    {
        if (RuntimeIdentifiers.Count == 0)
        {
            return true;
        }

        foreach (var candidate in RuntimeIdentifiers)
        {
            if (string.Equals(candidate, runtimeIdentifier, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
