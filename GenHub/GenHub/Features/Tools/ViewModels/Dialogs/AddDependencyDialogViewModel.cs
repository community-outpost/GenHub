using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add Dependency dialog.
/// Provides validation and creation of new CatalogDependency entries.
/// </summary>
public partial class AddDependencyDialogViewModel : ObservableValidator
{
    private readonly PublisherCatalog _catalog;
    private readonly CatalogContentItem _currentContent;
    private readonly Action<CatalogDependency> _onDependencyCreated;

    [ObservableProperty]
    private bool _isFromMyCatalog = true;

    [ObservableProperty]
    private CatalogContentItem? _selectedContent;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Publisher ID is required for external dependencies")]
    private string _externalPublisherId = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Content ID is required for external dependencies")]
    private string _externalContentId = string.Empty;

    [ObservableProperty]
    [Url(ErrorMessage = "Please enter a valid catalog URL")]
    private string _externalCatalogUrl = string.Empty;

    [ObservableProperty]
    private string _versionConstraint = string.Empty;

    [ObservableProperty]
    private bool _isOptional;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ObservableCollection<CatalogContentItem> _discoveredContent = [];

    [ObservableProperty]
    private string _conflictsWithIds = string.Empty;

    /// <summary>
    /// Gets the content items from the catalog that can be selected as dependencies.
    /// Excludes the current content item to prevent circular dependencies.
    /// </summary>
    public IReadOnlyList<CatalogContentItem> AvailableContent { get; }

    /// <summary>
    /// Gets example version constraints for user guidance.
    /// </summary>
    public IReadOnlyList<string> VersionConstraintExamples { get; } =
    [
        ">=1.0.0",
        "^2.0.0",
        "~1.2.0",
        ">=1.0.0 <2.0.0",
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="AddDependencyDialogViewModel"/> class.
    /// </summary>
    /// <param name="catalog">The current publisher catalog.</param>
    /// <param name="currentContent">The content item being edited (to exclude from dependencies).</param>
    /// <param name="onDependencyCreated">Callback invoked when dependency is successfully created.</param>
    public AddDependencyDialogViewModel(
        PublisherCatalog catalog,
        CatalogContentItem currentContent,
        Action<CatalogDependency> onDependencyCreated)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _currentContent = currentContent ?? throw new ArgumentNullException(nameof(currentContent));
        _onDependencyCreated = onDependencyCreated ?? throw new ArgumentNullException(nameof(onDependencyCreated));

        // Filter out current content to prevent self-dependency
        AvailableContent = catalog.Content
            .Where(c => c.Id != currentContent.Id)
            .ToList();

        // Auto-select first available if any
        if (AvailableContent.Count > 0)
        {
            SelectedContent = AvailableContent.FirstOrDefault();
            IsFromMyCatalog = true;
        }
        else
        {
            // If no other content exists (e.g. only 1 item total), default to External
            IsFromMyCatalog = false;
        }

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsFromMyCatalog) or nameof(SelectedContent) or
                nameof(ExternalPublisherId) or nameof(ExternalContentId))
            {
                Validate();
            }
        };
    }

    /// <summary>
    /// Gets display text for a content item in the dropdown.
    /// </summary>
    /// <param name="content">The content item.</param>
    /// <returns>Display text showing name and latest version.</returns>
    public static string GetContentDisplayText(CatalogContentItem content)
    {
        if (content == null)
        {
            return string.Empty;
        }

        var latestVersion = content.Releases
            .Where(r => r.IsLatest)
            .Select(r => r.Version)
            .FirstOrDefault() ?? content.Releases.FirstOrDefault()?.Version ?? "unknown";

        return $"{content.Name} v{latestVersion}";
    }

    /// <summary>
    /// Applies an example version constraint.
    /// </summary>
    /// <param name="example">The example constraint to apply.</param>
    [RelayCommand]
    private void ApplyVersionConstraintExample(string? example)
    {
        if (!string.IsNullOrWhiteSpace(example))
        {
            VersionConstraint = example;
        }
    }

    /// <summary>
    /// Closes the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        _onDependencyCreated(null!);
    }

    /// <summary>
    /// Creates the dependency if validation passes.
    /// </summary>
    [RelayCommand]
    private void CreateDependency()
    {
        if (IsFromMyCatalog)
        {
            if (SelectedContent == null)
            {
                ValidationError = "Please select a content item from your catalog";
                IsValid = false;
                return;
            }

            var dependency = new CatalogDependency
            {
                PublisherId = _catalog.Publisher.Id,
                ContentId = SelectedContent.Id,
                VersionConstraint = string.IsNullOrWhiteSpace(VersionConstraint) ? null : VersionConstraint.Trim(),
                IsOptional = IsOptional,
                CatalogUrl = null, // Same catalog, no URL needed
            };

            _onDependencyCreated(dependency);
        }
        else
        {
            // Validate external dependency fields
            if (string.IsNullOrWhiteSpace(ExternalPublisherId))
            {
                ValidationError = "Publisher ID is required for external dependencies";
                IsValid = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(ExternalContentId))
            {
                ValidationError = "Content ID is required for external dependencies";
                IsValid = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(ExternalCatalogUrl) &&
                !Uri.TryCreate(ExternalCatalogUrl, UriKind.Absolute, out _))
            {
                ValidationError = "Please enter a valid catalog URL";
                IsValid = false;
                return;
            }

            var dependency = new CatalogDependency
            {
                PublisherId = ExternalPublisherId.ToLowerInvariant().Trim(),
                ContentId = ExternalContentId.ToLowerInvariant().Trim(),
                VersionConstraint = string.IsNullOrWhiteSpace(VersionConstraint) ? null : VersionConstraint.Trim(),
                IsOptional = IsOptional,
                CatalogUrl = string.IsNullOrWhiteSpace(ExternalCatalogUrl) ? null : ExternalCatalogUrl.Trim(),
                ConflictsWith = ConflictsWithIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            };

            _onDependencyCreated(dependency);
        }
    }

    /// <summary>
    /// Discovers content from a remote catalog or definition URL.
    /// </summary>
    [RelayCommand]
    private async Task DiscoverContentAsync()
    {
        if (string.IsNullOrWhiteSpace(ExternalCatalogUrl))
        {
            ValidationError = "Please enter a Catalog or Provider Definition URL first";
            return;
        }

        IsBusy = true;
        ValidationError = null;
        DiscoveredContent.Clear();

        try
        {
            // We'll need a way to fetch and parse.
            // In a real app we'd inject an IContentDiscoveryService or similar.
            // For now, let's assume we can fetch the JSON.
            using var client = new System.Net.Http.HttpClient();
            var json = await client.GetStringAsync(ExternalCatalogUrl);

            // Try as Catalog first
            try
            {
                var catalog = System.Text.Json.JsonSerializer.Deserialize<PublisherCatalog>(json);
                if (catalog != null)
                {
                    ExternalPublisherId = catalog.Publisher.Id;
                    foreach (var item in catalog.Content)
                    {
                        DiscoveredContent.Add(item);
                    }
                }
            }
            catch
            {
                // Try as Definition
                var definition = System.Text.Json.JsonSerializer.Deserialize<PublisherDefinition>(json);
                if (definition != null)
                {
                    ExternalPublisherId = definition.Publisher.Id;

                    // Now fetch the actual catalog
                    var catalogJson = await client.GetStringAsync(definition.CatalogUrl);
                    var catalog = System.Text.Json.JsonSerializer.Deserialize<PublisherCatalog>(catalogJson);
                    if (catalog != null)
                    {
                        foreach (var item in catalog.Content)
                        {
                            DiscoveredContent.Add(item);
                        }
                    }
                }
            }

            if (DiscoveredContent.Count == 0)
            {
                ValidationError = "No content found at the provided URL";
            }
        }
        catch (Exception ex)
        {
            ValidationError = $"Discovery failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectDiscoveredContent(CatalogContentItem content)
    {
        if (content != null)
        {
            ExternalContentId = content.Id;
        }
    }

    private void Validate()
    {
        var errors = new List<string>();

        if (IsFromMyCatalog)
        {
            if (SelectedContent == null && AvailableContent.Count > 0)
            {
                errors.Add("Please select a content item from your catalog");
            }
            else if (AvailableContent.Count == 0)
            {
                errors.Add("No other content items available in your catalog");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ExternalPublisherId))
            {
                errors.Add("Publisher ID is required for external dependencies");
            }

            if (string.IsNullOrWhiteSpace(ExternalContentId))
            {
                errors.Add("Content ID is required for external dependencies");
            }
        }

        IsValid = errors.Count == 0;
        ValidationError = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
    }
}
