using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GenHub.Common.Controls;
using GenHub.Core.Constants;
using GenHub.Features.Downloads.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Code-behind for DownloadsBrowserView.
/// </summary>
public partial class DownloadsBrowserView : UserControl
{
    private readonly TextBlock _measureBlock = new()
    {
        FontSize = SidebarConstants.DownloadsPublisherNameFontSize,
        FontWeight = FontWeight.SemiBold,
    };

    private readonly HashSet<PublisherItemViewModel> _hookedPublishers = [];
    private DownloadsBrowserViewModel? _boundViewModel;
    private INotifyCollectionChanged? _boundPublisherCollection;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadsBrowserView"/> class.
    /// </summary>
    public DownloadsBrowserView()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is DownloadsBrowserViewModel vm)
        {
            HookViewModel(vm);
            AutoFitPaneToPublishers();
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnhookViewModel();
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UnhookViewModel();
        if (DataContext is DownloadsBrowserViewModel vm)
        {
            HookViewModel(vm);
            AutoFitPaneToPublishers();
        }
    }

    private void HookViewModel(DownloadsBrowserViewModel vm)
    {
        if (ReferenceEquals(_boundViewModel, vm))
        {
            return;
        }

        UnhookViewModel();
        _boundViewModel = vm;
        _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
        HookPublisherCollection(vm);
    }

    private void UnhookViewModel()
    {
        UnhookPublisherCollection();
        if (_boundViewModel != null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }
    }

    private void HookPublisherCollection(DownloadsBrowserViewModel vm)
    {
        UnhookPublisherCollection();
        foreach (var publisher in vm.Publishers)
        {
            HookPublisherItem(publisher);
        }

        if (vm.Publishers is INotifyCollectionChanged notify)
        {
            _boundPublisherCollection = notify;
            notify.CollectionChanged += OnPublishersChanged;
        }
    }

    private void UnhookPublisherCollection()
    {
        if (_boundPublisherCollection != null)
        {
            _boundPublisherCollection.CollectionChanged -= OnPublishersChanged;
            _boundPublisherCollection = null;
        }

        foreach (var publisher in _hookedPublishers)
        {
            publisher.PropertyChanged -= OnPublisherPropertyChanged;
        }

        _hookedPublishers.Clear();
    }

    private void HookPublisherItem(PublisherItemViewModel publisher)
    {
        if (_hookedPublishers.Add(publisher))
        {
            publisher.PropertyChanged += OnPublisherPropertyChanged;
        }
    }

    private void UnhookPublisherItem(PublisherItemViewModel publisher)
    {
        if (_hookedPublishers.Remove(publisher))
        {
            publisher.PropertyChanged -= OnPublisherPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadsBrowserViewModel.Publishers) && _boundViewModel != null)
        {
            HookPublisherCollection(_boundViewModel);
            AutoFitPaneToPublishers();
        }
    }

    private void OnPublishersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            if (_boundViewModel != null)
            {
                HookPublisherCollection(_boundViewModel);
            }
        }
        else
        {
            if (e.OldItems != null)
            {
                foreach (PublisherItemViewModel removed in e.OldItems)
                {
                    UnhookPublisherItem(removed);
                }
            }

            if (e.NewItems != null)
            {
                foreach (PublisherItemViewModel added in e.NewItems)
                {
                    HookPublisherItem(added);
                }
            }
        }

        AutoFitPaneToPublishers();
    }

    private void OnPublisherPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PublisherItemViewModel.DisplayName) ||
            e.PropertyName == nameof(PublisherItemViewModel.ContentCount))
        {
            AutoFitPaneToPublishers();
        }
    }

    private void AutoFitPaneToPublishers()
    {
        if (_boundViewModel is not { Publishers.Count: > 0 } vm)
        {
            return;
        }

        _measureBlock.FontFamily = GetValue(TextBlock.FontFamilyProperty);
        var maxTextWidth = 0.0;
        foreach (var publisher in vm.Publishers)
        {
            if (string.IsNullOrEmpty(publisher.DisplayName))
            {
                continue;
            }

            _measureBlock.Text = publisher.DisplayName;
            _measureBlock.Measure(Size.Infinity);
            maxTextWidth = Math.Max(maxTextWidth, _measureBlock.DesiredSize.Width);
        }

        var required = SidebarConstants.DownloadsPaneChromeWidth + maxTextWidth;
        if (vm.Publishers.Any(publisher => publisher.ContentCount > 0))
        {
            required += SidebarConstants.DownloadsPaneBadgeAllowance;
        }

        var (min, max) = SidebarLayout.GetSanitizedBounds(GetPaneMinLength(), GetPaneMaxLength());
        required = Math.Clamp(required, min, max);
        if (required > vm.OpenPaneLength)
        {
            vm.OpenPaneLength = required;
        }
    }

    private double GetPaneMinLength()
    {
        return FindSidebarLayout()?.MinPaneLength ?? SidebarConstants.MinPaneLength;
    }

    private double GetPaneMaxLength()
    {
        return FindSidebarLayout()?.MaxPaneLength ?? SidebarConstants.MaxPaneLength;
    }

    private SidebarLayout? FindSidebarLayout()
    {
        return Content as SidebarLayout;
    }
}
