namespace GenHub.Core.Models.Theming;

/// <summary>
/// Represents a selectable color theme for application accents.
/// </summary>
public sealed record ColorTheme
{
    /// <summary>
    /// Gets the unique identifier of the theme.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the human-readable display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the primary accent hex color.
    /// </summary>
    public required string PrimaryHex { get; init; }

    /// <summary>
    /// Gets the lighter accent variant hex color for gradients and highlights.
    /// </summary>
    public required string LightHex { get; init; }

    /// <summary>
    /// Gets the darker accent variant hex color for depth and gradients.
    /// </summary>
    public required string DarkHex { get; init; }

    /// <summary>
    /// Gets the translucent glow hex color for ambient lighting effects.
    /// </summary>
    public required string GlowHex { get; init; }
}
