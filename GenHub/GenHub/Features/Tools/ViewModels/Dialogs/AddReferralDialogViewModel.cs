using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;
using GenHub.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add Referral dialog with publisher discovery.
/// </summary>
[SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI.")]
public partial class AddReferralDialogViewModel : ObservableValidator, IDisposable
{
    private static readonly HttpClient SharedHttpClient = new(
        ImageCacheService.CreateSsrfSafeSocketsHttpHandler())
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly Action<PublisherReferral> _onReferralCreated;
    private CancellationTokenSource? _discoveryCts;

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
            _discoveryCts?.Cancel();
            _discoveryCts?.Dispose();
            _discoveryCts = null;
        }
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

        var requestedUrl = CatalogUrl.Trim();
        IsBusy = true;
        ValidationError = null;
        DiscoveredPublisher = null;

        if (_discoveryCts != null)
        {
            await _discoveryCts.CancelAsync();
            _discoveryCts.Dispose();
        }

        _discoveryCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = _discoveryCts.Token;

        try
        {
            var json = await SharedHttpClient.GetStringAsync(requestedUrl, ct);
            if (ct.IsCancellationRequested || !string.Equals(CatalogUrl?.Trim(), requestedUrl, StringComparison.Ordinal))
            {
                return;
            }

            if (TryExtractPublisherFromDefinition(json, out var defProfile, out var newCatalogUrl))
            {
                DiscoveredPublisher = defProfile;
                PublisherId = defProfile.Id;
                if (!string.IsNullOrEmpty(newCatalogUrl))
                {
                    CatalogUrl = newCatalogUrl;
                }

                return;
            }

            if (TryExtractPublisherFromCatalog(json, out var catProfile, out var parseError))
            {
                DiscoveredPublisher = catProfile;
                PublisherId = catProfile.Id;
                return;
            }

            ValidationError = parseError;
        }
        catch (OperationCanceledException)
        {
            SetDiscoveryErrorIfCurrentToken(ct, "Discovery request timed out or was canceled.");
        }
        catch (HttpRequestException ex)
        {
            SetDiscoveryErrorIfCurrentToken(ct, $"Failed to fetch URL: {ex.Message}");
        }
        catch (Exception ex)
        {
            SetDiscoveryErrorIfCurrentToken(ct, $"Discovery failed: {ex.Message}");
        }
        finally
        {
            if (_discoveryCts?.Token == ct)
            {
                IsBusy = false;
            }
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

    private bool TryExtractPublisherFromDefinition(
        string json,
        [NotNullWhen(true)] out PublisherProfile? profile,
        out string? catalogUrl)
    {
        profile = null;
        catalogUrl = null;

        try
        {
            var definition = System.Text.Json.JsonSerializer.Deserialize<PublisherDefinition>(json);
            if (definition?.Publisher != null)
            {
                profile = definition.Publisher;
                catalogUrl = definition.CatalogUrl;
                return true;
            }
        }
        catch
        {
            // Not a definition, fallback to catalog parser
        }

        return false;
    }

    private bool TryExtractPublisherFromCatalog(
        string json,
        [NotNullWhen(true)] out PublisherProfile? profile,
        out string? error)
    {
        profile = null;
        error = null;

        try
        {
            var catalog = System.Text.Json.JsonSerializer.Deserialize<PublisherCatalog>(json);
            if (catalog?.Publisher != null)
            {
                profile = catalog.Publisher;
                return true;
            }

            error = "No valid publisher information found at URL";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse catalog: {ex.Message}";
            return false;
        }
    }

    private void SetDiscoveryErrorIfCurrentToken(CancellationToken ct, string message)
    {
        if (_discoveryCts?.Token == ct)
        {
            ValidationError = message;
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
