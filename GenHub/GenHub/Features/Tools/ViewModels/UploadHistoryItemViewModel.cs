using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Infrastructure.Converters;
using System;
using System.ComponentModel;
using System.Globalization;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for a single upload history item.
/// </summary>
public sealed partial class UploadHistoryItemViewModel : ObservableObject, IDisposable
{
    private readonly UploadHistoryItem _item;
    private readonly ILocalizationService? _localizationService;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UploadHistoryItemViewModel"/> class.
    /// </summary>
    /// <param name="item">The upload history item.</param>
    /// <param name="localizationService">Optional localization service.</param>
    public UploadHistoryItemViewModel(UploadHistoryItem item, ILocalizationService? localizationService = null)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        _localizationService = localizationService ?? LocalizationConverterHelper.ResolveLocalizationService();
        if (_localizationService != null)
        {
            _localizationService.PropertyChanged += OnLocalizationPropertyChanged;
        }
    }

    /// <summary>
    /// Gets the filename.
    /// </summary>
    public string FileName => _item.FileName;

    /// <summary>
    /// Gets the URL.
    /// </summary>
    public string Url => _item.Url;

    /// <summary>
    /// Gets the game selected when the upload began, if recorded.
    /// </summary>
    public GameType? Game => _item.Game;

    /// <summary>
    /// Gets the formatted timestamp display.
    /// </summary>
    public string TimestampDisplay => GetTimeAgo(_item.Timestamp, _localizationService);

    /// <summary>
    /// Gets the formatted size display.
    /// </summary>
    public string SizeDisplay => FormatSize(_item.SizeBytes);

    /// <summary>
    /// Gets a value indicating whether the upload is still active (file exists in storage).
    /// </summary>
    public bool IsActive => IsVerified ? FileExists : (DateTime.UtcNow - _item.Timestamp).TotalDays < 14;

    /// <summary>
    /// Gets the status color based on activity.
    /// </summary>
    public string StatusColor => IsActive ? UiConstants.StatusSuccessColor : UiConstants.StatusErrorColor;

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
    /// Disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_localizationService != null)
        {
            _localizationService.PropertyChanged -= OnLocalizationPropertyChanged;
        }

        _disposed = true;
    }

    private static string GetTimeAgo(DateTime timestamp, ILocalizationService? localizationService = null)
    {
        localizationService ??= LocalizationConverterHelper.ResolveLocalizationService();
        var span = DateTime.UtcNow - timestamp;
        if (span.TotalDays >= 1)
        {
            var days = (int)span.TotalDays;
            return localizationService != null
                ? string.Format(CultureInfo.CurrentCulture, localizationService.GetString("Common.Time.DaysAgo") ?? "{0}d ago", days)
                : $"{days}d ago";
        }

        if (span.TotalHours >= 1)
        {
            var hours = (int)span.TotalHours;
            return localizationService != null
                ? string.Format(CultureInfo.CurrentCulture, localizationService.GetString("Common.Time.HoursAgo") ?? "{0}h ago", hours)
                : $"{hours}h ago";
        }

        if (span.TotalMinutes >= 1)
        {
            var minutes = (int)span.TotalMinutes;
            return localizationService != null
                ? string.Format(CultureInfo.CurrentCulture, localizationService.GetString("Common.Time.MinutesAgo") ?? "{0}m ago", minutes)
                : $"{minutes}m ago";
        }

        return localizationService?.GetString("Common.Time.JustNow") ?? "Just now";
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

        return $"{len:0.#} {sizes[order]}";
    }

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TimestampDisplay));
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
