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
public partial class AddReferralDialogViewModel(
    Action<PublisherReferral> onReferralCreated,
    IEnumerable<PublisherReferralOption>? existingSubscriptions = null) : ObservableValidator, IDisposable
{
    private static readonly HttpClient SharedHttpClient = new(
        ImageCacheService.CreateSsrfSafeSocketsHttpHandler())
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

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
    private ObservableCollection<PublisherReferralOption> _availablePublishers = new(
        existingSubscriptions ?? Enumerable.Empty<PublisherReferralOption>());

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

    partial void OnPublisherIdChanged(string value) => Validate();

    partial void OnCatalogUrlChanged(string value) => Validate();

    partial void OnSelectedPublisherChanged(PublisherReferralOption? value) => Validate();

    /// <summary>
    /// Closes the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        ArgumentNullException.ThrowIfNull(onReferralCreated);
        onReferralCreated(null!);
    }

    /// <summary>
    /// Creates the referral if validation passes.
    /// </summary>
    [RelayCommand]
    private void CreateReferral()
    {
        Validate();

        if (!IsValid) return;

        var referral = new PublisherReferral
        {
            PublisherId = PublisherId.ToLowerInvariant().Trim(),
            CatalogUrl = CatalogUrl.Trim(),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
        };

        ArgumentNullException.ThrowIfNull(onReferralCreated);
        onReferralCreated(referral);
    }

    /// <summary>
    /// Attempts to discover publisher information from the entered Catalog URL.
    /// </summary>
    [RelayCommand]
    private async Task DiscoverPublisherAsync()
    {
        if (string.IsNullOrWhiteSpace(CatalogUrl) || !Uri.TryCreate(CatalogUrl, UriKind.Absolute, out var uri))
        {
            ValidationError = "Please enter a valid URL first";
            return;
        }

        _discoveryCts?.Cancel();
        _discoveryCts = new CancellationTokenSource();
        var ct = _discoveryCts.Token;

        IsBusy = true;
        ValidationError = null;

        try
        {
            var response = await SharedHttpClient.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
            {
                SetDiscoveryErrorIfCurrentToken(ct, $"Could not fetch catalog: HTTP {(int)response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);

            // Attempt to parse as PublisherDefinition first (which contains PublisherProfile)
            if (TryExtractPublisherFromDefinition(json, out var defProfile, out var extractedUrl))
            {
                DiscoveredPublisher = defProfile;
                PublisherId = defProfile.Id;
                if (!string.IsNullOrEmpty(extractedUrl))
                {
                    CatalogUrl = extractedUrl;
                }

                Validate();
                return;
            }

            // Fallback: try parsing as a raw PublisherCatalog
            if (TryExtractPublisherFromCatalog(json, out var catProfile, out var error))
            {
                DiscoveredPublisher = catProfile;
                PublisherId = catProfile.Id;
                Validate();
                return;
            }

            SetDiscoveryErrorIfCurrentToken(ct, error ?? "Could not find publisher details in the response");
        }
        catch (OperationCanceledException)
        {
            // Request was canceled, do nothing
        }
        catch (Exception ex)
        {
            SetDiscoveryErrorIfCurrentToken(ct, $"Discovery error: {ex.Message}");
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
