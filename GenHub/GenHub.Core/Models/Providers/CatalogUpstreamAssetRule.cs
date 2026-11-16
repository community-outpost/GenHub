using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Defines an asset pattern rule for mapping upstream release artifacts to variant axes.
/// </summary>
public class CatalogUpstreamAssetRule
{
    /// <summary>
    /// Gets or sets the regex or glob pattern matching the asset filename.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the variant label assigned to matching assets.
    /// </summary>
    [JsonPropertyName("variant")]
    public string Variant { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this variant is the default variant.
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets the target game for matching assets. Defaults to <see cref="GameType.Unknown"/> (wildcard / match any game).
    /// </summary>
    [JsonPropertyName("targetGame")]
    public GameType TargetGame { get; set; } = GameType.Unknown;

    /// <summary>
    /// Gets or sets the platform the matching assets run on: an OS family (<c>windows</c>,
    /// <c>macos</c>, <c>linux</c>) or runtime OS name (<c>win</c>, <c>osx</c>), optionally
    /// followed by an architecture (<c>win-x64</c>, <c>osx-arm64</c>, <c>macos-arm64</c>).
    /// A numeric OS version (<c>osx.13-arm64</c>) and a Linux qualifier (<c>linux-musl-x64</c>)
    /// are accepted but not compared with the host. See <see cref="RuntimePlatformMatcher"/>.
    /// <c>null</c> matches every host.
    /// </summary>
    [JsonPropertyName("platform")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Platform { get; set; }

    /// <summary>
    /// Gets a value indicating whether this rule is limited to a platform.
    /// </summary>
    [JsonIgnore]
    public bool IsPlatformSpecific => !string.IsNullOrWhiteSpace(Platform);

    /// <summary>
    /// Determines whether assets matched by this rule run on the given host.
    /// </summary>
    /// <param name="runtimeIdentifier">The host runtime identifier, for example <c>osx-arm64</c>.</param>
    /// <returns><c>true</c> when the rule declares no platform or its platform matches the host.</returns>
    public bool SupportsRuntime(string runtimeIdentifier) => RuntimePlatformMatcher.Matches(Platform, runtimeIdentifier);
}
