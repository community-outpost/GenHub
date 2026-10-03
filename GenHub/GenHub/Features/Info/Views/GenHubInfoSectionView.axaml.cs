using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GenHub.Common.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Models.Info;
using GenHub.Features.Info.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Features.Info.Views;

/// <summary>
/// Interaction logic for GenHubInfoSectionView.axaml.
/// </summary>
public partial class GenHubInfoSectionView : UserControl
{
    private const string ChangelogsViewName = "GenHubChangelogsView";
    private const string GoChangelogViewName = "GenHubGoChangelogView";

    private readonly Dictionary<string, Vector> _sectionScrollOffsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ItemsControl> _toolCardControls = [];
    private string? _currentSectionId;
    private GenHubInfoSectionViewModel? _boundViewModel;
    private SectionScrollSpy<InfoCardViewModel>? _scrollSpy;

    private ScrollViewer? _contentScrollViewer;
    private ItemsControl? _cardsItemsControl;
    private ItemsControl? _faqLeftItemsControl;
    private ItemsControl? _faqRightItemsControl;
    private ChangelogsView? _changelogsView;
    private GeneralsOnlineChangelogView? _goChangelogView;
    private bool _syncingSelectionFromScroll;
    private bool _isSwitchingSection;
    private InfoCardViewModel? _deferredScrollCard;
    private int _sectionSwitchGeneration;

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
        _deferredScrollCard = null;
        UnhookControls();
        UnhookViewModel();
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _scrollSpy?.ClearSections();
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
        _changelogsView = this.FindControl<ChangelogsView>(ChangelogsViewName);
        _goChangelogView = this.FindControl<GeneralsOnlineChangelogView>(GoChangelogViewName);

        HookItemsControl(_cardsItemsControl);
        HookItemsControl(_faqLeftItemsControl);
        HookItemsControl(_faqRightItemsControl);

        string[] toolControlNames =
        [
            "ToolsIntroCardsControl",
            "ToolsReplayCardsControl",
            "ToolsMapCardsControl",
            "ToolsHotkeyCardsControl",
            "ToolsPublisherCardsControl",
            "ToolsModBuilderCardsControl",
            "ToolsWndCardsControl",
        ];
        foreach (var name in toolControlNames)
        {
            var control = this.FindControl<ItemsControl>(name);
            if (control != null)
            {
                _toolCardControls.Add(control);
                HookItemsControl(control);
            }
        }
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
        foreach (var control in _toolCardControls)
        {
            UnhookItemsControl(control);
        }

        _toolCardControls.Clear();
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
        _boundViewModel.ChangelogsLoaded += OnChangelogsLoaded;
        _currentSectionId = vm.SelectedSection?.Id;
    }

    private void UnhookViewModel()
    {
        _sectionSwitchGeneration++;
        _isSwitchingSection = false;
        _sectionScrollOffsets.Clear();
        if (_boundViewModel != null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel.ChangelogsLoaded -= OnChangelogsLoaded;
            _boundViewModel = null;
        }
    }

    private void OnChangelogsLoaded()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                RegisterAllCardContainers();
                if (_boundViewModel?.SelectedCard is { } selectedCard)
                {
                    ScrollToCard(selectedCard);
                }
            },
            DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GenHubInfoSectionViewModel.SelectedSection))
        {
            _isSwitchingSection = true;

            // Save the outgoing section scroll offset
            if (!string.IsNullOrEmpty(_currentSectionId) && _contentScrollViewer != null)
            {
                _sectionScrollOffsets[_currentSectionId] = _contentScrollViewer.Offset;
            }

            _scrollSpy?.StopAnimation();
            _scrollSpy?.ClearSections();

            var newSectionId = _boundViewModel?.SelectedSection?.Id;
            _currentSectionId = newSectionId;
            var targetOffset = (newSectionId != null && _sectionScrollOffsets.TryGetValue(newSectionId, out var savedOffset))
                ? savedOffset
                : Vector.Zero;

            _contentScrollViewer?.SetCurrentValue(ScrollViewer.OffsetProperty, targetOffset);

            var generation = ++_sectionSwitchGeneration;
            var expectedViewModel = _boundViewModel;
            var expectedSectionId = newSectionId;

            Dispatcher.UIThread.Post(
                () =>
                {
                    if (generation != _sectionSwitchGeneration || _boundViewModel != expectedViewModel || _boundViewModel?.SelectedSection?.Id != expectedSectionId)
                    {
                        return;
                    }

                    RegisterAllCardContainers();
                    _contentScrollViewer?.SetCurrentValue(ScrollViewer.OffsetProperty, targetOffset);
                    _isSwitchingSection = false;
                },
                DispatcherPriority.Loaded);
        }
        else if (e.PropertyName == nameof(GenHubInfoSectionViewModel.SelectedCard) && _boundViewModel?.SelectedCard != null && !_syncingSelectionFromScroll && !_isSwitchingSection)
        {
            ScrollToCard(_boundViewModel.SelectedCard);
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

    private Control? FindDemoContainer(string cardId) => cardId switch
    {
        InfoConstants.CardProfilesDemo => this.FindControl<Control>("ProfilesDemoContainer"),
        InfoConstants.CardShortcutsDemo => this.FindControl<Control>("ShortcutsDemoContainer"),
        InfoConstants.CardSteamDemo => this.FindControl<Control>("SteamDemoContainer"),
        InfoConstants.CardToolsDemo => this.FindControl<Control>("ToolsDemoContainer"),
        InfoConstants.CardToolsReplayDemo => this.FindControl<Control>("ReplayDemoContainer"),
        InfoConstants.CardToolsMapDemo => this.FindControl<Control>("MapDemoContainer"),
        InfoConstants.CardToolsHotkeyDemo => this.FindControl<Control>("HotkeyDemoContainer"),
        InfoConstants.CardToolsPublisherDemo => this.FindControl<Control>("PublisherDemoContainer"),
        InfoConstants.CardToolsModBuilderDemo => this.FindControl<Control>("ModBuilderDemoContainer"),
        InfoConstants.CardToolsWndDemo => this.FindControl<Control>("WndDemoContainer"),
        InfoConstants.CardSettingsDemo => this.FindControl<Control>("GameSettingsDemoContainer"),
        InfoConstants.CardContentDemo => this.FindControl<Control>("GameProfileContentDemoContainer"),
        InfoConstants.CardLocalContentDemo => this.FindControl<Control>("LocalContentDemoContainer"),
        InfoConstants.CardScanDemo => this.FindControl<Control>("ScanDemoContainer"),
        InfoConstants.CardWorkspaceDemo => this.FindControl<Control>("WorkspacesDemoContainer"),
        InfoConstants.CardUpdatesDemo => this.FindControl<Control>("AppUpdatesDemoContainer"),
        InfoConstants.CardChangelogsOverview => this.FindControl<Control>(ChangelogsViewName),
        InfoConstants.CardGoChangelogOverview => this.FindControl<Control>(GoChangelogViewName),
        _ => null,
    };

    private void RegisterAllCardContainers()
    {
        if (_scrollSpy == null)
        {
            return;
        }

        RegisterCardsFromControl(_cardsItemsControl ??= this.FindControl<ItemsControl>("CardsItemsControl"));
        RegisterCardsFromControl(_faqLeftItemsControl ??= this.FindControl<ItemsControl>("FaqLeftItemsControl"));
        RegisterCardsFromControl(_faqRightItemsControl ??= this.FindControl<ItemsControl>("FaqRightItemsControl"));
        foreach (var toolControl in _toolCardControls)
        {
            RegisterCardsFromControl(toolControl);
        }

        _changelogsView ??= this.FindControl<ChangelogsView>(ChangelogsViewName);
        _goChangelogView ??= this.FindControl<GeneralsOnlineChangelogView>(GoChangelogViewName);

        if (_boundViewModel?.SelectedSection?.Cards != null)
        {
            foreach (var card in _boundViewModel.SelectedSection.Cards)
            {
                RegisterSectionCard(card);
            }
        }
    }

    private void RegisterSectionCard(InfoCardViewModel card)
    {
        var demoCtrl = FindDemoContainer(card.Id);
        if (demoCtrl != null)
        {
            _scrollSpy?.RegisterSection(card, demoCtrl);
        }
        else if (card.TargetItem is ChangelogItemViewModel chItem && _changelogsView?.ContainerFromItem(chItem) is Control chControl)
        {
            _scrollSpy?.RegisterSection(card, chControl);
        }
        else if (card.TargetItem is PatchNote pnItem && _goChangelogView?.ContainerFromItem(pnItem) is Control pnControl)
        {
            _scrollSpy?.RegisterSection(card, pnControl);
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
        if (_isSwitchingSection || _boundViewModel == null || _scrollSpy == null || _scrollSpy.IsScrollingProgrammatically)
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

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Kept as instance method to satisfy StyleCop member ordering rules")]
    private void ExpandCardAndTarget(InfoCardViewModel card)
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
    }

    private Control? ResolveCardContainer(InfoCardViewModel card)
    {
        Control? container = null;
        if (card.TargetItem is ChangelogItemViewModel chItem)
        {
            _changelogsView ??= this.FindControl<ChangelogsView>(ChangelogsViewName);
            container = _changelogsView?.ContainerFromItem(chItem);
        }
        else if (card.TargetItem is PatchNote pnItem)
        {
            _goChangelogView ??= this.FindControl<GeneralsOnlineChangelogView>(GoChangelogViewName);
            container = _goChangelogView?.ContainerFromItem(pnItem);
        }

        container ??= FindDemoContainer(card.Id);

        container ??= (_cardsItemsControl?.ContainerFromItem(card)
            ?? _faqLeftItemsControl?.ContainerFromItem(card)
            ?? _faqRightItemsControl?.ContainerFromItem(card)) as Control;

        if (container == null)
        {
            foreach (var toolControl in _toolCardControls)
            {
                container = toolControl.ContainerFromItem(card) as Control;
                if (container != null)
                {
                    break;
                }
            }
        }

        return container;
    }

    private void DeferScrollToCard(InfoCardViewModel card)
    {
        if (_deferredScrollCard != null)
        {
            _deferredScrollCard = card;
            return;
        }

        _deferredScrollCard = card;
        Dispatcher.UIThread.Post(
            () =>
            {
                var pending = _deferredScrollCard;
                _deferredScrollCard = null;
                if (pending != null && ReferenceEquals(_boundViewModel?.SelectedCard, pending))
                {
                    RegisterAllCardContainers();
                    _scrollSpy?.ScrollToSection(pending);
                }
            },
            DispatcherPriority.Loaded);
    }

    private void ScrollToCard(InfoCardViewModel card)
    {
        ExpandCardAndTarget(card);

        EnsureScrollSpy();
        if (_scrollSpy == null)
        {
            return;
        }

        var container = ResolveCardContainer(card);

        if (container == null || container.Bounds.Height <= 0)
        {
            DeferScrollToCard(card);
            return;
        }

        _scrollSpy.RegisterSection(card, container);
        _scrollSpy.ScrollToSection(card);
    }
}
