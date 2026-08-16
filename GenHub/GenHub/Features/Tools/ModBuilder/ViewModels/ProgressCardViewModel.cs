using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for individual progress cards.
/// </summary>
public partial class ProgressCardViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the card title.
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Gets or sets the icon key.
    /// </summary>
    [ObservableProperty]
    private string _icon = string.Empty;

    /// <summary>
    /// Gets or sets the status (Pending, InProgress, Completed).
    /// </summary>
    [ObservableProperty]
    private string _status = "Pending";

    /// <summary>
    /// Gets or sets the progress (0-100).
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    private string _message = string.Empty;
}
