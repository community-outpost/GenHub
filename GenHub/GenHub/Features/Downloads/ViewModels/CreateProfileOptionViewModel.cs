using System.Diagnostics.CodeAnalysis;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Represents the create-profile card displayed alongside profile options.
/// Display text is provided by localized resources in the view.
/// </summary>
[SuppressMessage("Major Code Smell", "S2094:Classes should not be empty", Justification = "Marker class for Avalonia DataTemplate resolution.")]
public sealed class CreateProfileOptionViewModel : ProfilePickerItemViewModel;
