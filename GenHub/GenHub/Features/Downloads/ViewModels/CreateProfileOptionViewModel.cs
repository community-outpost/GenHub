namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents the create-profile card displayed alongside profile options.
/// </summary>
public sealed class CreateProfileOptionViewModel : ProfilePickerItemViewModel
{
    /// <summary>
    /// Gets the title for the create-profile card.
    /// </summary>
    public string Title => "Create new profile";

    /// <summary>
    /// Gets the description for the create-profile card.
    /// </summary>
    public string Subtitle => "Add this content to a fresh profile";
}
