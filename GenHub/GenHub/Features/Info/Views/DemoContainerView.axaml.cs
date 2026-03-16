using Avalonia;
using Avalonia.Controls;

namespace GenHub.Features.Info.Views;

/// <summary>
/// A container view for interactive UI demonstrations.
/// </summary>
public partial class DemoContainerView : UserControl
{
    /// <summary>
    /// Defines the Title property.
    /// </summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<DemoContainerView, string>(nameof(Title), "Demo");

    /// <summary>
    /// Defines the Description property.
    /// </summary>
    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<DemoContainerView, string>(nameof(Description), string.Empty);

    /// <summary>
    /// Defines the DemoContent property for the embedded view.
    /// </summary>
    public static readonly StyledProperty<object?> DemoContentProperty =
        AvaloniaProperty.Register<DemoContainerView, object?>(nameof(DemoContent));

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoContainerView"/> class.
    /// </summary>
    public DemoContainerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the demo title.
    /// </summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the demo description.
    /// </summary>
    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets the demo content (the embedded view).
    /// </summary>
    public object? DemoContent
    {
        get => GetValue(DemoContentProperty);
        set => SetValue(DemoContentProperty, value);
    }
}
