using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Tools.Interfaces;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for adding or editing a content item in the catalog.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI and CommunityToolkit ObservableProperty generated properties.")]
public partial class AddContentDialogViewModel : ObservableValidator
{
    private readonly Action<CatalogContentItem> _onContentCreated;
    private readonly IPublisherStudioDialogService? _dialogService;
    private readonly CatalogContentItem? _existingItem;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    [Required(ErrorMessage = "Content ID is required")]
    [RegularExpression(@"^[a-z0-9-]+$", ErrorMessage = "ID must be lowercase alphanumeric with hyphens only")]
    [MinLength(3, ErrorMessage = "ID must be at least 3 characters")]
    [MaxLength(64, ErrorMessage = "ID cannot exceed 64 characters")]
    private string _contentId = string.Empty;

    [ObservableProperty]
    [Required(ErrorMessage = "Content name is required")]
    [MinLength(2, ErrorMessage = "Name must be at least 2 characters")]
    [MaxLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    private string _contentName = string.Empty;

    [ObservableProperty]
    [Required(ErrorMessage = "Description is required")]
    [MinLength(10, ErrorMessage = "Description must be at least 10 characters")]
    [MaxLength(2000, ErrorMessage = "Description cannot exceed 2000 characters")]
    private string _description = string.Empty;

    [ObservableProperty]
    private ContentType _selectedContentType = ContentType.Mod;

    [ObservableProperty]
    private GameType _selectedTargetGame = GameType.ZeroHour;

    [ObservableProperty]
    private string _tagsInput = string.Empty;

    [ObservableProperty]
    private string? _extendsContentId;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string? _validationError;

    // Release & Artifact Direct Download / File support
    [ObservableProperty]
    private bool _includeInitialRelease = true;

    [ObservableProperty]
    private string _initialVersion = "1.0.0";

    [ObservableProperty]
    private bool _useDirectUrl = true;

    [ObservableProperty]
    private string? _downloadUrl;

    [ObservableProperty]
    private string? _localFilePath;

    [ObservableProperty]
    private string? _packageFilename;

    [ObservableProperty]
    private long _fileSize;

    [ObservableProperty]
    private string _fileSizeDisplay = string.Empty;

    [ObservableProperty]
    private string? _sha256Hash;

    [ObservableProperty]
    private bool _isComputingHash;

    /// <summary>
    /// Gets available content types for selection.
    /// </summary>
    public static ContentType[] AvailableContentTypes =>
    [
        ContentType.Mod,
        ContentType.Patch,
        ContentType.Addon,
        ContentType.Map,
        ContentType.MapPack,
        ContentType.LanguagePack,
        ContentType.ContentBundle,
        ContentType.Mission,
        ContentType.Skin,
        ContentType.GameClient,
    ];

    /// <summary>
    /// Gets available target games for selection.
    /// </summary>
    public static GameType[] AvailableTargetGames =>
    [
        GameType.Generals,
        GameType.ZeroHour,
    ];

    /// <summary>
    /// Gets the dialog title based on mode.
    /// </summary>
    public string DialogTitle => IsEditMode ? "Edit Content Item" : "Add Content Item";

    /// <summary>
    /// Gets the primary action button text based on mode.
    /// </summary>
    public string ActionButtonText => IsEditMode ? "Save Changes" : "Create Content";

    /// <summary>
    /// Gets the submit button text for view binding.
    /// </summary>
    public string SubmitButtonText => ActionButtonText;

    /// <summary>
    /// Gets a suggested content ID based on the entered name.
    /// </summary>
    public string SuggestedContentId => GenerateContentId(ContentName);

    /// <summary>
    /// Gets a value indicating whether the content type can extend another.
    /// </summary>
    public bool CanExtend => SelectedContentType == ContentType.Addon;

    /// <summary>
    /// Gets a value indicating whether the addon parent selection field should be shown.
    /// </summary>
    public bool ShowAddonParentSelection => CanExtend;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddContentDialogViewModel"/> class.
    /// </summary>
    /// <param name="onContentCreated">Callback invoked when content is created or saved.</param>
    /// <param name="dialogService">Optional dialog service for browsing files.</param>
    public AddContentDialogViewModel(
        Action<CatalogContentItem> onContentCreated,
        IPublisherStudioDialogService? dialogService = null)
    {
        _onContentCreated = onContentCreated ?? throw new ArgumentNullException(nameof(onContentCreated));
        _dialogService = dialogService;

        // Re-validate when properties change
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ContentId) or nameof(ContentName) or nameof(Description))
            {
                Validate();
            }
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AddContentDialogViewModel"/> class in edit mode.
    /// </summary>
    /// <param name="existing">The existing content item to edit.</param>
    /// <param name="onContentSaved">Callback invoked when content is successfully saved.</param>
    /// <param name="dialogService">Optional dialog service for browsing files.</param>
    public AddContentDialogViewModel(
        CatalogContentItem existing,
        Action<CatalogContentItem> onContentSaved,
        IPublisherStudioDialogService? dialogService = null)
        : this(onContentSaved, dialogService)
    {
        ArgumentNullException.ThrowIfNull(existing);

        _existingItem = existing;
        IsEditMode = true;
        ContentId = existing.Id;
        ContentName = existing.Name;
        Description = existing.Description;
        SelectedContentType = existing.ContentType;
        SelectedTargetGame = existing.TargetGame;
        TagsInput = string.Join(", ", existing.Tags);
        ExtendsContentId = existing.ExtendsContentId;
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }

        return $"{number:n1} {suffixes[counter]}";
    }

    /// <summary>
    /// Generates a content ID from the given name.
    /// </summary>
    /// <param name="name">The content name.</param>
    /// <returns>A URL-friendly content ID.</returns>
    private static string GenerateContentId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // Convert to lowercase, replace spaces with hyphens, remove invalid chars
        var id = name.ToLowerInvariant().Trim();
        id = Regex.Replace(id, @"\s+", "-", RegexOptions.None, TimeSpan.FromSeconds(1));
        id = Regex.Replace(id, @"[^a-z0-9-]", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(1));
        id = Regex.Replace(id, @"-+", "-", RegexOptions.None, TimeSpan.FromSeconds(1));
        id = id.Trim('-');

        return id;
    }

    /// <summary>
    /// Parses a comma or semicolon separated string of tags.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>A list of tags.</returns>
    private static List<string> ParseTags(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .ToList();
    }

    partial void OnSelectedContentTypeChanged(ContentType value)
    {
        OnPropertyChanged(nameof(CanExtend));
        OnPropertyChanged(nameof(ShowAddonParentSelection));
    }

    partial void OnDownloadUrlChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            {
                var name = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(PackageFilename))
                {
                    PackageFilename = name;
                }
            }
        }
        catch
        {
            // Ignore malformed URI while typing
        }
    }

    /// <summary>
    /// Browses for a local archive file (.zip, .big, .7z, etc.).
    /// </summary>
    [RelayCommand]
    private async Task BrowseLocalFileAsync()
    {
        if (_dialogService == null) return;

        var filePath = await _dialogService.ShowFilePickerAsync("Select Content Archive File");
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            LocalFilePath = filePath;
            var info = new FileInfo(filePath);
            FileSize = info.Length;
            FileSizeDisplay = FormatBytes(info.Length);

            if (string.IsNullOrWhiteSpace(PackageFilename))
            {
                PackageFilename = Path.GetFileName(filePath);
            }

            await ComputeSha256Async(filePath);
        }
    }

    private async Task ComputeSha256Async(string filePath)
    {
        try
        {
            IsComputingHash = true;
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream);
            Sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch
        {
            Sha256Hash = string.Empty;
        }
        finally
        {
            IsComputingHash = false;
        }
    }

    /// <summary>
    /// Applies the suggested content ID.
    /// </summary>
    [RelayCommand]
    private void ApplySuggestedId()
    {
        if (!string.IsNullOrWhiteSpace(ContentName))
        {
            ContentId = SuggestedContentId;
        }
    }

    /// <summary>
    /// Closes the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        _onContentCreated(null!);
    }

    /// <summary>
    /// Creates the content item if validation passes.
    /// </summary>
    [RelayCommand]
    private void CreateContent()
    {
        ValidateAllProperties();

        if (HasErrors)
        {
            ValidationError = string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage));
            IsValid = false;
            return;
        }

        var tags = ParseTags(TagsInput);

        var contentItem = new CatalogContentItem
        {
            Id = ContentId.ToLowerInvariant().Trim(),
            Name = ContentName.Trim(),
            Description = Description.Trim(),
            ContentType = SelectedContentType,
            TargetGame = SelectedTargetGame,
            Tags = [.. tags],
            ExtendsContentId = SelectedContentType == ContentType.Addon ? ExtendsContentId : null,
        };

        if (!IsEditMode && IncludeInitialRelease)
        {
            AttachInitialRelease(contentItem);
        }
        else if (IsEditMode)
        {
            CopyFromExistingItem(contentItem);
        }

        _onContentCreated(contentItem);
    }

    private string DetermineArtifactName(string contentId, string version)
    {
        if (!string.IsNullOrWhiteSpace(PackageFilename))
        {
            return PackageFilename.Trim();
        }

        if (!string.IsNullOrWhiteSpace(LocalFilePath))
        {
            return Path.GetFileName(LocalFilePath);
        }

        return $"{contentId}-{version}.zip";
    }

    private void AttachInitialRelease(CatalogContentItem contentItem)
    {
        var version = string.IsNullOrWhiteSpace(InitialVersion) ? "1.0.0" : InitialVersion.Trim();
        var release = new ContentRelease
        {
            Version = version,
            ReleaseDate = DateTime.UtcNow,
            IsLatest = true,
            Artifacts = [],
        };

        var artifactName = DetermineArtifactName(contentItem.Id, version);

        var artifact = new ReleaseArtifact
        {
            Filename = artifactName,
            DownloadUrl = DownloadUrl?.Trim() ?? string.Empty,
            LocalFilePath = LocalFilePath,
            Size = FileSize,
            Sha256 = Sha256Hash?.Trim() ?? string.Empty,
            IsPrimary = true,
        };

        release.Artifacts.Add(artifact);
        contentItem.Releases.Add(release);
    }

    private void CopyFromExistingItem(CatalogContentItem contentItem)
    {
        if (_existingItem == null) return;

        // Preserve existing releases & dependencies
        foreach (var rel in _existingItem.Releases)
        {
            contentItem.Releases.Add(rel);
        }

        foreach (var dep in _existingItem.BundledItems)
        {
            contentItem.BundledItems.Add(dep);
        }
    }

    private void Validate()
    {
        ValidateAllProperties();
        IsValid = !HasErrors;
        ValidationError = HasErrors
            ? string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage))
            : null;
    }
}
