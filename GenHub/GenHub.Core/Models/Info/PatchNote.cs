using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Core.Models.Info;

/// <summary>
/// Represents a single patch note entry.
/// </summary>
public partial class PatchNote : ObservableObject
{
    /// <summary>Gets or sets the unique identifier for the patch note.</summary>
    [ObservableProperty]
    private string _id = string.Empty;

    /// <summary>Gets or sets the title of the patch note.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Gets or sets the date of the patch note.</summary>
    [ObservableProperty]
    private string _date = string.Empty;

    /// <summary>Gets or sets the summary of the patch note.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Gets or sets the URL to the detailed patch note.</summary>
    [ObservableProperty]
    private string _detailsUrl = string.Empty;

    /// <summary>Gets or sets the list of specific changes in this patch.</summary>
    public ObservableCollection<string> Changes { get; set; } = [];

    [ObservableProperty]
    private bool _isDetailsLoaded;

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    private bool _isExpanded;
}
