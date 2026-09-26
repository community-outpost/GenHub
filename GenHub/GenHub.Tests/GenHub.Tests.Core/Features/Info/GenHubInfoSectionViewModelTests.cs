using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Models.Info;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for <see cref="GenHubInfoSectionViewModel"/>.
/// </summary>
public class GenHubInfoSectionViewModelTests
{
    private readonly Mock<IInfoContentProvider> _contentProviderMock = new();
    private readonly Mock<IGitHubApiClient> _gitHubMock = new();
    private readonly Mock<ILogger<ChangelogsViewModel>> _changelogLoggerMock = new();
    private readonly Mock<IGeneralsOnlinePatchNotesService> _patchNotesMock = new();
    private readonly Mock<ILogger<GeneralsOnlineChangelogViewModel>> _goLoggerMock = new();
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();

    /// <summary>
    /// Tests that Dispose unsubscribes from ILocalizationService PropertyChanged.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Dispose_UnsubscribesFromLocalizationServiceAsync()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        var titleChanged = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(GenHubInfoSectionViewModel.Title))
            {
                titleChanged = true;
            }
        };

        // Before dispose, event triggers PropertyChanged
        _localizationServiceMock.Raise(
            l => l.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));

        titleChanged.Should().BeTrue();

        // Reset and dispose
        titleChanged = false;
        vm.Dispose();

        _localizationServiceMock.Raise(
            l => l.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));

        titleChanged.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that right card sidebar properties initialize with expected default values.
    /// </summary>
    [Fact]
    public void CardsSidebar_DefaultValues_AreCorrect()
    {
        var vm = CreateViewModel();

        vm.IsCardsPaneOpen.Should().BeTrue();
        vm.CardsOpenPaneLength.Should().Be(SidebarConstants.DefaultOpenPaneLength);
        vm.SelectedCard.Should().BeNull();
    }

    /// <summary>
    /// Verifies that selecting a section initializes SelectedCard to the section's first card.
    /// </summary>
    [Fact]
    public void SelectedSection_Change_UpdatesSelectedCardToFirstCard()
    {
        var vm = CreateViewModel();
        var card1 = new InfoCard { Id = "c1", Title = "Card 1" };
        var card2 = new InfoCard { Id = "c2", Title = "Card 2" };
        var section = new InfoSection
        {
            Id = "sec1",
            Title = "Section 1",
            Cards = new List<InfoCard> { card1, card2 },
        };

        var sectionVm = new InfoSectionViewModel(section, _localizationServiceMock.Object);
        vm.SelectedSection = sectionVm;

        vm.SelectedCard.Should().NotBeNull();
        vm.SelectedCard!.Title.Should().Be("Card 1");
    }

    /// <summary>
    /// Verifies that UpdateCardFromScroll updates SelectedCard without raising ScrollToCardRequested.
    /// </summary>
    [Fact]
    public void UpdateCardFromScroll_UpdatesSelectedCardSilently()
    {
        var vm = CreateViewModel();
        var card1 = new InfoCard { Id = "c1", Title = "Card 1" };
        var card2 = new InfoCard { Id = "c2", Title = "Card 2" };
        var section = new InfoSection
        {
            Id = "sec1",
            Title = "Section 1",
            Cards = new List<InfoCard> { card1, card2 },
        };
        var sectionVm = new InfoSectionViewModel(section, _localizationServiceMock.Object);
        vm.SelectedSection = sectionVm;
        var card2Vm = sectionVm.Cards[1];

        var scrollRequested = false;
        vm.ScrollToCardRequested += _ => scrollRequested = true;

        var propChanged = false;
        vm.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(GenHubInfoSectionViewModel.SelectedCard))
            {
                propChanged = true;
            }
        };

        vm.UpdateCardFromScroll(card2Vm);

        vm.SelectedCard.Should().BeSameAs(card2Vm);
        propChanged.Should().BeTrue();
        scrollRequested.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that UpdateCardFromScroll ignores cards outside the currently selected section.
    /// </summary>
    [Fact]
    public void UpdateCardFromScroll_OutsideSelectedSection_DoesNotUpdateSelectedCard()
    {
        var vm = CreateViewModel();
        var card1 = new InfoCard { Id = "c1", Title = "Card 1" };
        var section = new InfoSection
        {
            Id = "sec1",
            Title = "Section 1",
            Cards = new List<InfoCard> { card1 },
        };
        var sectionVm = new InfoSectionViewModel(section, _localizationServiceMock.Object);
        vm.SelectedSection = sectionVm;

        var outsideCardVm = new InfoCardViewModel(new InfoCard { Id = "other", Title = "Other Card" }, "sec2");
        vm.UpdateCardFromScroll(outsideCardVm);

        vm.SelectedCard.Should().BeSameAs(sectionVm.Cards[0]);
    }

    /// <summary>
    /// Verifies that SelectCardCommand triggers ScrollToCardRequested.
    /// </summary>
    [Fact]
    public void SelectCardCommand_FiresScrollToCardRequested()
    {
        var vm = CreateViewModel();
        var cardVm = new InfoCardViewModel(new InfoCard { Id = "c1", Title = "Card 1" }, "sec1");

        InfoCardViewModel? requestedCard = null;
        vm.ScrollToCardRequested += card => requestedCard = card;

        vm.SelectCardCommand.Execute(cardVm);

        requestedCard.Should().BeSameAs(cardVm);
        vm.SelectedCard.Should().BeSameAs(cardVm);
    }

    private GenHubInfoSectionViewModel CreateViewModel()
    {
        var changelogVm = new ChangelogsViewModel(_gitHubMock.Object, _changelogLoggerMock.Object);
        var goChangelogVm = new GeneralsOnlineChangelogViewModel(_patchNotesMock.Object, _goLoggerMock.Object);

        return new GenHubInfoSectionViewModel(
            _contentProviderMock.Object,
            changelogVm,
            goChangelogVm,
            localizationService: _localizationServiceMock.Object);
    }
}
