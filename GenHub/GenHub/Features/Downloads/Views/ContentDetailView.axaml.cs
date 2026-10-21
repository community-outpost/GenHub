using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;
using GenHub.Features.Downloads.ViewModels;
using System;
using System.Collections.Specialized;
using System.Windows.Input;
using ParsedImage = GenHub.Core.Models.Parsers.Image;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Code-behind for ContentDetailView.
/// </summary>
public partial class ContentDetailView : UserControl
{
    private readonly TabControl? _detailTabs;
    private readonly ICommand _openCardImageCommand;
    private Point? resizeStartPoint;
    private double initialModalWidth;
    private double initialModalHeight;
    private ContentDetailViewModel? _customTabsViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentDetailView"/> class.
    /// </summary>
    public ContentDetailView()
    {
        InitializeComponent();

        _detailTabs = this.FindControl<TabControl>("DetailTabs");
        _openCardImageCommand = new RelayCommand<CustomTabCardDefinition>(
            OpenCardImage,
            card => MediaFileHelper.IsRemoteHttpUrl(card?.ImageUrl));

        var resizeGrip = this.FindControl<Border>("VideoModalResizeGrip");
        if (resizeGrip != null)
        {
            resizeGrip.PointerPressed += OnResizeGripPointerPressed;
            resizeGrip.PointerMoved += OnResizeGripPointerMoved;
            resizeGrip.PointerReleased += OnResizeGripPointerReleased;
            resizeGrip.PointerCaptureLost += OnResizeGripPointerCaptureLost;
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindCustomTabs(DataContext as ContentDetailViewModel);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindCustomTabs(DataContext as ContentDetailViewModel);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        BindCustomTabs(null);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && DataContext is ContentDetailViewModel vm && vm.IsVideoPlayerOpen)
        {
            if (vm.IsVideoPlayerFullscreen)
            {
                vm.IsVideoPlayerFullscreen = false;
            }
            else
            {
                vm.CloseVideoPlayerCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ContentDetailViewModel vm && !vm.IsVideoPlayerFullscreen)
        {
            resizeStartPoint = e.GetPosition(this);
            initialModalWidth = vm.VideoModalWidth;
            initialModalHeight = vm.VideoModalHeight;
            if (sender is InputElement grip)
            {
                e.Pointer.Capture(grip);
            }

            e.Handled = true;
        }
    }

    private void OnResizeGripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (resizeStartPoint.HasValue && DataContext is ContentDetailViewModel vm && !vm.IsVideoPlayerFullscreen)
        {
            var currentPoint = e.GetPosition(this);
            var deltaX = currentPoint.X - resizeStartPoint.Value.X;
            var deltaY = currentPoint.Y - resizeStartPoint.Value.Y;

            var maxWidth = Math.Max(0, Bounds.Width - 48);
            var maxHeight = Math.Max(0, Bounds.Height - 48);
            var minWidth = Math.Min(560, maxWidth);
            var minHeight = Math.Min(380, maxHeight);

            vm.VideoModalWidth = Math.Clamp(initialModalWidth + deltaX, minWidth, maxWidth);
            vm.VideoModalHeight = Math.Clamp(initialModalHeight + deltaY, minHeight, maxHeight);
            e.Handled = true;
        }
    }

    private void OnResizeGripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndResize(e.Pointer);
        e.Handled = true;
    }

    private void OnResizeGripPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndResize(null);
    }

    private void BindCustomTabs(ContentDetailViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _customTabsViewModel))
        {
            return;
        }

        if (_customTabsViewModel != null)
        {
            _customTabsViewModel.CustomTabs.CollectionChanged -= OnCustomTabsChanged;
        }

        _customTabsViewModel = viewModel;
        if (_customTabsViewModel != null)
        {
            _customTabsViewModel.CustomTabs.CollectionChanged += OnCustomTabsChanged;
        }

        RebuildCustomTabs();
    }

    private void OnCustomTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildCustomTabs();
    }

    private void RebuildCustomTabs()
    {
        if (_detailTabs == null)
        {
            return;
        }

        var items = _detailTabs.Items;
        while (items.Count > ContentDetailViewModel.BuiltInTabCount)
        {
            items.RemoveAt(items.Count - 1);
        }

        if (_customTabsViewModel != null)
        {
            foreach (var tab in _customTabsViewModel.CustomTabs)
            {
                items.Add(new TabItem
                {
                    Header = tab.Header,
                    Content = new CustomTabContentView
                    {
                        DataContext = tab,
                        ImageCommand = _openCardImageCommand,
                    },
                });
            }
        }

        if (_detailTabs.SelectedIndex < 0 || _detailTabs.SelectedIndex >= items.Count)
        {
            _detailTabs.SelectedIndex = 0;
        }
    }

    private void OpenCardImage(CustomTabCardDefinition? card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.ImageUrl) || _customTabsViewModel == null)
        {
            return;
        }

        _customTabsViewModel.OpenFullScreenMediaCommand.Execute(new ParsedImage(card.Title, card.ImageUrl, card.ImageUrl));
    }

    private void EndResize(IPointer? pointer)
    {
        resizeStartPoint = null;
        pointer?.Capture(null);
    }
}
