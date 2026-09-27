using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Info;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for the interactive tool demo view models.
/// </summary>
public class ToolDemoViewModelTests
{
    private const string SampleWndText =
        "FILE_VERSION = 2;\n" +
        "WINDOW\n" +
        "  WINDOWTYPE = USER;\n" +
        "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600;\n" +
        "  NAME = \"Test.wnd:Root\";\n" +
        "END\n";

    /// <summary>
    /// Verifies that the demo factory returns the actual WND editor view model with a parsed sample document.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task CreateDemoWndEditor_ReturnsActualViewModelWithSampleDocument()
    {
        var viewModel = DemoViewModelFactory.CreateDemoWndEditor();

        var loaded = await viewModel.LoadFromTextAsync(SampleWndText, "Test.wnd");

        loaded.Should().BeTrue();
        viewModel.HasDocument.Should().BeTrue();
        viewModel.RootNodes.Should().ContainSingle();
    }

    /// <summary>
    /// Verifies that the demo factory returns the actual ModBuilder view model which initializes without disk access.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task CreateDemoModBuilder_ReturnsActualViewModel()
    {
        var viewModel = DemoViewModelFactory.CreateDemoModBuilder();

        await viewModel.InitializeAsync();

        viewModel.FileManager.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the demo factory returns the actual GenHotkeys view model with a sample profile and real tech tree.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task CreateDemoGenHotkeys_ReturnsActualViewModelWithSampleProfile()
    {
        var viewModel = DemoViewModelFactory.CreateDemoGenHotkeys();

        await viewModel.InitializeAsync();

        viewModel.Profiles.Should().ContainSingle(p => p.Name == "Demo Hotkeys");
        viewModel.SelectedProfile.Should().NotBeNull();
        viewModel.Factions.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that the demo factory returns the actual Publisher Studio view model with a sample project.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task CreateDemoPublisherStudio_ReturnsActualViewModelWithSampleProject()
    {
        var viewModel = DemoViewModelFactory.CreateDemoPublisherStudio();

        viewModel.CurrentProject.Should().NotBeNull();
        viewModel.CurrentProject!.Catalogs.Should().ContainSingle();

        await viewModel.ReloadFromCurrentProjectAsync();

        viewModel.SelectedCatalog.Should().NotBeNull();
        viewModel.ContentLibraryViewModel.Should().NotBeNull();
        viewModel.PublisherProfileViewModel.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the update manager demo ships mock branches and PRs users can subscribe to.
    /// </summary>
    [Fact]
    public void CreateDemoUpdateViewModel_AllowsSubscribingToMockBranchesAndPrs()
    {
        var viewModel = DemoViewModelFactory.CreateDemoUpdateViewModel();

        viewModel.AvailableBranches.Should().Contain("development");
        viewModel.AvailablePullRequests.Should().NotBeEmpty();

        viewModel.SubscribeToBranchCommand.Execute("development");
        viewModel.SubscribedBranch.Should().Be("development");
        viewModel.IsSubscribedToAny.Should().BeTrue();

        viewModel.SubscribeToPrCommand.Execute(viewModel.AvailablePullRequests[0].Number);
        viewModel.SubscribedPr.Should().NotBeNull();
        viewModel.IsSubscribedToAny.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that clicking a settings demo tab switches the tab and requests navigation to the mapped section.
    /// </summary>
    [AvaloniaFact]
    public void DemoSettingsTabClick_SwitchesTabAndRequestsNavigation()
    {
        var viewModel = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_SettingsTab();
        viewModel.SelectedTabIndex.Should().Be(2);

        string? requested = null;
        ((DemoGameProfileSettingsViewModel)viewModel).NavigationRequested = sectionId => requested = sectionId;

        viewModel.SelectTabCommand.Execute("0");
        Dispatcher.UIThread.RunJobs();

        viewModel.SelectedTabIndex.Should().Be(0);
        requested.Should().Be(InfoConstants.SectionGameProfileContent);
    }

    /// <summary>
    /// Verifies that release cards are listed first in the changelog sidebar, matching the demo browser at the top,
    /// and that the main column only renders guide cards.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ChangelogSection_ListsReleasesFirstAndHidesThemFromMainColumn()
    {
        var gitHubMock = new Mock<IGitHubApiClient>();
        gitHubMock
            .Setup(g => g.GetReleasesAsync("community-outpost", "GenHub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GitHubRelease>
            {
                new() { TagName = "v0.0.1", Name = "GenHub Alpha v0.0.1", Body = "First", PublishedAt = new DateTimeOffset(2025, 12, 20, 0, 0, 0, TimeSpan.Zero) },
                new() { TagName = "v0.0.2", Name = "GenHub Alpha v0.0.2", Body = "Second", PublishedAt = new DateTimeOffset(2025, 12, 28, 0, 0, 0, TimeSpan.Zero) },
            });
        var patchNotesMock = new Mock<IGeneralsOnlinePatchNotesService>();
        patchNotesMock.Setup(p => p.GetPatchNotesAsync()).ReturnsAsync(new List<PatchNote>());

        var viewModel = new GenHubInfoSectionViewModel(
            new DefaultInfoContentProvider(),
            new ChangelogsViewModel(gitHubMock.Object, Mock.Of<ILogger<ChangelogsViewModel>>()),
            new GeneralsOnlineChangelogViewModel(patchNotesMock.Object, Mock.Of<ILogger<GeneralsOnlineChangelogViewModel>>()));
        await viewModel.InitializeAsync();

        var section = viewModel.Sections.First(s => s.Id == InfoConstants.SectionChangelogs);
        section.Cards.Should().HaveCount(6);
        section.Cards[0].TargetItem.Should().NotBeNull();
        section.Cards[1].TargetItem.Should().NotBeNull();
        section.Cards[0].Title.Should().Contain("v0.0.2");
        section.Cards[1].Title.Should().Contain("v0.0.1");

        viewModel.SelectedSection = section;
        viewModel.MainColumnCards.Should().HaveCount(4);
        viewModel.MainColumnCards.Should().OnlyContain(c => c.TargetItem == null);
    }

    /// <summary>
    /// Verifies that the Tools section exposes per-tool card groups matching the provider card counts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ToolsSection_ExposesPerToolCardGroups()
    {
        var viewModel = await CreateInitializedViewModelAsync();
        var toolsSection = viewModel.Sections.First(s => s.Id == InfoConstants.SectionTools);
        viewModel.SelectedSection = toolsSection;

        viewModel.ToolsIntroCards.Should().ContainSingle();
        viewModel.ToolsReplayCards.Should().HaveCount(5);
        viewModel.ToolsMapCards.Should().HaveCount(2);
        viewModel.ToolsHotkeyCards.Should().HaveCount(2);
        viewModel.ToolsPublisherCards.Should().HaveCount(4);
        viewModel.ToolsModBuilderCards.Should().HaveCount(2);
        viewModel.ToolsWndCards.Should().HaveCount(3);
        viewModel.IsStandardCardsVisible.Should().BeFalse();
    }

    private static async Task<GenHubInfoSectionViewModel> CreateInitializedViewModelAsync()
    {
        var gitHubMock = new Mock<IGitHubApiClient>();
        gitHubMock
            .Setup(g => g.GetReleasesAsync("community-outpost", "GenHub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GitHubRelease>());
        var patchNotesMock = new Mock<IGeneralsOnlinePatchNotesService>();
        patchNotesMock
            .Setup(p => p.GetPatchNotesAsync())
            .ReturnsAsync(new List<PatchNote>());

        var viewModel = new GenHubInfoSectionViewModel(
            new DefaultInfoContentProvider(),
            new ChangelogsViewModel(gitHubMock.Object, Mock.Of<ILogger<ChangelogsViewModel>>()),
            new GeneralsOnlineChangelogViewModel(patchNotesMock.Object, Mock.Of<ILogger<GeneralsOnlineChangelogViewModel>>()));
        await viewModel.InitializeAsync();
        return viewModel;
    }
}
