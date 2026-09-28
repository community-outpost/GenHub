using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Info;
using GenHub.Core.Models.Notifications;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for the interactive tool demo view models.
/// </summary>
public class ToolDemoViewModelTests
{
    /// <summary>
    /// Synchronously forwards progress reports on the caller's thread for deterministic assertions.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> action) : IProgress<T>
    {
        /// <inheritdoc/>
        public void Report(T value) => action(value);
    }

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
    /// Verifies that the demo factory seeds the WND editor with the sample main menu and
    /// presets a zoomed-out canvas so the whole menu fits the embedded demo viewport.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task CreateDemoWndEditor_SeedsMainMenuAndPresetsZoom()
    {
        var viewModel = DemoViewModelFactory.CreateDemoWndEditor();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!viewModel.HasDocument && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        viewModel.HasDocument.Should().BeTrue();
        viewModel.DocumentTitle.Should().Contain("MainMenu.wnd");
        viewModel.Zoom.Should().Be(0.5);
        viewModel.RootNodes.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that the demo factory returns the actual ModBuilder view model with a seeded sample
    /// project, bundle packs, and enabled action commands, without disk access.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task CreateDemoModBuilder_LoadsSampleProjectWithBundles()
    {
        var viewModel = DemoViewModelFactory.CreateDemoModBuilder();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((!viewModel.IsProjectLoaded || viewModel.Bundles.Count == 0) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        viewModel.IsProjectLoaded.Should().BeTrue();
        viewModel.CurrentProject.Should().NotBeNull();
        viewModel.ProjectName.Should().Be("Demo Mod");
        viewModel.FileManager.Should().NotBeNull();
        viewModel.Bundles.Should().HaveCount(3);
        viewModel.Bundles.Select(b => b.Name).Should().Contain(["Core Assets", "Maps Pack", "Movies Archive"]);
        viewModel.FileCount.Should().BeGreaterThan(0);

        viewModel.BuildCommand.CanExecute(null).Should().BeTrue();
        viewModel.SaveProjectCommand.CanExecute(null).Should().BeTrue();
        viewModel.OpenConfigEditorCommand.CanExecute(null).Should().BeTrue();
        viewModel.OpenManifestsCommand.CanExecute(null).Should().BeTrue();

        viewModel.SelectedBundle = viewModel.Bundles[0];
        viewModel.EditBundleCommand.CanExecute(null).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that background demo seeding does not surface a project-created toast.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task CreateDemoModBuilder_SeedingDoesNotNotify()
    {
        var notificationMock = new Mock<INotificationService>();
        var viewModel = DemoViewModelFactory.CreateDemoModBuilder(notificationMock.Object);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((!viewModel.IsProjectLoaded || viewModel.Bundles.Count == 0) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        viewModel.IsProjectLoaded.Should().BeTrue();
        notificationMock.Verify(n => n.Show(It.IsAny<NotificationMessage>()), Times.Never);
        notificationMock.Verify(n => n.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
        notificationMock.Verify(n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
        notificationMock.Verify(n => n.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
        notificationMock.Verify(n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the demo factory returns the actual GenHotkeys view model with a sample profile and real tech tree.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task CreateDemoGenHotkeys_ReturnsActualViewModelWithSampleProfile()
    {
        var viewModel = DemoViewModelFactory.CreateDemoGenHotkeys();

        // The factory seeds the demo in the background on the caller's synchronization
        // context, and InitializeAsync is a no-op while that seeding is in flight,
        // so wait for seeding to finish instead of re-initializing.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (viewModel.Factions.Count == 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

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

        await WaitForPublisherSeedAsync(viewModel);
        await viewModel.ReloadFromCurrentProjectAsync();

        viewModel.SelectedCatalog.Should().NotBeNull();
        viewModel.ContentLibraryViewModel.Should().NotBeNull();
        viewModel.PublisherProfileViewModel.Should().NotBeNull();
        viewModel.PublishShareViewModel.Should().NotBeNull();
        viewModel.IsSetupComplete.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the Publisher Studio demo seeds an authenticated mock hosting provider,
    /// so catalog, hosting, and publish tabs stay usable without network access.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task CreateDemoPublisherStudio_SeedsAuthenticatedMockHosting()
    {
        var viewModel = DemoViewModelFactory.CreateDemoPublisherStudio();

        await WaitForPublisherSeedAsync(viewModel);

        viewModel.PublishShareViewModel.Should().NotBeNull();
        var publish = viewModel.PublishShareViewModel!;
        publish.HostingProviders.Should().ContainSingle();
        publish.SelectedHostingProvider.Should().NotBeNull();
        publish.SelectedHostingProvider!.ProviderId.Should().Be(MockHostingProvider.DemoProviderId);
        publish.IsProviderAuthenticated.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that uploads through the demo hosting provider succeed end to end with progress.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateDemoPublisherStudio_MockUploadSucceedsWithProgress()
    {
        var viewModel = DemoViewModelFactory.CreateDemoPublisherStudio();

        await WaitForPublisherSeedAsync(viewModel);

        var provider = viewModel.PublishShareViewModel!.SelectedHostingProvider!;
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes("{\"demo\":true}"));
        var progress = new List<int>();
        var progressReporter = new SynchronousProgress<int>(progress.Add);
        var result = await provider.UploadFileAsync(payload, "catalog-demo.json", progress: progressReporter);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.PublicUrl.Should().StartWith("https://demo.genhub.local/");
        result.Data.FileSize.Should().BeGreaterThan(0);
        result.Data.Sha256Hash.Should().NotBeNullOrWhiteSpace();

        progress.Should().Contain([0, 100]);
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
    /// Verifies that the overview card stays pinned above release tags in the changelog sidebar,
    /// and that the main column only renders guide cards.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ChangelogSection_PinsOverviewAboveReleasesAndHidesThemFromMainColumn()
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
        section.Cards.Should().HaveCount(5);
        section.Cards[0].Id.Should().Be(InfoConstants.CardChangelogsOverview);
        section.Cards[1].TargetItem.Should().NotBeNull();
        section.Cards[2].TargetItem.Should().NotBeNull();
        section.Cards[1].Title.Should().Contain("v0.0.2");
        section.Cards[2].Title.Should().Contain("v0.0.1");

        viewModel.SelectedSection = section;
        viewModel.MainColumnCards.Should().HaveCount(3);
        viewModel.MainColumnCards.Should().OnlyContain(c => c.TargetItem == null);

        // No redundant interactive demo card: the release browser is the section content,
        // and the overview card already targets it.
        section.Cards.Should().OnlyContain(c => !c.Title.Contains("Interactive Demo"));
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

        // Per-demo sidebar anchors exist in card order but never render inside tool groups.
        string[] anchorIds =
        [
            InfoConstants.CardToolsReplayDemo,
            InfoConstants.CardToolsMapDemo,
            InfoConstants.CardToolsHotkeyDemo,
            InfoConstants.CardToolsPublisherDemo,
            InfoConstants.CardToolsModBuilderDemo,
            InfoConstants.CardToolsWndDemo,
        ];
        toolsSection.Cards.Select(c => c.Id).Should().Contain(anchorIds);
        foreach (var group in new[]
        {
            viewModel.ToolsIntroCards,
            viewModel.ToolsReplayCards,
            viewModel.ToolsMapCards,
            viewModel.ToolsHotkeyCards,
            viewModel.ToolsPublisherCards,
            viewModel.ToolsModBuilderCards,
            viewModel.ToolsWndCards,
        })
        {
            group.Select(c => c.Id).Should().NotContain(anchorIds);
        }
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

    private static async Task WaitForPublisherSeedAsync(PublisherStudioViewModel viewModel)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (viewModel.PublishShareViewModel == null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);

            // Only pump jobs when executing on the UI thread ([AvaloniaFact]).
            // Avoids cross-thread dispatcher pumping that throws PlatformNotSupportedException on headless Linux.
            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.RunJobs();
            }
        }
    }
}
