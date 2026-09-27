using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Info;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
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
    /// Verifies that UpdateCardFromScroll updates SelectedCard.
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
    /// Verifies that SelectCardCommand sets the selected card.
    /// </summary>
    [Fact]
    public void SelectCardCommand_SetsSelectedCard()
    {
        var vm = CreateViewModel();
        var cardVm = new InfoCardViewModel(new InfoCard { Id = "c1", Title = "Card 1" }, "sec1");

        vm.SelectCardCommand.Execute(cardVm);

        vm.SelectedCard.Should().BeSameAs(cardVm);
    }

    /// <summary>
    /// Verifies that CardsOpenPaneLength dynamically adjusts based on the longest card title.
    /// </summary>
    [Fact]
    public void CardsOpenPaneLength_DynamicallyAdjusts_BasedOnCardTitles()
    {
        var vm = CreateViewModel();
        var shortCard = new InfoCard { Id = "c1", Title = "Short" };
        var sectionShort = new InfoSection
        {
            Id = "sec-short",
            Title = "Short Section",
            Cards = new List<InfoCard> { shortCard },
        };
        vm.SelectedSection = new InfoSectionViewModel(sectionShort, _localizationServiceMock.Object);
        vm.CardsOpenPaneLength.Should().BeInRange(180, 220);

        var longCard = new InfoCard { Id = "c2", Title = "A Very Long Section Card Title That Requires More Sidebar Space" };
        var sectionLong = new InfoSection
        {
            Id = "sec-long",
            Title = "Long Section",
            Cards = new List<InfoCard> { longCard },
        };
        vm.SelectedSection = new InfoSectionViewModel(sectionLong, _localizationServiceMock.Object);
        vm.CardsOpenPaneLength.Should().BeGreaterThan(220);
        vm.CardsOpenPaneLength.Should().BeLessOrEqualTo(380);
    }

    /// <summary>
    /// Verifies that changing the demo width notifies the size preset flags used by the preset highlight.
    /// </summary>
    [Fact]
    public void DemoSettingsWidth_Change_NotifiesDemoSizePresets()
    {
        var vm = CreateViewModel();
        vm.IsDemoSizeStandard.Should().BeTrue();

        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        vm.DemoSettingsWidth = 980;

        vm.IsDemoSizeExpanded.Should().BeTrue();
        vm.IsDemoSizeStandard.Should().BeFalse();
        notified.Should().Contain(nameof(GenHubInfoSectionViewModel.IsDemoSizeCompact));
        notified.Should().Contain(nameof(GenHubInfoSectionViewModel.IsDemoSizeStandard));
        notified.Should().Contain(nameof(GenHubInfoSectionViewModel.IsDemoSizeExpanded));
    }

    /// <summary>
    /// Verifies that reloading changelogs raises a single loaded notification instead of one per streamed item.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReloadChangelogs_RaisesChangelogsLoadedOnceAsync()
    {
        var releases = new List<GitHubRelease>
        {
            new() { TagName = "v1", Name = "R1", PublishedAt = DateTime.UtcNow, Body = "B1" },
            new() { TagName = "v2", Name = "R2", PublishedAt = DateTime.UtcNow.AddDays(-1), Body = "B2" },
        };
        _gitHubMock
            .Setup(g => g.GetReleasesAsync("community-outpost", "GenHub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(releases);
        _contentProviderMock
            .Setup(p => p.GetAllSectionsAsync())
            .ReturnsAsync(new List<InfoSection>
            {
                new() { Id = InfoConstants.SectionChangelogs, Title = "Changelogs", Cards = new List<InfoCard>() },
            });

        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.Changelogs.Releases.Should().HaveCount(2);

        var raised = 0;
        vm.ChangelogsLoaded += () => raised++;

        await vm.Changelogs.LoadChangelogsAsync();

        raised.Should().Be(1);
        var section = vm.Sections.Single(s => s.Id == InfoConstants.SectionChangelogs);
        section.Cards.Should().HaveCount(2);
        section.Cards.Select(c => c.Id).Should().OnlyContain(id => id.StartsWith(InfoConstants.CardChangelogsReleasePrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that the release history overview card stays pinned above release tags in the sidebar.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadChangelogs_PinsOverviewAboveReleaseTagsAsync()
    {
        var now = DateTime.UtcNow;
        var releases = new List<GitHubRelease>
        {
            new() { TagName = "v0.0.1", Name = "GenHub Alpha v0.0.1", PublishedAt = now.AddDays(-2), Body = "B1" },
            new() { TagName = "v0.0.3", Name = "GenHub Alpha v0.0.3", PublishedAt = now, Body = "B3" },
            new() { TagName = "v0.0.2", Name = "GenHub Alpha v0.0.2", PublishedAt = now.AddDays(-1), Body = "B2" },
        };
        _gitHubMock
            .Setup(g => g.GetReleasesAsync("community-outpost", "GenHub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(releases);
        _contentProviderMock
            .Setup(p => p.GetAllSectionsAsync())
            .ReturnsAsync(new List<InfoSection>
            {
                new()
                {
                    Id = InfoConstants.SectionChangelogs,
                    Title = "Changelogs",
                    Cards = new List<InfoCard>
                    {
                        new() { Id = InfoConstants.CardChangelogsOverview, Title = "Release History & Changelogs" },
                        new() { Id = InfoConstants.CardChangelogsUpdates, Title = "Automatic Update Distribution" },
                        new() { Id = InfoConstants.CardChangelogsCompatibility, Title = "Rollbacks & Workspace Stability" },
                    },
                },
            });

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        var section = vm.Sections.Single(s => s.Id == InfoConstants.SectionChangelogs);
        section.Cards.Select(c => c.Id).Should().Equal(
            InfoConstants.CardChangelogsOverview,
            InfoConstants.CardChangelogsReleasePrefix + "v0.0.3",
            InfoConstants.CardChangelogsReleasePrefix + "v0.0.2",
            InfoConstants.CardChangelogsReleasePrefix + "v0.0.1",
            InfoConstants.CardChangelogsUpdates,
            InfoConstants.CardChangelogsCompatibility);
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
