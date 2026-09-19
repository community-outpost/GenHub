using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add Dependency dialog.
/// Provides validation and creation of new CatalogDependency entries.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI and CommunityToolkit ObservableProperty generated properties.")]
public partial class AddDependencyDialogViewModel(
    PublisherCatalog catalog,
    CatalogContentItem currentContent,
    Action<CatalogDependency> onDependencyCreated,
    GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null) : ObservableValidator, IDisposable
{
    private static readonly HttpClient SharedHttpClient = new(
        ImageCacheService.CreateSsrfSafeSocketsHttpHandler())
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private CancellationTokenSource? _discoveryCts;

    [ObservableProperty]
    private bool _isFromMyCatalog = catalog?.Content.Any(c => !string.Equals(c.Id, currentContent.Id, StringComparison.OrdinalIgnoreCase)) == true;

    [ObservableProperty]
    private CatalogContentItem? _selectedContent = catalog?.Content.FirstOrDefault(c => !string.Equals(c.Id, currentContent.Id, StringComparison.OrdinalIgnoreCase));

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
    public IReadOnlyList<CatalogContentItem> AvailableContent { get; } = catalog?.Content
        .Where(c => !string.Equals(c.Id, currentContent.Id, StringComparison.OrdinalIgnoreCase))
        .ToList() ?? [];

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

    private static async Task<(string PublisherId, List<CatalogContentItem> Content)?> TryParseDefinitionCatalogAsync(
        HttpClient client,
        string json,
        CancellationToken cancellationToken)
    {
        PublisherDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<PublisherDefinition>(json, PublisherJsonOptions.Definition);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return null;
        }

        if (definition == null ||
            definition.Publisher == null ||
            (definition.Catalogs.Count == 0 && string.IsNullOrWhiteSpace(definition.CatalogUrl)))
        {
            return null;
        }

        var targetUrl = !string.IsNullOrWhiteSpace(definition.CatalogUrl)
            ? definition.CatalogUrl
            : definition.Catalogs.FirstOrDefault()?.Url;

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return null;
        }

        var (targetSafe, _) = await NetworkSecurityHelper.IsSafeUrlAsync(targetUrl, cancellationToken);
        if (!targetSafe)
        {
            return null;
        }

        PublisherCatalog? parsedCatalog;
        try
        {
            var catalogJson = await CatalogDocumentReader.ReadAsync(client, targetUrl, CatalogConstants.MaxCatalogSizeBytes, cancellationToken);
            parsedCatalog = JsonSerializer.Deserialize<PublisherCatalog>(catalogJson, PublisherJsonOptions.Definition);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }

        if (parsedCatalog == null || parsedCatalog.Content is not { Count: > 0 })
        {
            return null;
        }

        return (definition.Publisher.Id, parsedCatalog.Content);
    }

    private static (string PublisherId, List<CatalogContentItem> Content)? TryParseCatalogJson(string json)
    {
        PublisherCatalog? parsedCatalog;
        try
        {
            parsedCatalog = JsonSerializer.Deserialize<PublisherCatalog>(json, PublisherJsonOptions.Definition);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsedCatalog == null || parsedCatalog.Publisher == null || parsedCatalog.Content is not { Count: > 0 })
        {
            return null;
        }

        return (parsedCatalog.Publisher.Id, parsedCatalog.Content);
    }

    partial void OnIsFromMyCatalogChanged(bool value) => Validate();

    partial void OnSelectedContentChanged(CatalogContentItem? value) => Validate();

    partial void OnExternalPublisherIdChanged(string value) => Validate();

    partial void OnExternalContentIdChanged(string value) => Validate();

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
        ArgumentNullException.ThrowIfNull(onDependencyCreated);
        onDependencyCreated(null!);
    }

    /// <summary>
    /// Creates the dependency if validation passes.
    /// </summary>
    [RelayCommand]
    private void CreateDependency()
    {
        if (IsFromMyCatalog)
        {
            CreateInternalDependency();
        }
        else
        {
            CreateExternalDependency();
        }
    }

    private void CreateInternalDependency()
    {
        if (SelectedContent == null)
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Dependency.SelectContentRequired",
                "Please select a content item from your catalog");
            IsValid = false;
            return;
        }

        var dependency = new CatalogDependency
        {
            PublisherId = catalog.Publisher.Id,
            ContentId = SelectedContent.Id,
            VersionConstraint = string.IsNullOrWhiteSpace(VersionConstraint) ? null : VersionConstraint.Trim(),
            IsOptional = IsOptional,
            CatalogUrl = null, // Same catalog, no URL needed
        };

        ArgumentNullException.ThrowIfNull(onDependencyCreated);
        onDependencyCreated(dependency);
    }

    private void CreateExternalDependency()
    {
        if (string.IsNullOrWhiteSpace(ExternalPublisherId))
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Dependency.PublisherIdRequired",
                "Publisher ID is required for external dependencies");
            IsValid = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(ExternalContentId))
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Dependency.ContentIdRequired",
                "Content ID is required for external dependencies");
            IsValid = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(ExternalCatalogUrl) &&
            !Uri.TryCreate(ExternalCatalogUrl, UriKind.Absolute, out _))
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Dependency.InvalidCatalogUrl",
                "Please enter a valid catalog URL");
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

        ArgumentNullException.ThrowIfNull(onDependencyCreated);
        onDependencyCreated(dependency);
    }

    /// <summary>
    /// Discovers content from a remote catalog or definition URL.
    /// </summary>
    [RelayCommand]
    private async Task DiscoverContentAsync()
    {
        if (string.IsNullOrWhiteSpace(ExternalCatalogUrl))
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Dependency.EnterCatalogUrlFirst",
                "Please enter a Catalog or Provider Definition URL first");
            return;
        }

        var requestedUrl = ExternalCatalogUrl.Trim();
        IsBusy = true;
        ValidationError = null;
        DiscoveredContent.Clear();

        if (_discoveryCts != null)
        {
            await _discoveryCts.CancelAsync();
            _discoveryCts.Dispose();
        }

        _discoveryCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = _discoveryCts.Token;

        try
        {
            var json = await CatalogDocumentReader.ReadAsync(SharedHttpClient, requestedUrl, CatalogConstants.MaxCatalogSizeBytes, ct);
            if (ct.IsCancellationRequested || !string.Equals(ExternalCatalogUrl?.Trim(), requestedUrl, StringComparison.Ordinal))
            {
                return;
            }

            await TryParseCatalogOrDefinitionAsync(SharedHttpClient, json, ct);

            if (ct.IsCancellationRequested || !string.Equals(ExternalCatalogUrl?.Trim(), requestedUrl, StringComparison.Ordinal))
            {
                return;
            }

            if (DiscoveredContent.Count == 0)
            {
                ValidationError = GetLocalizedString(
                    "Tools.PublisherStudio.Dependency.NoContentFound",
                    "No content found at the provided URL");
            }
        }
        catch (OperationCanceledException)
        {
            if (_discoveryCts?.Token != ct)
            {
                return;
            }

            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Dependency.DiscoveryTimeout",
                "Discovery request timed out or was canceled.");
        }
        catch (Exception ex)
        {
            if (_discoveryCts?.Token != ct)
            {
                return;
            }

            ValidationError = localizationService?.GetString(
                    "Tools.PublisherStudio.Dependency.DiscoveryFailed",
                    ex.Message) ?? $"Discovery failed: {ex.Message}";
        }
        finally
        {
            if (_discoveryCts?.Token == ct)
            {
                IsBusy = false;
            }
        }
    }

    private async Task TryParseCatalogOrDefinitionAsync(HttpClient client, string json, CancellationToken cancellationToken)
    {
        var definitionResult = await TryParseDefinitionCatalogAsync(client, json, cancellationToken);
        if (definitionResult.HasValue)
        {
            ExternalPublisherId = definitionResult.Value.PublisherId;
            foreach (var item in definitionResult.Value.Content)
            {
                DiscoveredContent.Add(item);
            }

            return;
        }

        var catalogResult = TryParseCatalogJson(json);
        if (catalogResult.HasValue)
        {
            ExternalPublisherId = catalogResult.Value.PublisherId;
            foreach (var item in catalogResult.Value.Content)
            {
                DiscoveredContent.Add(item);
            }
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
                errors.Add(GetLocalizedString(
                    "Tools.PublisherStudio.Dependency.SelectContentRequired",
                    "Please select a content item from your catalog"));
            }
            else if (AvailableContent.Count == 0)
            {
                errors.Add(GetLocalizedString(
                    "Tools.PublisherStudio.Dependency.NoOtherContentItems",
                    "No other content items available in your catalog"));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ExternalPublisherId))
            {
                errors.Add(GetLocalizedString(
                    "Tools.PublisherStudio.Dependency.PublisherIdRequired",
                    "Publisher ID is required for external dependencies"));
            }

            if (string.IsNullOrWhiteSpace(ExternalContentId))
            {
                errors.Add(GetLocalizedString(
                    "Tools.PublisherStudio.Dependency.ContentIdRequired",
                    "Content ID is required for external dependencies"));
            }
        }

        IsValid = errors.Count == 0;
        ValidationError = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
    }

    private string GetLocalizedString(string key, string fallback)
    {
        return localizationService?.GetString(key) ?? fallback;
    }
}
