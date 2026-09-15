namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents the create-profile card displayed alongside profile options.
/// </summary>
public sealed class CreateProfileOptionViewModel : ProfilePickerItemViewModel
{
    /// <summary>
    /// Title text for the create-profile card.
    /// </summary>
    public const string DefaultTitle = "Create new profile";

    /// <summary>
    /// Subtitle description for the create-profile card.
    /// </summary>
    public const string DefaultSubtitle = "Add this content to a fresh profile";

    /// <summary>
    /// Gets the title for the create-profile card.
    /// </summary>
    public static string Title => DefaultTitle;

    /// <summary>
    /// Gets the description for the create-profile card.
    /// </summary>
    public static string Subtitle => DefaultSubtitle;
}
