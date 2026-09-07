using System;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Publishers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the Publisher Profile tab.
/// </summary>
public partial class PublisherProfileViewModel : ObservableValidator
{
    private readonly PublisherStudioProject _project;
    private readonly PublisherStudioViewModel _parentViewModel;
    private readonly ILogger _logger;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Publisher ID is required")]
    [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Publisher ID must use lowercase letters, numbers, and hyphens only (no spaces or special characters)")]
    private string _publisherId = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Publisher Name is required")]
    [MinLength(2, ErrorMessage = "Publisher Name must be at least 2 characters")]
    private string _publisherName = string.Empty;

    [ObservableProperty]
    private string _avatarUrl = string.Empty;

    [ObservableProperty]
    private string _websiteUrl = string.Empty;

    [ObservableProperty]
    private string _supportUrl = string.Empty;

    [ObservableProperty]
    private string _contactEmail = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _tagsString = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherProfileViewModel"/> class.
    /// </summary>
    /// <param name="project">The publisher studio project.</param>
    /// <param name="parentViewModel">The parent view model.</param>
    /// <param name="logger">The logger.</param>
    public PublisherProfileViewModel(
        PublisherStudioProject project,
        PublisherStudioViewModel parentViewModel,
        ILogger logger)
    {
        _project = project;
        _parentViewModel = parentViewModel;
        _logger = logger;

        // Load existing values
        PublisherId = project.Catalog.Publisher.Id;
        PublisherName = project.Catalog.Publisher.Name;
        AvatarUrl = project.Catalog.Publisher.AvatarUrl ?? string.Empty;
        WebsiteUrl = project.Catalog.Publisher.WebsiteUrl ?? string.Empty;
        SupportUrl = project.Catalog.Publisher.SupportUrl ?? string.Empty;
        ContactEmail = project.Catalog.Publisher.ContactEmail ?? string.Empty;
        Description = project.Catalog.Publisher.Description ?? string.Empty;
        TagsString = string.Join(", ", project.Tags);

        // Validate initial state (optional, but good for showing errors on load if data is invalid)
        ValidateAllProperties();

        // Subscribe to property changes
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != nameof(PublisherId) &&
                e.PropertyName != nameof(PublisherName) &&
                e.PropertyName != nameof(AvatarUrl) &&
                e.PropertyName != nameof(WebsiteUrl) &&
                e.PropertyName != nameof(SupportUrl) &&
                e.PropertyName != nameof(ContactEmail) &&
                e.PropertyName != nameof(Description) &&
                e.PropertyName != nameof(TagsString))
            {
                return;
            }

            _parentViewModel.MarkDirty();
        };
    }

    /// <summary>
    /// Saves the publisher profile to the project.
    /// </summary>
    [RelayCommand]
    private void SaveProfile()
    {
        ValidateAllProperties();

        if (HasErrors)
        {
            _logger.LogWarning("Cannot save publisher profile due to validation errors");

            // Ideally assume UI shows errors.
            return;
        }

        try
        {
            _project.Catalog.Publisher.Id = _publisherId.ToLowerInvariant().Trim();
            _project.Catalog.Publisher.Name = _publisherName.Trim();
            _project.Catalog.Publisher.AvatarUrl = string.IsNullOrWhiteSpace(_avatarUrl) ? null : _avatarUrl.Trim();
            _project.Catalog.Publisher.WebsiteUrl = string.IsNullOrWhiteSpace(_websiteUrl) ? null : _websiteUrl.Trim();
            _project.Catalog.Publisher.SupportUrl = string.IsNullOrWhiteSpace(_supportUrl) ? null : _supportUrl.Trim();
            _project.Catalog.Publisher.ContactEmail = string.IsNullOrWhiteSpace(_contactEmail) ? null : _contactEmail.Trim();
            _project.Catalog.Publisher.Description = string.IsNullOrWhiteSpace(_description) ? null : _description.Trim();

            foreach (var namedCatalog in _project.Catalogs)
            {
                namedCatalog.Catalog.Publisher = _project.Catalog.Publisher;
            }

            _project.Tags.Clear();
            _project.Tags.AddRange(_tagsString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            _parentViewModel.MarkDirty();
            _logger.LogInformation("Saved publisher profile: {PublisherId}", _publisherId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save publisher profile");
        }
    }
}
