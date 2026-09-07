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
/// ViewModel for the Add Referral dialog with publisher discovery.
/// </summary>
public partial class AddReferralDialogViewModel : ObservableValidator
{
    private readonly Action<PublisherReferral> _onReferralCreated;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Publisher ID is required")]
    private string _publisherId = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Catalog URL is required")]
    [Url(ErrorMessage = "Please enter a valid URL")]
    private string _catalogUrl = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private PublisherProfile? _discoveredPublisher;

    [ObservableProperty]
    private ObservableCollection<PublisherReferralOption> _availablePublishers = new();

    [ObservableProperty]
    private PublisherReferralOption? _selectedPublisher;

    /// <summary>
    /// Gets example catalog URLs for user guidance.
    /// </summary>
    public IReadOnlyList<string> ExampleCatalogUrls { get; } =
    [
        "https://raw.githubusercontent.com/username/publisher/main/catalog.json",
        "https://gist.githubusercontent.com/username/...",
        "https://example.com/publisher.json",
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="AddReferralDialogViewModel"/> class.
    /// </summary>
    /// <param name="onReferralCreated">Callback invoked when referral is created.</param>
    /// <param name="existingSubscriptions">List of existing publisher subscriptions to offer as suggestions.</param>
    public AddReferralDialogViewModel(
        Action<PublisherReferral> onReferralCreated,
        IEnumerable<PublisherReferralOption>? existingSubscriptions = null)
    {
        _onReferralCreated = onReferralCreated ?? throw new ArgumentNullException(nameof(onReferralCreated));

        // Load available publishers from subscriptions
        if (existingSubscriptions != null)
        {
            foreach (var publisher in existingSubscriptions)
            {
                AvailablePublishers.Add(publisher);
            }
        }

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PublisherId) or nameof(CatalogUrl) or nameof(SelectedPublisher))
            {
                Validate();
            }
        };
    }

    [RelayCommand]
    private void Close()
    {
        _onReferralCreated(null!);
    }

    [RelayCommand]
    private void CreateReferral()
    {
        Validate();
        if (!IsValid) return;

        var referral = new PublisherReferral
        {
            PublisherId = string.IsNullOrWhiteSpace(PublisherId) ? SelectedPublisher?.PublisherId ?? string.Empty : PublisherId.ToLowerInvariant().Trim(),
            CatalogUrl = string.IsNullOrWhiteSpace(CatalogUrl) ? SelectedPublisher?.CatalogUrl ?? string.Empty : CatalogUrl.Trim(),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
        };

        _onReferralCreated(referral);
    }

    /// <summary>
    /// Discovers publisher information from the entered URL.
    /// </summary>
    [RelayCommand]
    private async Task DiscoverPublisherAsync()
    {
        if (string.IsNullOrWhiteSpace(CatalogUrl))
        {
            ValidationError = "Please enter a Catalog or Provider Definition URL first";
            return;
        }

        IsBusy = true;
        ValidationError = null;
        DiscoveredPublisher = null;

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var json = await client.GetStringAsync(CatalogUrl);

            // Try as Publisher Definition first (Tier 3)
            try
            {
                var definition = System.Text.Json.JsonSerializer.Deserialize<PublisherDefinition>(json);
                if (definition != null && definition.Publisher != null)
                {
                    DiscoveredPublisher = definition.Publisher;
                    PublisherId = definition.Publisher.Id;

                    // If definition has a catalog URL, use that instead
                    if (!string.IsNullOrEmpty(definition.CatalogUrl))
                    {
                        CatalogUrl = definition.CatalogUrl;
                    }

                    return;
                }
            }
            catch
            {
                // Not a definition, try as catalog (Tier 2)
            }

            // Try as Publisher Catalog
            try
            {
                var catalog = System.Text.Json.JsonSerializer.Deserialize<PublisherCatalog>(json);
                if (catalog != null && catalog.Publisher != null)
                {
                    DiscoveredPublisher = catalog.Publisher;
                    PublisherId = catalog.Publisher.Id;
                }
                else
                {
                    ValidationError = "No valid publisher information found at URL";
                }
            }
            catch (Exception ex)
            {
                ValidationError = $"Failed to parse catalog: {ex.Message}";
            }
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            ValidationError = $"Failed to fetch URL: {ex.Message}";
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

    /// <summary>
    /// Selects a publisher from the available list.
    /// </summary>
    [RelayCommand]
    private void SelectPublisher(PublisherReferralOption? publisher)
    {
        if (publisher != null)
        {
            SelectedPublisher = publisher;
            PublisherId = publisher.PublisherId;
            CatalogUrl = publisher.CatalogUrl;
        }
    }

    private void Validate()
    {
        var errors = new List<string>();

        // If a publisher is selected from list, use that info
        if (SelectedPublisher != null)
        {
            IsValid = true;
            ValidationError = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(PublisherId))
            errors.Add("Publisher ID is required");

        if (string.IsNullOrWhiteSpace(CatalogUrl))
            errors.Add("Catalog URL is required");
        else if (!Uri.TryCreate(CatalogUrl, UriKind.Absolute, out _))
            errors.Add("Invalid Catalog URL");

        IsValid = errors.Count == 0;
        ValidationError = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
    }
}
