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
    /// Defines the <see cref="EditCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> EditCommandProperty =
        AvaloniaProperty.Register<CustomTabContentView, ICommand?>(nameof(EditCommand));

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

    /// <summary>
    /// Gets or sets the command invoked with the tab by the edit buttons on its header, body and cards.
    /// The edit buttons are shown only when the command is set, as in Publisher Studio.
    /// </summary>
    public ICommand? EditCommand
    {
        get => GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }
}
