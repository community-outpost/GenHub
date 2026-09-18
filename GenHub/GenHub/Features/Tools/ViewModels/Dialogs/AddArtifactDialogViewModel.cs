using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add Artifact dialog.
/// Provides validation and creation of new ReleaseArtifact entries.
/// </summary>
[SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI.")]
public partial class AddArtifactDialogViewModel(Action<ReleaseArtifact> onArtifactCreated, GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null) : ObservableValidator, IDisposable
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Filename is required")]
    private string _filename = string.Empty;

    [ObservableProperty]
    private string _downloadUrl = string.Empty;

    [ObservableProperty]
    private long _fileSize;

    [ObservableProperty]
    private string _sha256Hash = string.Empty;

    [ObservableProperty]
    private bool _isPrimary = true;

    [ObservableProperty]
    private bool _useLocalFile = true;

    [ObservableProperty]
    private string? _localFilePath;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private bool _isComputingHash;

    [ObservableProperty]
    private string _fileSizeDisplay = string.Empty;

    [ObservableProperty]
    private string _fileSizeInput = string.Empty;

    private string? _lastAutoUrlFilename;

    private CancellationTokenSource? _hashCts;

    [ObservableProperty]
    private string _artifactStatus = localizationService?.GetString("Tools.PublisherStudio.Artifact.NoFileConfigured") ?? "No file configured";

    /// <summary>
    /// Gets or sets a value indicating whether to use an existing URL instead of uploading a file.
    /// </summary>
    public bool UseExistingUrl
    {
        get => !UseLocalFile;
        set
        {
            if (UseLocalFile == !value) return;
            UseLocalFile = !value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets a value indicating whether a local file has been selected.
    /// </summary>
    public bool IsLocalFile => !string.IsNullOrEmpty(LocalFilePath);

    /// <summary>
    /// Gets a value indicating whether the artifact is hosted remotely (URL set, no local file).
    /// </summary>
    public bool IsHosted => !string.IsNullOrEmpty(DownloadUrl) && !IsLocalFile;

    /// <summary>
    /// Attempts to parse a human-readable file size string (e.g. 500 MB, 1.2 GB, or raw bytes).
    /// </summary>
    /// <param name="input">The size string to parse.</param>
    /// <param name="bytes">The resulting size in bytes.</param>
    /// <returns>True if parsing succeeded; otherwise, false.</returns>
    public static bool TryParseFileSize(string? input, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (long.TryParse(trimmed, out var directBytes) && directBytes >= 0)
        {
            bytes = directBytes;
            return true;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"^([\d\.]+)\s*([KkMmGgTt]?[Bb]?)$",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));

        if (!match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || number < 0)
        {
            return false;
        }

        var unit = match.Groups[2].Value.ToUpperInvariant();
        long multiplier = unit switch
        {
            "KB" or "K" => 1024L,
            "MB" or "M" => 1024L * 1024,
            "GB" or "G" => 1024L * 1024 * 1024,
            "TB" or "T" => 1024L * 1024 * 1024 * 1024,
            _ => 1L,
        };

        bytes = (long)(number * multiplier);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hashCts?.Cancel();
            _hashCts?.Dispose();
            _hashCts = null;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:0.##} {suffixes[suffixIndex]}";
    }

    private static string ComputeSha256(string filePath, CancellationToken cancellationToken)
    {
        const int BufferSize = 81920;
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[BufferSize];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        sha256.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
    }

    private void CancelPendingHash()
    {
        _hashCts?.Cancel();
        _hashCts?.Dispose();
        _hashCts = null;
    }

    /// <summary>
    /// Gets the localized parsed file-size hint, or null when no size is parsed.
    /// </summary>
    public string? ParsedSizeDisplay => string.IsNullOrEmpty(FileSizeDisplay)
        ? null
        : string.Format(
            localizationService?.GetString("Tools.PublisherStudio.Artifact.ParsedSize") ?? "Parsed: {0}",
            FileSizeDisplay);

    partial void OnFilenameChanged(string value)
    {
        Validate();
    }

    partial void OnFileSizeDisplayChanged(string value)
    {
        OnPropertyChanged(nameof(ParsedSizeDisplay));
    }

    partial void OnUseLocalFileChanged(bool value)
    {
        if (value)
        {
            DownloadUrl = string.Empty;
        }
        else
        {
            CancelPendingHash();
            LocalFilePath = null;
            FileSize = 0;
            FileSizeDisplay = string.Empty;
            FileSizeInput = string.Empty;
            Sha256Hash = string.Empty;
        }

        OnPropertyChanged(nameof(UseExistingUrl));
        UpdateArtifactStatus();
        Validate();
    }

    partial void OnLocalFilePathChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            CancelPendingHash();
            FileSize = 0;
            FileSizeDisplay = string.Empty;
            FileSizeInput = string.Empty;
            Sha256Hash = string.Empty;
        }

        OnPropertyChanged(nameof(IsLocalFile));
        OnPropertyChanged(nameof(IsHosted));
        UpdateArtifactStatus();
        Validate();
    }

    partial void OnDownloadUrlChanged(string value)
    {
        if (UseExistingUrl && !string.IsNullOrWhiteSpace(value))
        {
            try
            {
                if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
                {
                    var seg = Path.GetFileName(uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(seg) && (string.IsNullOrWhiteSpace(Filename) || Filename == _lastAutoUrlFilename))
                    {
                        Filename = seg;
                        _lastAutoUrlFilename = seg;
                    }
                }
            }
            catch (UriFormatException)
            {
                // Ignore format errors while typing
            }
            catch (ArgumentException)
            {
                // Ignore format errors while typing
            }
        }

        OnPropertyChanged(nameof(IsLocalFile));
        OnPropertyChanged(nameof(IsHosted));
        UpdateArtifactStatus();
        Validate();
    }

    partial void OnFileSizeInputChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            FileSize = 0;
            FileSizeDisplay = string.Empty;
            ValidationError = null;
            return;
        }

        if (TryParseFileSize(value, out var bytes))
        {
            FileSize = bytes;
            FileSizeDisplay = FormatFileSize(bytes);
            ValidationError = null;
        }
        else
        {
            FileSize = 0;
            FileSizeDisplay = GetLocalizedString("Tools.PublisherStudio.Artifact.InvalidSize", "Invalid size");
            ValidationError = GetLocalizedString("Tools.PublisherStudio.Artifact.InvalidSizeFormat", "Invalid file size format (e.g., 10 MB, 500 KB, 1.5 GB)");
        }
    }

    private void UpdateArtifactStatus()
    {
        if (!string.IsNullOrEmpty(LocalFilePath))
        {
            ArtifactStatus = GetLocalizedString(
                "Tools.PublisherStudio.Artifact.StatusLocalFile",
                "Local file selected - will be uploaded during publish");
        }
        else if (!string.IsNullOrEmpty(DownloadUrl))
        {
            ArtifactStatus = GetLocalizedString(
                "Tools.PublisherStudio.Artifact.StatusExternalCdn",
                "Hosted externally on CDN / mirror (will not be uploaded)");
        }
        else
        {
            ArtifactStatus = GetLocalizedString(
                "Tools.PublisherStudio.Artifact.NoFileConfigured",
                "No file configured");
        }
    }

    /// <summary>
    /// Opens a file picker dialog and populates artifact fields from the selected file.
    /// Auto-fills filename, file size, and computes SHA256 hash asynchronously.
    /// </summary>
    [RelayCommand]
    private async Task BrowseLocalFileAsync()
    {
        try
        {
            var lifetime = Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            if (mainWindow == null) return;

            var topLevel = TopLevel.GetTopLevel(mainWindow);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Select Artifact File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Archives") { Patterns = new[] { "*.zip", "*.rar", "*.7z" } },
                        new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                    },
                });

            if (files.Count > 0)
            {
                var file = files[0];
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    LocalFilePath = path;
                    Filename = Path.GetFileName(path);

                    var fileInfo = new FileInfo(path);
                    FileSize = fileInfo.Length;
                    FileSizeDisplay = FormatFileSize(FileSize);
                    FileSizeInput = FileSizeDisplay;

                    // Compute SHA256 in background
                    CancelPendingHash();
                    _hashCts = new CancellationTokenSource();
                    var hashCt = _hashCts.Token;
                    IsComputingHash = true;
                    try
                    {
                        var computedHash = await Task.Run(() => ComputeSha256(path, hashCt), hashCt);
                        if (!hashCt.IsCancellationRequested && LocalFilePath == path)
                        {
                            Sha256Hash = computedHash;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Superseded by a newer selection or dialog close
                    }
                    finally
                    {
                        IsComputingHash = false;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Selection or dialog was canceled
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ArtifactStatus = localizationService?.GetString(
                "Tools.PublisherStudio.Artifact.BrowseError",
                ex.Message) ?? $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Sets the local file and extracts filename and size.
    /// </summary>
    /// <param name="filePath">Path to the local file.</param>
    [RelayCommand]
    private async Task SelectLocalFileAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        LocalFilePath = filePath;
        Filename = Path.GetFileName(filePath);

        var fileInfo = new FileInfo(filePath);
        FileSize = fileInfo.Length;
        FileSizeDisplay = FormatFileSize(FileSize);

        // Auto-compute hash
        await ComputeHashAsync();
    }

    /// <summary>
    /// Computes the SHA256 hash from the local file.
    /// </summary>
    [RelayCommand]
    private async Task ComputeHashAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalFilePath) || !File.Exists(LocalFilePath))
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Artifact.SelectLocalFileFirst",
                "Please select a local file first");
            return;
        }

        var targetPath = LocalFilePath;
        CancelPendingHash();
        _hashCts = new CancellationTokenSource();
        var hashCt = _hashCts.Token;
        IsComputingHash = true;
        ValidationError = null;

        try
        {
            await using var stream = File.OpenRead(targetPath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, hashCt);
            if (!hashCt.IsCancellationRequested && LocalFilePath == targetPath)
            {
                Sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection or dialog close
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!hashCt.IsCancellationRequested && LocalFilePath == targetPath)
            {
                ValidationError = localizationService?.GetString(
                    "Tools.PublisherStudio.Artifact.HashComputeFailed",
                    ex.Message) ?? $"Failed to compute hash: {ex.Message}";
            }
        }
        finally
        {
            IsComputingHash = false;
        }
    }

    /// <summary>
    /// Creates the artifact if validation passes.
    /// </summary>
    [RelayCommand]
    private void CreateArtifact()
    {
        ValidateAllProperties();

        if (HasErrors)
        {
            ValidationError = string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage));
            IsValid = false;
            return;
        }

        // Validate based on selection mode
        if (UseLocalFile)
        {
            // Local file mode - require local file path
            if (string.IsNullOrWhiteSpace(LocalFilePath) || !File.Exists(LocalFilePath))
            {
                ValidationError = GetLocalizedString(
                    "Tools.PublisherStudio.Artifact.LocalFileRequired",
                    "Please select a local file to upload");
                IsValid = false;
                return;
            }
        }
        else
        {
            // URL mode - require valid URL
            if (string.IsNullOrWhiteSpace(DownloadUrl) ||
                !Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ValidationError = GetLocalizedString(
                    "Tools.PublisherStudio.Artifact.ValidUrlRequired",
                    "Please enter a valid HTTP or HTTPS download URL");
                IsValid = false;
                return;
            }
        }

        var artifact = new ReleaseArtifact
        {
            Filename = Filename.Trim(),
            DownloadUrl = UseLocalFile ? string.Empty : DownloadUrl.Trim(),
            Size = FileSize,
            Sha256 = string.IsNullOrWhiteSpace(Sha256Hash) ? string.Empty : Sha256Hash.Trim(),
            IsPrimary = IsPrimary,
            LocalFilePath = UseLocalFile ? LocalFilePath : null,
        };

        ArgumentNullException.ThrowIfNull(onArtifactCreated);
        onArtifactCreated(artifact);
    }

    /// <summary>
    /// Closes the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        // Dialog window will be closed by view binding
    }

    /// <summary>
    /// Cancels the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        // Dialog window will be closed by view binding
    }

    private void Validate()
    {
        ValidateAllProperties();
        IsValid = !HasErrors;
        ValidationError = HasErrors
            ? string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage))
            : null;
    }

    private string GetLocalizedString(string key, string fallback)
    {
        return localizationService?.GetString(key) ?? fallback;
    }
}
