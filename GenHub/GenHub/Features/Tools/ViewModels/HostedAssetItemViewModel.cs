using System;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Helpers;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel representing an asset hosted on a cloud provider or linked via external CDN.
/// </summary>
public partial class HostedAssetItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _location = string.Empty;

    [ObservableProperty]
    private long _fileSize;

    [ObservableProperty]
    private string _fileSizeFormatted = "0 B";

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private bool _isExternalCdn;

    [ObservableProperty]
    private DateTime _lastUpdated;

    [ObservableProperty]
    private string _lastUpdatedFormatted = string.Empty;

    [ObservableProperty]
    private string? _sha256;

    /// <summary>
    /// Updates the formatted file size whenever <see cref="FileSize"/> changes.
    /// </summary>
    partial void OnFileSizeChanged(long value)
    {
        FileSizeFormatted = FileSizeFormatter.Format(value);
    }

    /// <summary>
    /// Updates the formatted date whenever <see cref="LastUpdated"/> changes.
    /// </summary>
    partial void OnLastUpdatedChanged(DateTime value)
    {
        LastUpdatedFormatted = value == DateTime.MinValue
            ? "Never"
            : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
}
