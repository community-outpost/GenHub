using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using GenHub.Features.GitHub.ViewModels;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GitHub.Views;

/// <summary>
/// The view for GitHub details.
/// </summary>
public partial class GitHubDetailsView : UserControl
{
    private readonly ILogger<GitHubDetailsView>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubDetailsView"/> class.
    /// </summary>
    public GitHubDetailsView()
    {
        try
        {
            InitializeComponent();
            _logger = AppLocator.GetServiceOrDefault<ILogger<GitHubDetailsView>>();
            _logger?.LogDebug("GitHubDetailsView initialized");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing GitHubDetailsView: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Called when the view is loaded.
    /// </summary>
    /// <param name="e">The routed event args.</param>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        try
        {
            if (DataContext is GitHubDetailsViewModel viewModel)
            {
                _logger?.LogDebug("GitHubDetailsView loaded with ViewModel");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during GitHubDetailsView loaded event");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
