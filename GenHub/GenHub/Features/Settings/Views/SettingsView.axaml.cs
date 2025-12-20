using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.Settings.Views;

/// <summary>
/// Represents the view for application settings in the GenHub application.
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsView"/> class.
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();

        // Handle pointer press to unfocus text boxes when clicking elsewhere
        this.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // If clicking outside of a TextBox, clear focus from any focused TextBox
        if (e.Source is not TextBox)
        {
            this.Focus();
        }
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        // In Avalonia, we can't use GetBindingExpression like in WPF
        // The binding will automatically update when focus is lost if properly configured
        // This method exists for potential future enhancements
    }

    private void OnOpenPatCreationUrl(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Core.Constants.GitHubConstants.PatCreationUrl)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Silently fail if browser cannot be opened
        }
    }

    private void OnViewWorkflowRun(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string url && !string.IsNullOrEmpty(url))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Silently fail if browser cannot be opened
            }
        }
    }

    /// <summary>
    /// Loads and initializes the XAML components for this view.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}