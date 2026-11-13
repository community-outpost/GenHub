using GenHub.Core.Helpers;

namespace GenHub.Core.Models.Content;

/// <summary>
/// A display-ready publisher card shown inside a custom content detail tab.
/// </summary>
public class CustomTabCardDefinition
{
    /// <summary>Gets or sets the card heading.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets supporting text for the card.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional local or remote image URL.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Gets a value indicating whether the image is a remote HTTP or HTTPS link that a browser can open.</summary>
    public bool HasRemoteImage => MediaFileHelper.IsRemoteHttpUrl(ImageUrl);

    /// <summary>Gets or sets an optional compact label displayed above the card title.</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets the card accent colour in a format understood by Avalonia.</summary>
    public string AccentColor { get; set; } = "#303D59";
}
