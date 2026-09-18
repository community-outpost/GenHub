using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.ViewModels.Catalog;
using GenHub.Features.Downloads.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// View model for importing a catalog or publisher provider subscription via URL or genhub:// link.
/// </summary>
public partial class ImportSubscriptionViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty]
    private string _inputUrl = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isValidating;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportSubscriptionViewModel"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public ImportSubscriptionViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _localizationService = serviceProvider.GetService<ILocalizationService>();
    }

    /// <summary>
    /// Gets or sets an action called to request closing the dialog.
    /// </summary>
    public Action<bool?>? RequestClose { get; set; }

    private static string ResolveTargetUrl(string raw)
    {
        var targetUrl = CommandLineParser.ExtractSubscriptionUrl([raw]);
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            targetUrl = raw;
        }

        return CloudUrlHelper.NormalizeDirectDownloadUrl(targetUrl);
    }

    private string GetLocalizedString(string key, string fallback) =>
        _localizationService?.GetString(key) ?? fallback;

    private string GetLocalizedString(string key, string fallback, params object?[] args)
    {
        var format = _localizationService?.GetString(key);
        return string.IsNullOrEmpty(format) || string.Equals(format, key, StringComparison.Ordinal)
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, fallback, args)
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
    }

    private async Task LaunchConfirmationDialogAsync(string targetUrl)
    {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var parent = desktop?.MainWindow;

        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var client = httpClientFactory.CreateClient(CatalogConstants.CatalogHttpClientName);
        var confirmVm = ActivatorUtilities.CreateInstance<SubscriptionConfirmationViewModel>(_serviceProvider, targetUrl, client);
        var confirmDialog = new SubscriptionConfirmationDialog
        {
            DataContext = confirmVm,
        };

        confirmDialog.Opened += (_, _) => RequestClose?.Invoke(true);

        if (parent != null)
        {
            await confirmDialog.ShowDialog(parent);
        }
        else
        {
            confirmDialog.Show();
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(InputUrl))
        {
            ErrorMessage = GetLocalizedString("Downloads.ImportSubscription.Error.UrlRequired", "Please enter a URL or genhub:// link.");
            return;
        }

        var targetUrl = ResolveTargetUrl(InputUrl.Trim());

        // The downstream catalog reader enforces HTTPS, so reject plain HTTP here for immediate feedback.
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            ErrorMessage = GetLocalizedString("Downloads.ImportSubscription.Error.InvalidUrl", "Invalid URL format. Please provide a valid HTTPS or genhub:// link.");
            return;
        }

        IsValidating = true;
        try
        {
            await LaunchConfirmationDialogAsync(targetUrl);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetLocalizedString("Downloads.ImportSubscription.Error.LaunchFailedFormat", "Failed to launch subscription confirmation: {0}", ex.Message);
        }
        finally
        {
            IsValidating = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}
