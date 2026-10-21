using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Renders one publisher-defined custom tab: a plain-text intro, an optional Markdown body, and a card grid.
/// </summary>
/// <remarks>
/// Publisher content is shown only through text, the safe Markdown viewer, and image loading.
/// No publisher markup or web content is executed.
/// </remarks>
public partial class CustomTabContentView : UserControl
{
    /// <summary>
    /// Defines the <see cref="ImageCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ImageCommandProperty =
        AvaloniaProperty.Register<CustomTabContentView, ICommand?>(nameof(ImageCommand));

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomTabContentView"/> class.
    /// </summary>
    public CustomTabContentView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the command invoked with the card when a card image is clicked.
    /// </summary>
    public ICommand? ImageCommand
    {
        get => GetValue(ImageCommandProperty);
        set => SetValue(ImageCommandProperty, value);
    }
}
