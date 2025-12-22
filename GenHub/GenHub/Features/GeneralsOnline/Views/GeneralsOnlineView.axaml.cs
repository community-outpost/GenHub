using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.GeneralsOnline.Views;

/// <summary>
/// View for the Generals Online tab displaying multiplayer service information.
/// </summary>
public partial class GeneralsOnlineView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineView"/> class.
    /// </summary>
    public GeneralsOnlineView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}