using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add New Content dialog.
/// Provides validation and creation of new CatalogContentItem entries.
/// </summary>
public partial class AddContentDialogViewModel : ObservableValidator
{
    private readonly Action<CatalogContentItem> _onContentCreated;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Content ID is required")]
    [RegularExpression(@"^[a-z0-9-]+$", ErrorMessage = "Use lowercase letters, numbers, and hyphens only")]
    [MinLength(2, ErrorMessage = "Content ID must be at least 2 characters")]
    private string _contentId = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Content name is required")]
    [MinLength(2, ErrorMessage = "Content name must be at least 2 characters")]
    private string _contentName = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Description is required")]
    [MinLength(10, ErrorMessage = "Description should be at least 10 characters")]
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
    private string? _validationError;

    [ObservableProperty]
    private bool _isValid;

    /// <summary>
    /// Gets the available content types for selection.
    /// </summary>
    public IReadOnlyList<ContentType> AvailableContentTypes { get; } =
    [
        // Primary content types
        ContentType.Mod,
        ContentType.Map,
        ContentType.MapPack,
        ContentType.Mission,
        ContentType.Addon,
        ContentType.Patch,

        // Media types
        ContentType.Video,
        ContentType.Replay,
        ContentType.Skin,

        // Tools and utilities
        ContentType.ModdingTool,
        ContentType.Executable,

        // Localization
        ContentType.LanguagePack,

        // Collections
        ContentType.ContentBundle,
    ];

    /// <summary>
    /// Gets the available target games for selection.
    /// </summary>
    public IReadOnlyList<GameType> AvailableTargetGames { get; } =
    [
        GameType.ZeroHour,
        GameType.Generals,
    ];

    /// <summary>
    /// Gets a value indicating whether the dialog is in edit mode.
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Gets the dialog title based on the current mode.
    /// </summary>
    public string DialogTitle => IsEditMode ? "Edit Content" : "Add New Content";

    /// <summary>
    /// Gets the submit button text based on the current mode.
    /// </summary>
    public string SubmitButtonText => IsEditMode ? "Save Changes" : "Add Content";

    /// <summary>
    /// Gets a value indicating whether the addon parent selection should be visible.
    /// </summary>
    public bool ShowAddonParentSelection => SelectedContentType == ContentType.Addon;

    /// <summary>
    /// Gets the available parent content items for addon selection.
    /// </summary>
    public ObservableCollection<CatalogContentItem> AvailableParentContent { get; } = new();

    /// <summary>
    /// Gets the suggested content ID based on the content name.
    /// </summary>
    public string SuggestedContentId => GenerateContentId(ContentName);

    /// <summary>
    /// Initializes a new instance of the <see cref="AddContentDialogViewModel"/> class.
    /// </summary>
    /// <param name="onContentCreated">Callback invoked when content is successfully created.</param>
    public AddContentDialogViewModel(Action<CatalogContentItem> onContentCreated)
    {
        _onContentCreated = onContentCreated ?? throw new ArgumentNullException(nameof(onContentCreated));

        // Re-validate when properties change
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ContentId) or nameof(ContentName) or nameof(Description))
            {
                Validate();
            }

            if (e.PropertyName == nameof(SelectedContentType))
            {
                OnPropertyChanged(nameof(ShowAddonParentSelection));
            }
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AddContentDialogViewModel"/> class in edit mode,
    /// pre-populated with an existing content item's data.
    /// </summary>
    /// <param name="existing">The existing content item to edit.</param>
    /// <param name="onContentCreated">Callback invoked when content is successfully saved.</param>
    public AddContentDialogViewModel(CatalogContentItem existing, Action<CatalogContentItem> onContentCreated)
        : this(onContentCreated)
    {
        ArgumentNullException.ThrowIfNull(existing);

        IsEditMode = true;
        ContentId = existing.Id;
        ContentName = existing.Name;
        Description = existing.Description;
        SelectedContentType = existing.ContentType;
        SelectedTargetGame = existing.TargetGame;
        TagsInput = string.Join(", ", existing.Tags);
        ExtendsContentId = existing.ExtendsContentId;
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

        _onContentCreated(contentItem);
    }

    private void Validate()
    {
        ValidateAllProperties();
        IsValid = !HasErrors;
        ValidationError = HasErrors
            ? string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage))
            : null;

        OnPropertyChanged(nameof(SuggestedContentId));
    }
}
