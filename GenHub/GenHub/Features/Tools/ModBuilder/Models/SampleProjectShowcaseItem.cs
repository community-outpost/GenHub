namespace GenHub.Features.Tools.ModBuilder.Models;

/// <summary>
/// Represents a publisher sample project featured in the ModBuilder dashboard showcase.
/// </summary>
public sealed class SampleProjectShowcaseItem
{
    /// <summary>
    /// Gets the unique identifier/directory name of the sample project template.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display title of the sample project.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the publisher or author credit.
    /// </summary>
    public required string Publisher { get; init; }

    /// <summary>
    /// Gets the description of what this sample project builds and demonstrates.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the target game (e.g., "Generals", "Zero Hour").
    /// </summary>
    public required string TargetGame { get; init; }

    /// <summary>
    /// Gets the release BIG archive filename produced by building this project.
    /// </summary>
    public required string OutputFileName { get; init; }

    /// <summary>
    /// Gets the badge category or tag (e.g. "Balance &amp; Bugfix", "Widescreen UI", "Control Bar", "Competitive Hotkeys").
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Gets a summary of available variants or bundle packs (e.g. "3 Language Variants (EN, RU, ES)").
    /// </summary>
    public string VariantSummary { get; init; } = string.Empty;

    /// <summary>
    /// Gets the count of bundle pack variants in this project.
    /// </summary>
    public int VariantCount { get; init; } = 1;

    /// <summary>
    /// Gets the expected SHA-256 hash of the publisher's binary for internal integrity checks.
    /// </summary>
    public string ExpectedSha256 { get; init; } = string.Empty;
}
