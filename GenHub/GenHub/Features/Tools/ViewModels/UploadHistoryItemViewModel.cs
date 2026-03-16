using System;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Common;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for a single upload history item.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="UploadHistoryItemViewModel"/> class.
/// </remarks>
/// <param name="item">The upload history item.</param>
public partial class UploadHistoryItemViewModel(UploadHistoryItem item) : ObservableObject
{
    /// <summary>
    /// Gets the filename.
    /// </summary>
    public string FileName => item.FileName;

    /// <summary>
    /// Gets the URL.
    /// </summary>
    public string Url => item.Url;

    /// <summary>
    /// Gets the formatted timestamp display.
    /// </summary>
    public string TimestampDisplay => GetTimeAgo(item.Timestamp);

    /// <summary>
    /// Gets the formatted size display.
    /// </summary>
    public string SizeDisplay => FormatSize(item.SizeBytes);

    /// <summary>
    /// Gets or sets a value indicating whether the file existence has been verified.
    /// </summary>
    [ObservableProperty]
    private bool isVerified;

    /// <summary>
    /// Gets or sets a value indicating whether the file exists in storage.
    /// </summary>
    [ObservableProperty]
    private bool fileExists;

    /// <summary>
    /// Gets a value indicating whether the upload is still active (file exists in storage).
    /// </summary>
    public bool IsActive => IsVerified ? FileExists : (DateTime.UtcNow - item.Timestamp).TotalDays < 14;

    /// <summary>
    /// Gets the status color based on activity.
    /// </summary>
    public string StatusColor => IsActive ? "#4CAF50" : "#888888";

    private static string GetTimeAgo(DateTime timestamp)
    {
        var span = DateTime.UtcNow - timestamp;
        if (span.TotalDays > 1)
        {
            return $"{(int)span.TotalDays}d ago";
        }

        if (span.TotalHours > 1)
        {
            return $"{(int)span.TotalHours}h ago";
        }

        if (span.TotalMinutes > 1)
        {
            return $"{(int)span.TotalMinutes}m ago";
        }

        return "Just now";
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    partial void OnFileExistsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(StatusColor));
    }

    partial void OnIsVerifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(StatusColor));
    }
}
