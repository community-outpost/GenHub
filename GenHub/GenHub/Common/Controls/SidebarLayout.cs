using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using System;
using System.Collections;

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
    /// Defines the <see cref="PanePlacement"/> property.
    /// </summary>
    public static readonly StyledProperty<Dock> PanePlacementProperty =
        AvaloniaProperty.Register<SidebarLayout, Dock>(
            nameof(PanePlacement),
            defaultValue: Dock.Left);

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
            defaultValue: SidebarConstants.DefaultOpenPaneLength,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="MinPaneLength"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MinPaneLengthProperty =
        AvaloniaProperty.Register<SidebarLayout, double>(
            nameof(MinPaneLength),
            defaultValue: SidebarConstants.MinPaneLength);

    /// <summary>
    /// Defines the <see cref="MaxPaneLength"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MaxPaneLengthProperty =
        AvaloniaProperty.Register<SidebarLayout, double>(
            nameof(MaxPaneLength),
            defaultValue: SidebarConstants.MaxPaneLength);

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

    private Grid? _rootGrid;
    private ColumnDefinition? _sidebarColumn;
    private ColumnDefinition? _splitterColumn;
    private Control? _sidebarPane;
    private GridSplitter? _splitter;
    private Control? _triggerZone;
    private Border? _expandTab;
    private Control? _contentPresenter;

    static SidebarLayout()
    {
        IsPaneOpenProperty.Changed.AddClassHandler<SidebarLayout>((x, _) => x.OnIsPaneOpenChanged());
        OpenPaneLengthProperty.Changed.AddClassHandler<SidebarLayout>((x, _) => x.OnOpenPaneLengthChanged());
        MinPaneLengthProperty.Changed.AddClassHandler<SidebarLayout>((x, _) => x.OnMinMaxPaneLengthChanged());
        MaxPaneLengthProperty.Changed.AddClassHandler<SidebarLayout>((x, _) => x.OnMinMaxPaneLengthChanged());
        PanePlacementProperty.Changed.AddClassHandler<SidebarLayout>((x, _) => x.OnPanePlacementChanged());
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
    /// Gets or sets the placement of the sidebar pane (Left or Right).
    /// </summary>
    public Dock PanePlacement
    {
        get => GetValue(PanePlacementProperty);
        set => SetValue(PanePlacementProperty, value);
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
    /// Gets or sets additional content displayed at the top of the sidebar pane.
    /// </summary>
    public object? PaneHeader
    {
        get => GetValue(PaneHeaderProperty);
        set => SetValue(PaneHeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets additional content displayed at the bottom of the sidebar pane.
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

    /// <summary>
    /// Sanitizes pane length bounds so hosting views can clamp to the same limits as the control.
    /// </summary>
    /// <param name="min">The requested minimum pane length.</param>
    /// <param name="max">The requested maximum pane length.</param>
    /// <returns>A tuple with the resolved minimum and maximum pane lengths.</returns>
    internal static (double Min, double Max) GetSanitizedBounds(double min, double max)
    {
        var resolvedMin = double.IsNaN(min) || double.IsInfinity(min) || min < 0 ? SidebarConstants.MinPaneLength : min;
        var resolvedMax = double.IsNaN(max) || double.IsInfinity(max) || max < resolvedMin ? Math.Max(resolvedMin, SidebarConstants.MaxPaneLength) : max;
        return (resolvedMin, resolvedMax);
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UnsubscribeEvents();

        _rootGrid = e.NameScope.Find<Grid>("PART_RootGrid");
        _sidebarPane = e.NameScope.Find<Control>("PART_SidebarPane");
        _splitter = e.NameScope.Find<GridSplitter>("PART_Splitter");
        _triggerZone = e.NameScope.Find<Control>("PART_TriggerZone");
        _expandTab = e.NameScope.Find<Border>("PART_ExpandTab");
        _contentPresenter = e.NameScope.Find<Control>("PART_ContentPresenter");

        ConfigurePlacement();
        SubscribeEvents();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeEvents();
        UpdateLayoutState();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnsubscribeEvents();

        if (IsPaneOpen && _sidebarColumn != null && _sidebarColumn.Width.IsAbsolute && _sidebarColumn.Width.Value > 0)
        {
            OpenPaneLength = ClampPaneLength(_sidebarColumn.Width.Value, MinPaneLength, MaxPaneLength);
        }
    }

    private static double ClampPaneLength(double value, double min, double max)
    {
        var (resolvedMin, resolvedMax) = GetSanitizedBounds(min, max);
        var resolvedVal = double.IsNaN(value) || double.IsInfinity(value) ? SidebarConstants.DefaultOpenPaneLength : value;
        return Math.Clamp(resolvedVal, resolvedMin, resolvedMax);
    }

    private static void SetControlVisibility(Control? control, bool isVisible)
    {
        if (control != null)
        {
            control.IsVisible = isVisible;
        }
    }

    private void ConfigurePlacement()
    {
        if (_rootGrid == null || _rootGrid.ColumnDefinitions.Count < 3)
        {
            _sidebarColumn = _rootGrid is { ColumnDefinitions.Count: >= 1 } ? _rootGrid.ColumnDefinitions[0] : null;
            _splitterColumn = _rootGrid is { ColumnDefinitions.Count: >= 2 } ? _rootGrid.ColumnDefinitions[1] : null;
            UpdateLayoutState();
            return;
        }

        var isRight = PanePlacement == Dock.Right;
        var (min, max) = GetSanitizedBounds(MinPaneLength, MaxPaneLength);
        var length = ClampPaneLength(OpenPaneLength, min, max);
        var contentIndex = isRight ? 0 : 2;
        var sidebarIndex = isRight ? 2 : 0;

        SetupColumns(contentIndex, sidebarIndex, min, max, length);
        SetupPlacementChildren(isRight, contentIndex, sidebarIndex);
        UpdateLayoutState();
    }

    private void SetupColumns(int contentIndex, int sidebarIndex, double min, double max, double length)
    {
        if (_rootGrid == null)
        {
            return;
        }

        var contentColumn = _rootGrid.ColumnDefinitions[contentIndex];
        contentColumn.Width = new GridLength(1, GridUnitType.Star);
        contentColumn.MinWidth = 0;
        contentColumn.MaxWidth = double.PositiveInfinity;

        _splitterColumn = _rootGrid.ColumnDefinitions[1];
        _splitterColumn.Width = new GridLength(IsPaneOpen ? SidebarConstants.SplitterWidth : 0, GridUnitType.Pixel);

        _sidebarColumn = _rootGrid.ColumnDefinitions[sidebarIndex];
        _sidebarColumn.Width = new GridLength(IsPaneOpen ? length : 0, GridUnitType.Pixel);
        _sidebarColumn.MinWidth = IsPaneOpen ? min : 0;
        _sidebarColumn.MaxWidth = IsPaneOpen ? max : 0;
    }

    private void SetupPlacementChildren(bool isRight, int contentIndex, int sidebarIndex)
    {
        if (_contentPresenter != null)
        {
            Grid.SetColumn(_contentPresenter, contentIndex);
        }

        if (_splitter != null)
        {
            Grid.SetColumn(_splitter, 1);
        }

        UpdateSidebarPanePlacement(isRight, sidebarIndex);
        UpdateTriggerAndTabPlacement(isRight);
    }

    private void UpdateSidebarPanePlacement(bool isRight, int sidebarIndex)
    {
        if (_sidebarPane == null)
        {
            return;
        }

        Grid.SetColumn(_sidebarPane, sidebarIndex);
        if (_sidebarPane is Border border)
        {
            border.BorderThickness = isRight ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        }
    }

    private void UpdateTriggerAndTabPlacement(bool isRight)
    {
        if (_triggerZone is Border trigger)
        {
            trigger.HorizontalAlignment = isRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        if (_expandTab != null)
        {
            _expandTab.HorizontalAlignment = isRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _expandTab.CornerRadius = isRight ? new CornerRadius(3, 0, 0, 3) : new CornerRadius(0, 3, 3, 0);
        }
    }

    private void SubscribeEvents()
    {
        UnsubscribeEvents();

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
        }
    }

    private void UnsubscribeEvents()
    {
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
        }
    }

    private void OnIsPaneOpenChanged()
    {
        UpdateLayoutState();
    }

    private void OnPanePlacementChanged()
    {
        ConfigurePlacement();
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
            var (min, max) = GetSanitizedBounds(MinPaneLength, MaxPaneLength);
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
        var (min, max) = GetSanitizedBounds(MinPaneLength, MaxPaneLength);
        var length = ClampPaneLength(OpenPaneLength, min, max);

        sidebarColumn.Width = new GridLength(length, GridUnitType.Pixel);
        sidebarColumn.MinWidth = min;
        sidebarColumn.MaxWidth = max;
        splitterColumn.Width = new GridLength(SidebarConstants.SplitterWidth, GridUnitType.Pixel);

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
