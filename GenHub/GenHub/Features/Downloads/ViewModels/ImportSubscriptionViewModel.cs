using System;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Helpers;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.ViewModels.Catalog;
using GenHub.Features.Downloads.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// View model for importing a catalog or publisher provider subscription via URL or genhub:// link.
/// </summary>
public partial class ImportSubscriptionViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;

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
    }

    /// <summary>
    /// Gets or sets an action called to request closing the dialog.
    /// </summary>
    public Action<bool?>? RequestClose { get; set; }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(InputUrl))
        {
            ErrorMessage = "Please enter a URL or genhub:// link.";
            return;
        }

        var raw = InputUrl.Trim();
        var targetUrl = CommandLineParser.ExtractSubscriptionUrl(new[] { raw });
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            targetUrl = raw;
        }

        targetUrl = CloudUrlHelper.NormalizeDirectDownloadUrl(targetUrl);

        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ErrorMessage = "Invalid URL format. Please provide a valid HTTP, HTTPS, or genhub:// link.";
            return;
        }

        IsValidating = true;
        try
        {
            var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var parent = desktop?.MainWindow;

            var confirmVm = ActivatorUtilities.CreateInstance<SubscriptionConfirmationViewModel>(_serviceProvider, targetUrl);
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
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to launch subscription confirmation: {ex.Message}";
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
