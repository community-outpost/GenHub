using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Core.Models.Info;

/// <summary>
/// Represents a single patch note entry.
/// </summary>
public partial class PatchNote : ObservableObject
{
    /// <summary>Gets or sets the unique identifier for the patch note.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the title of the patch note.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the date of the patch note.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Gets or sets the summary of the patch note.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Gets or sets the URL to the detailed patch note.</summary>
    public string DetailsUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of specific changes in this patch.</summary>
    public List<string> Changes { get; set; } = [];

    [ObservableProperty]
    private bool _isDetailsLoaded;

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    private bool _isExpanded;
}
