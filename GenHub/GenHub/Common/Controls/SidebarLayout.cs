using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace GenHub.Common.Controls;

/// <summary>
/// A layout control that provides a collapsible sidebar pane and a main content area.
/// </summary>
public class SidebarLayout : ContentControl
{
    /// <summary>
    /// Defines the <see cref="IsPaneOpen"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsPaneOpenProperty =
        AvaloniaProperty.Register<SidebarLayout, bool>(nameof(IsPaneOpen), defaultValue: false);

    /// <summary>
    /// Defines the <see cref="PaneTitle"/> property.
    /// </summary>
    public static readonly StyledProperty<string> PaneTitleProperty =
        AvaloniaProperty.Register<SidebarLayout, string>(nameof(PaneTitle), "Sections");

    /// <summary>
    /// Defines the <see cref="OpenPaneLength"/> property.
    /// </summary>
    public static readonly StyledProperty<double> OpenPaneLengthProperty =
        AvaloniaProperty.Register<SidebarLayout, double>(nameof(OpenPaneLength), 300);

    /// <summary>
    /// Defines the <see cref="PaneHeader"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> PaneHeaderProperty =
        AvaloniaProperty.Register<SidebarLayout, object?>(nameof(PaneHeader));

    /// <summary>
    /// Defines the <see cref="PaneFooter"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> PaneFooterProperty =
        AvaloniaProperty.Register<SidebarLayout, object?>(nameof(PaneFooter));

    /// <summary>
    /// Defines the <see cref="ItemsSource"/> property.
    /// </summary>
    public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.Register<SidebarLayout, IEnumerable>(nameof(ItemsSource));

    /// <summary>
    /// Defines the <see cref="SelectedItem"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SidebarLayout, object?>(nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="ItemTemplate"/> property.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<SidebarLayout, IDataTemplate?>(nameof(ItemTemplate));

    private Panel? _triggerZone;
    private Panel? _contentOverlay;
    private Control? _sidebarPane;

    /// <summary>
    /// Initializes a new instance of the <see cref="SidebarLayout"/> class.
    /// </summary>
    public SidebarLayout()
    {
        ClosePaneCommand = new RelayCommand(() => IsPaneOpen = false);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the sidebar pane is open.
    /// </summary>
    public bool IsPaneOpen
    {
        get => GetValue(IsPaneOpenProperty);
        set => SetValue(IsPaneOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the title displayed in the sidebar pane.
    /// </summary>
    public string PaneTitle
    {
        get => GetValue(PaneTitleProperty);
        set => SetValue(PaneTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the sidebar pane when it is open.
    /// </summary>
    public double OpenPaneLength
    {
        get => GetValue(OpenPaneLengthProperty);
        set => SetValue(OpenPaneLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the content to be displayed in the header of the sidebar pane.
    /// </summary>
    public object? PaneHeader
    {
        get => GetValue(PaneHeaderProperty);
        set => SetValue(PaneHeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the content to be displayed in the footer of the sidebar pane.
    /// </summary>
    public object? PaneFooter
    {
        get => GetValue(PaneFooterProperty);
        set => SetValue(PaneFooterProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of items used to generate the sidebar content.
    /// </summary>
    public IEnumerable ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected item in the sidebar.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the template used to display each item in the sidebar.
    /// </summary>
    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>
    /// Gets the command that closes the sidebar pane.
    /// </summary>
    public IRelayCommand ClosePaneCommand { get; }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_triggerZone != null)
        {
            _triggerZone.PointerEntered -= OnTriggerZonePointerEntered;
        }

        if (_contentOverlay != null)
        {
            _contentOverlay.PointerPressed -= OnContentPointerPressed;
            _contentOverlay.PointerEntered -= OnContentPointerEntered;
        }

        if (_sidebarPane != null)
        {
            _sidebarPane.PointerExited -= OnSidebarPanePointerExited;
        }

        _triggerZone = e.NameScope.Find<Panel>("PART_TriggerZone");
        _contentOverlay = e.NameScope.Find<Panel>("PART_ContentOverlay");
        _sidebarPane = e.NameScope.Find<Control>("PART_SidebarPane");

        if (_triggerZone != null)
        {
            _triggerZone.PointerEntered += OnTriggerZonePointerEntered;
        }

        if (_contentOverlay != null)
        {
            _contentOverlay.PointerPressed += OnContentPointerPressed;
            _contentOverlay.PointerEntered += OnContentPointerEntered;
        }

        if (_sidebarPane != null)
        {
            _sidebarPane.PointerExited += OnSidebarPanePointerExited;
        }
    }

    private void OnTriggerZonePointerEntered(object? sender, PointerEventArgs e)
    {
        IsPaneOpen = true;
    }

    private void OnSidebarPanePointerExited(object? sender, PointerEventArgs e)
    {
        // Only close if we are actually outside the pane bounds
        // This simple check works for now; more robust hit testing could be added if needed
        var point = e.GetPosition(_sidebarPane);
        if (_sidebarPane != null &&
            (point.X < 0 || point.X >= _sidebarPane.Bounds.Width ||
             point.Y < 0 || point.Y >= _sidebarPane.Bounds.Height))
        {
            IsPaneOpen = false;
        }
    }

    private void OnContentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        IsPaneOpen = false;
    }

    private void OnContentPointerEntered(object? sender, PointerEventArgs e)
    {
        IsPaneOpen = false;
    }
}
