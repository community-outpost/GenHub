using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add Artifact dialog.
/// Provides validation and creation of new ReleaseArtifact entries.
/// </summary>
public partial class AddArtifactDialogViewModel : ObservableValidator
{
    private readonly Action<ReleaseArtifact> _onArtifactCreated;

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
    private string _artifactStatus = "No file configured";

    /// <summary>
    /// Gets or sets a value indicating whether to use an existing URL instead of uploading a file.
    /// </summary>
    public bool UseExistingUrl
    {
        get => !_useLocalFile;
        set
        {
            if (_useLocalFile == !value) return;
            UseLocalFile = !value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets a value indicating whether a local file has been selected.
    /// </summary>
    public bool IsLocalFile => !string.IsNullOrEmpty(_localFilePath);

    /// <summary>
    /// Gets a value indicating whether the artifact is hosted remotely (URL set, no local file).
    /// </summary>
    public bool IsHosted => !string.IsNullOrEmpty(_downloadUrl) && !IsLocalFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddArtifactDialogViewModel"/> class.
    /// </summary>
    /// <param name="onArtifactCreated">Callback invoked when artifact is successfully created.</param>
    public AddArtifactDialogViewModel(Action<ReleaseArtifact> onArtifactCreated)
    {
        _onArtifactCreated = onArtifactCreated ?? throw new ArgumentNullException(nameof(onArtifactCreated));

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Filename) or nameof(DownloadUrl) or nameof(LocalFilePath) or nameof(UseLocalFile))
            {
                Validate();
            }
        };
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

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    partial void OnUseLocalFileChanged(bool value)
    {
        if (value)
        {
            DownloadUrl = string.Empty;
        }
        else
        {
            LocalFilePath = null;
        }

        OnPropertyChanged(nameof(UseExistingUrl));
        UpdateArtifactStatus();
    }

    partial void OnLocalFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsLocalFile));
        OnPropertyChanged(nameof(IsHosted));
        UpdateArtifactStatus();
    }

    partial void OnDownloadUrlChanged(string value)
    {
        OnPropertyChanged(nameof(IsLocalFile));
        OnPropertyChanged(nameof(IsHosted));
        UpdateArtifactStatus();
    }

    private void UpdateArtifactStatus()
    {
        if (!string.IsNullOrEmpty(_localFilePath))
            ArtifactStatus = "Local file selected - will be uploaded during publish";
        else if (!string.IsNullOrEmpty(_downloadUrl))
            ArtifactStatus = "Hosted remotely";
        else
            ArtifactStatus = "No file configured";
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

                    // Compute SHA256 in background
                    IsComputingHash = true;
                    try
                    {
                        Sha256Hash = await Task.Run(() => ComputeSha256(path));
                    }
                    finally
                    {
                        IsComputingHash = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ArtifactStatus = $"Error: {ex.Message}";
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
        if (string.IsNullOrWhiteSpace(_localFilePath) || !File.Exists(_localFilePath))
        {
            ValidationError = "Please select a local file first";
            return;
        }

        IsComputingHash = true;
        ValidationError = null;

        try
        {
            await using var stream = File.OpenRead(_localFilePath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, CancellationToken.None);
            Sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            ValidationError = $"Failed to compute hash: {ex.Message}";
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
        if (_useLocalFile)
        {
            // Local file mode - require local file path
            if (string.IsNullOrWhiteSpace(_localFilePath) || !System.IO.File.Exists(_localFilePath))
            {
                ValidationError = "Please select a local file to upload";
                IsValid = false;
                return;
            }
        }
        else
        {
            // URL mode - require valid URL
            if (string.IsNullOrWhiteSpace(_downloadUrl) ||
                !Uri.TryCreate(_downloadUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ValidationError = "Please enter a valid HTTP or HTTPS download URL";
                IsValid = false;
                return;
            }
        }

        var artifact = new ReleaseArtifact
        {
            Filename = Filename.Trim(),
            DownloadUrl = DownloadUrl.Trim(),
            Size = FileSize,
            Sha256 = string.IsNullOrWhiteSpace(Sha256Hash) ? string.Empty : Sha256Hash.Trim(),
            IsPrimary = IsPrimary,
            LocalFilePath = LocalFilePath,
        };

        _onArtifactCreated(artifact);
    }

    /// <summary>
    /// Closes the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        _onArtifactCreated(null!);
    }

    private void Validate()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            IsValid = false;
            ValidationError = string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage));
            return;
        }

        if (!_useLocalFile)
        {
            if (string.IsNullOrWhiteSpace(_downloadUrl))
            {
                IsValid = false;
                ValidationError = "Download URL is required";
                return;
            }

            if (!Uri.TryCreate(_downloadUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                IsValid = false;
                ValidationError = "Please enter a valid HTTP or HTTPS download URL";
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_localFilePath))
            {
                IsValid = false;
                ValidationError = "Please select a local file to upload";
                return;
            }
        }

        IsValid = true;
        ValidationError = null;
    }
}
