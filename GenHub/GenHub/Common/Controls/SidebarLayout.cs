using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;

namespace GenHub.Common.Controls;

/// <summary>
/// A layout control that provides a collapsible, resizable inline sidebar pane and a main content area.
/// </summary>
public class SidebarLayout : ContentControl
{
    /// <summary>
    /// Defines the <see cref="IsPaneOpen"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsPaneOpenProperty =
        AvaloniaProperty.Register<SidebarLayout, bool>(
            nameof(IsPaneOpen),
            defaultValue: true,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="PaneTitle"/> property.
    /// </summary>
    public static readonly StyledProperty<string> PaneTitleProperty =
        AvaloniaProperty.Register<SidebarLayout, string>(nameof(PaneTitle), "Sections");

    /// <summary>
    /// Defines the <see cref="OpenPaneLength"/> property.
    /// </summary>
    public static readonly StyledProperty<double> OpenPaneLengthProperty =
        AvaloniaProperty.Register<SidebarLayout, double>(
            nameof(OpenPaneLength),
            defaultValue: 220.0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="MinPaneLength"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MinPaneLengthProperty =
        AvaloniaProperty.Register<SidebarLayout, double>(nameof(MinPaneLength), 140.0);

    /// <summary>
    /// Defines the <see cref="MaxPaneLength"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MaxPaneLengthProperty =
        AvaloniaProperty.Register<SidebarLayout, double>(nameof(MaxPaneLength), 360.0);

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
        AvaloniaProperty.Register<SidebarLayout, object?>(
            nameof(SelectedItem),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="ItemTemplate"/> property.
    /// </summary>
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<SidebarLayout, IDataTemplate?>(nameof(ItemTemplate));

    private ColumnDefinition? _sidebarColumn;
    private ColumnDefinition? _splitterColumn;
    private Control? _sidebarPane;
    private GridSplitter? _splitter;
    private Control? _triggerZone;

    static SidebarLayout()
    {
        IsPaneOpenProperty.Changed.AddClassHandler<SidebarLayout>((x, _) => x.OnIsPaneOpenChanged());
        OpenPaneLengthProperty.Changed.AddClassHandler<SidebarLayout>((x, _) => x.OnOpenPaneLengthChanged());
        MinPaneLengthProperty.Changed.AddClassHandler<SidebarLayout>((x, _) => x.OnMinMaxPaneLengthChanged());
        MaxPaneLengthProperty.Changed.AddClassHandler<SidebarLayout>((x, _) => x.OnMinMaxPaneLengthChanged());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SidebarLayout"/> class.
    /// </summary>
    public SidebarLayout()
    {
        ClosePaneCommand = new RelayCommand(() => IsPaneOpen = false);
        OpenPaneCommand = new RelayCommand(() => IsPaneOpen = true);
        TogglePaneCommand = new RelayCommand(() => IsPaneOpen = !IsPaneOpen);
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
    /// Gets or sets the minimum width of the sidebar pane when resizing.
    /// </summary>
    public double MinPaneLength
    {
        get => GetValue(MinPaneLengthProperty);
        set => SetValue(MinPaneLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum width of the sidebar pane when resizing.
    /// </summary>
    public double MaxPaneLength
    {
        get => GetValue(MaxPaneLengthProperty);
        set => SetValue(MaxPaneLengthProperty, value);
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

    /// <summary>
    /// Gets the command that opens the sidebar pane.
    /// </summary>
    public IRelayCommand OpenPaneCommand { get; }

    /// <summary>
    /// Gets the command that toggles the sidebar pane open or closed.
    /// </summary>
    public IRelayCommand TogglePaneCommand { get; }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_triggerZone != null)
        {
            _triggerZone.PointerEntered -= OnTriggerZonePointerEntered;
            _triggerZone.PointerPressed -= OnTriggerZonePointerPressed;
        }

        if (_sidebarPane != null)
        {
            _sidebarPane.SizeChanged -= OnSidebarPaneSizeChanged;
        }

        if (_splitter != null)
        {
            _splitter.PointerCaptureLost -= OnSplitterDragCompleted;
            _splitter.PointerReleased -= OnSplitterDragCompleted;
        }

        var rootGrid = e.NameScope.Find<Grid>("PART_RootGrid");
        if (rootGrid != null && rootGrid.ColumnDefinitions.Count >= 2)
        {
            _sidebarColumn = rootGrid.ColumnDefinitions[0];
            _splitterColumn = rootGrid.ColumnDefinitions[1];
        }
        else
        {
            _sidebarColumn = null;
            _splitterColumn = null;
        }

        _sidebarPane = e.NameScope.Find<Control>("PART_SidebarPane");
        _splitter = e.NameScope.Find<GridSplitter>("PART_Splitter");
        _triggerZone = e.NameScope.Find<Control>("PART_TriggerZone");

        if (_triggerZone != null)
        {
            _triggerZone.PointerEntered += OnTriggerZonePointerEntered;
            _triggerZone.PointerPressed += OnTriggerZonePointerPressed;
        }

        if (_sidebarPane != null)
        {
            _sidebarPane.SizeChanged += OnSidebarPaneSizeChanged;
        }

        if (_splitter != null)
        {
            _splitter.PointerCaptureLost += OnSplitterDragCompleted;
            _splitter.PointerReleased += OnSplitterDragCompleted;
        }

        UpdateLayoutState();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_triggerZone != null)
        {
            _triggerZone.PointerEntered -= OnTriggerZonePointerEntered;
            _triggerZone.PointerPressed -= OnTriggerZonePointerPressed;
        }

        if (_sidebarPane != null)
        {
            _sidebarPane.SizeChanged -= OnSidebarPaneSizeChanged;
        }

        if (_splitter != null)
        {
            _splitter.PointerCaptureLost -= OnSplitterDragCompleted;
            _splitter.PointerReleased -= OnSplitterDragCompleted;
        }

        if (IsPaneOpen && _sidebarColumn != null && _sidebarColumn.Width.IsAbsolute && _sidebarColumn.Width.Value > 0)
        {
            OpenPaneLength = ClampPaneLength(_sidebarColumn.Width.Value, MinPaneLength, MaxPaneLength);
        }
    }

    private static double ClampPaneLength(double value, double min, double max)
    {
        var resolvedMin = double.IsNaN(min) || double.IsInfinity(min) || min < 0 ? 0 : min;
        var resolvedMax = double.IsNaN(max) || double.IsInfinity(max) || max < resolvedMin ? Math.Max(resolvedMin, 360.0) : max;

        if (resolvedMin > resolvedMax)
        {
            (resolvedMin, resolvedMax) = (resolvedMax, resolvedMin);
        }

        var resolvedVal = double.IsNaN(value) || double.IsInfinity(value) ? 220.0 : value;
        return Math.Clamp(resolvedVal, resolvedMin, resolvedMax);
    }

    private static void SetControlVisibility(Control? control, bool isVisible)
    {
        if (control != null)
        {
            control.IsVisible = isVisible;
        }
    }

    private void OnIsPaneOpenChanged()
    {
        UpdateLayoutState();
    }

    private void OnOpenPaneLengthChanged()
    {
        if (IsPaneOpen && _sidebarColumn != null)
        {
            var clamped = ClampPaneLength(OpenPaneLength, MinPaneLength, MaxPaneLength);
            if (Math.Abs(_sidebarColumn.Width.Value - clamped) > 0.5)
            {
                _sidebarColumn.Width = new GridLength(clamped, GridUnitType.Pixel);
            }
        }
    }

    private void OnMinMaxPaneLengthChanged()
    {
        if (IsPaneOpen && _sidebarColumn != null)
        {
            var min = Math.Min(MinPaneLength, MaxPaneLength);
            var max = Math.Max(MinPaneLength, MaxPaneLength);
            _sidebarColumn.MinWidth = min;
            _sidebarColumn.MaxWidth = max;
            var clamped = ClampPaneLength(OpenPaneLength, min, max);
            _sidebarColumn.Width = new GridLength(clamped, GridUnitType.Pixel);
        }
    }

    private void OnSidebarPaneSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (IsPaneOpen && _sidebarColumn != null && _sidebarPane != null && _sidebarPane.Bounds.Width > 0)
        {
            var clamped = ClampPaneLength(_sidebarPane.Bounds.Width, MinPaneLength, MaxPaneLength);
            if (Math.Abs(OpenPaneLength - clamped) > 1.0)
            {
                OpenPaneLength = clamped;
            }
        }
    }

    private void OnSplitterDragCompleted(object? sender, RoutedEventArgs e)
    {
        if (IsPaneOpen && _sidebarColumn != null && _sidebarColumn.Width.IsAbsolute && _sidebarColumn.Width.Value > 0)
        {
            OpenPaneLength = ClampPaneLength(_sidebarColumn.Width.Value, MinPaneLength, MaxPaneLength);
        }
    }

    private void UpdateLayoutState()
    {
        if (_sidebarColumn is null || _splitterColumn is null)
        {
            return;
        }

        if (IsPaneOpen)
        {
            ApplyOpenState(_sidebarColumn, _splitterColumn);
        }
        else
        {
            ApplyClosedState(_sidebarColumn, _splitterColumn);
        }
    }

    private void ApplyOpenState(ColumnDefinition sidebarColumn, ColumnDefinition splitterColumn)
    {
        var min = Math.Min(MinPaneLength, MaxPaneLength);
        var max = Math.Max(MinPaneLength, MaxPaneLength);
        var length = ClampPaneLength(OpenPaneLength, min, max);

        sidebarColumn.Width = new GridLength(length, GridUnitType.Pixel);
        sidebarColumn.MinWidth = min;
        sidebarColumn.MaxWidth = max;
        splitterColumn.Width = new GridLength(4, GridUnitType.Pixel);

        SetControlVisibility(_sidebarPane, true);
        SetControlVisibility(_splitter, true);
        SetControlVisibility(_triggerZone, false);
    }

    private void ApplyClosedState(ColumnDefinition sidebarColumn, ColumnDefinition splitterColumn)
    {
        if (sidebarColumn.Width.IsAbsolute && sidebarColumn.Width.Value > 0)
        {
            OpenPaneLength = ClampPaneLength(sidebarColumn.Width.Value, MinPaneLength, MaxPaneLength);
        }

        sidebarColumn.Width = new GridLength(0, GridUnitType.Pixel);
        sidebarColumn.MinWidth = 0;
        sidebarColumn.MaxWidth = 0;
        splitterColumn.Width = new GridLength(0, GridUnitType.Pixel);

        SetControlVisibility(_sidebarPane, false);
        SetControlVisibility(_splitter, false);
        SetControlVisibility(_triggerZone, true);
    }

    private void OnTriggerZonePointerEntered(object? sender, PointerEventArgs e)
    {
        IsPaneOpen = true;
    }

    private void OnTriggerZonePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        IsPaneOpen = true;
    }
}
