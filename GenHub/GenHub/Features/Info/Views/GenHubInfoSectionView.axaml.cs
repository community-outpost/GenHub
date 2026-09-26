using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GenHub.Common.Controls;
using GenHub.Core.Models.Info;
using GenHub.Features.Info.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for GenHubInfoSectionView.axaml.
/// </summary>
public partial class GenHubInfoSectionView : UserControl
{
    private GenHubInfoSectionViewModel? _boundViewModel;
    private SectionScrollSpy<InfoCardViewModel>? _scrollSpy;
    private ScrollViewer? _contentScrollViewer;
    private ItemsControl? _cardsItemsControl;
    private ItemsControl? _faqLeftItemsControl;
    private ItemsControl? _faqRightItemsControl;
    private ChangelogsView? _changelogsView;
    private GeneralsOnlineChangelogView? _goChangelogView;
    private bool _syncingSelectionFromScroll;
    private bool _deferredScrollPending;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenHubInfoSectionView"/> class.
    /// </summary>
    public GenHubInfoSectionView()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitializeControls();
        if (DataContext is GenHubInfoSectionViewModel vm)
        {
            HookViewModel(vm);
            EnsureScrollSpy();
            RegisterAllCardContainers();
            if (vm.SelectedCard != null)
            {
                ScrollToCard(vm.SelectedCard);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _scrollSpy?.Dispose();
        _scrollSpy = null;
        _deferredScrollPending = false;
        UnhookControls();
        UnhookViewModel();
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UnhookViewModel();
        if (DataContext is GenHubInfoSectionViewModel vm)
        {
            HookViewModel(vm);
            if (VisualRoot != null)
            {
                EnsureScrollSpy();
                RegisterAllCardContainers();
                if (vm.SelectedCard != null)
                {
                    ScrollToCard(vm.SelectedCard);
                }
            }
        }
    }

    private void InitializeControls()
    {
        UnhookControls();

        _contentScrollViewer = this.FindControl<ScrollViewer>("InfoContentScrollViewer");
        _cardsItemsControl = this.FindControl<ItemsControl>("CardsItemsControl");
        _faqLeftItemsControl = this.FindControl<ItemsControl>("FaqLeftItemsControl");
        _faqRightItemsControl = this.FindControl<ItemsControl>("FaqRightItemsControl");
        _changelogsView = this.FindControl<ChangelogsView>("GenHubChangelogsView");
        _goChangelogView = this.FindControl<GeneralsOnlineChangelogView>("GenHubGoChangelogView");

        HookItemsControl(_cardsItemsControl);
        HookItemsControl(_faqLeftItemsControl);
        HookItemsControl(_faqRightItemsControl);
    }

    private void HookItemsControl(ItemsControl? control)
    {
        if (control != null)
        {
            control.ContainerPrepared += OnCardContainerPrepared;
            control.ContainerClearing += OnCardContainerClearing;
        }
    }

    private void UnhookItemsControl(ItemsControl? control)
    {
        if (control != null)
        {
            control.ContainerPrepared -= OnCardContainerPrepared;
            control.ContainerClearing -= OnCardContainerClearing;
        }
    }

    private void UnhookControls()
    {
        UnhookItemsControl(_cardsItemsControl);
        UnhookItemsControl(_faqLeftItemsControl);
        UnhookItemsControl(_faqRightItemsControl);

        _cardsItemsControl = null;
        _faqLeftItemsControl = null;
        _faqRightItemsControl = null;
        _contentScrollViewer = null;
        _changelogsView = null;
        _goChangelogView = null;
    }

    private void HookViewModel(GenHubInfoSectionViewModel vm)
    {
        if (ReferenceEquals(_boundViewModel, vm))
        {
            return;
        }

        UnhookViewModel();
        _boundViewModel = vm;
        _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _boundViewModel.ScrollToCardRequested += OnScrollToCardRequested;
    }

    private void UnhookViewModel()
    {
        if (_boundViewModel != null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel.ScrollToCardRequested -= OnScrollToCardRequested;
            _boundViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GenHubInfoSectionViewModel.SelectedSection))
        {
            _scrollSpy?.ClearSections();
            _contentScrollViewer?.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, 0));
            Dispatcher.UIThread.Post(
                () =>
                {
                    RegisterAllCardContainers();
                },
                DispatcherPriority.Loaded);
        }
        else if (e.PropertyName == nameof(GenHubInfoSectionViewModel.SelectedCard) && _boundViewModel?.SelectedCard != null && !_syncingSelectionFromScroll)
        {
            ScrollToCard(_boundViewModel.SelectedCard);
        }
    }

    private void OnScrollToCardRequested(InfoCardViewModel card)
    {
        if (!_syncingSelectionFromScroll)
        {
            ScrollToCard(card);
        }
    }

    private void EnsureScrollSpy()
    {
        if (_scrollSpy != null)
        {
            return;
        }

        if (VisualRoot == null)
        {
            return;
        }

        _contentScrollViewer ??= this.FindControl<ScrollViewer>("InfoContentScrollViewer");
        if (_contentScrollViewer is null)
        {
            return;
        }

        _scrollSpy = new SectionScrollSpy<InfoCardViewModel>(_contentScrollViewer, OnSpyCardActivated);
        _scrollSpy.Attach();
    }

    private void RegisterAllCardContainers()
    {
        if (_scrollSpy == null)
        {
            return;
        }

        RegisterCardsFromControl(_cardsItemsControl ??= this.FindControl<ItemsControl>("CardsItemsControl"));
        RegisterCardsFromControl(_faqLeftItemsControl ??= this.FindControl<ItemsControl>("FaqLeftItemsControl"));
        RegisterCardsFromControl(_faqRightItemsControl ??= this.FindControl<ItemsControl>("FaqRightItemsControl"));

        _changelogsView ??= this.FindControl<ChangelogsView>("GenHubChangelogsView");
        _goChangelogView ??= this.FindControl<GeneralsOnlineChangelogView>("GenHubGoChangelogView");

        if (_boundViewModel?.SelectedSection?.Cards != null)
        {
            foreach (var card in _boundViewModel.SelectedSection.Cards)
            {
                if (card.TargetItem is ChangelogItemViewModel chItem && _changelogsView != null)
                {
                    if (_changelogsView.ContainerFromItem(chItem) is Control chControl)
                    {
                        _scrollSpy.RegisterSection(card, chControl);
                    }
                }
                else if (card.TargetItem is PatchNote pnItem && _goChangelogView != null)
                {
                    if (_goChangelogView.ContainerFromItem(pnItem) is Control pnControl)
                    {
                        _scrollSpy.RegisterSection(card, pnControl);
                    }
                }
            }
        }
    }

    private void RegisterCardsFromControl(ItemsControl? control)
    {
        if (_scrollSpy == null || control?.ItemsSource is not IEnumerable<InfoCardViewModel> cards)
        {
            return;
        }

        foreach (var card in cards)
        {
            var container = control.ContainerFromItem(card);
            if (container is Control c)
            {
                _scrollSpy.RegisterSection(card, c);
            }
        }
    }

    private void OnCardContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (_scrollSpy != null && e.Container is Control control && control.DataContext is InfoCardViewModel card)
        {
            _scrollSpy.RegisterSection(card, control);
        }
    }

    private void OnCardContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (_scrollSpy != null && e.Container is Control control)
        {
            _scrollSpy.RemoveControl(control);
        }
    }

    private void OnSpyCardActivated(InfoCardViewModel card)
    {
        if (_boundViewModel == null || _scrollSpy == null || _scrollSpy.IsScrollingProgrammatically)
        {
            return;
        }

        _syncingSelectionFromScroll = true;
        try
        {
            _boundViewModel.UpdateCardFromScroll(card);
        }
        finally
        {
            _syncingSelectionFromScroll = false;
        }
    }

    private void ScrollToCard(InfoCardViewModel card)
    {
        if (card.IsExpandable && !card.IsExpanded)
        {
            card.IsExpanded = true;
        }

        if (card.TargetItem is ChangelogItemViewModel releaseItem)
        {
            releaseItem.IsExpanded = true;
        }
        else if (card.TargetItem is PatchNote patchNote)
        {
            patchNote.IsExpanded = true;
        }

        EnsureScrollSpy();
        if (_scrollSpy == null)
        {
            return;
        }

        Control? container = null;
        if (card.TargetItem is ChangelogItemViewModel chItem)
        {
            _changelogsView ??= this.FindControl<ChangelogsView>("GenHubChangelogsView");
            container = _changelogsView?.ContainerFromItem(chItem);
        }
        else if (card.TargetItem is PatchNote pnItem)
        {
            _goChangelogView ??= this.FindControl<GeneralsOnlineChangelogView>("GenHubGoChangelogView");
            container = _goChangelogView?.ContainerFromItem(pnItem);
        }

        container ??= (_cardsItemsControl?.ContainerFromItem(card)
            ?? _faqLeftItemsControl?.ContainerFromItem(card)
            ?? _faqRightItemsControl?.ContainerFromItem(card)) as Control;

        if (container == null || container.Bounds.Height <= 0)
        {
            if (!_deferredScrollPending)
            {
                _deferredScrollPending = true;
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        _deferredScrollPending = false;
                        if (_boundViewModel?.SelectedCard != null && ReferenceEquals(_boundViewModel.SelectedCard, card))
                        {
                            RegisterAllCardContainers();
                            _scrollSpy?.ScrollToSection(card);
                        }
                    },
                    DispatcherPriority.Loaded);
            }

            return;
        }

        _scrollSpy.RegisterSection(card, container);
        _scrollSpy.ScrollToSection(card);
    }
}
