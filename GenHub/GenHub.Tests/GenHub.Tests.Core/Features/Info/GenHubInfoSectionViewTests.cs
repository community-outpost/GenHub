using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Info;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Info.Views;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Headless view tests for <see cref="GenHubInfoSectionView"/> demo interactions.
/// </summary>
public sealed class GenHubInfoSectionViewTests
{
    /// <summary>
    /// Verifies that clicking a profile settings demo tab navigates to the mapped info section without crashing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task DemoSettingsTabClick_NavigatesToSectionAsync()
    {
        var vm = await CreateInitializedViewModelAsync();
        var view = new GenHubInfoSectionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 900 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var settingsDemo = vm.DemoGameSettings_SettingsTab.Should().BeOfType<DemoGameProfileSettingsViewModel>().Subject;
            var contentDemo = vm.DemoGameSettings_ContentTab.Should().BeOfType<DemoGameProfileSettingsViewModel>().Subject;
            var profileDemo = vm.DemoGameSettings_ProfileTab.Should().BeOfType<DemoGameProfileSettingsViewModel>().Subject;

            foreach (var (demo, tab, section) in new[]
            {
                (settingsDemo, "0", InfoConstants.SectionGameProfileContent),
                (settingsDemo, "1", InfoConstants.SectionGameProfileSettings),
                (settingsDemo, "2", InfoConstants.SectionGameSettings),
                (contentDemo, "2", InfoConstants.SectionGameSettings),
                (contentDemo, "1", InfoConstants.SectionGameProfileSettings),
                (contentDemo, "0", InfoConstants.SectionGameProfileContent),
                (profileDemo, "0", InfoConstants.SectionGameProfileContent),
                (profileDemo, "2", InfoConstants.SectionGameSettings),
                (profileDemo, "1", InfoConstants.SectionGameProfileSettings),
            })
            {
                demo.SelectTabCommand.Execute(tab);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(400);
                Dispatcher.UIThread.RunJobs();

                vm.SelectedSection.Should().NotBeNull();
                vm.SelectedSection!.Id.Should().Be(section);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that the Tools section renders every actual tool demo view without crashing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ToolsSection_RendersAllActualDemosAsync()
    {
        var vm = await CreateInitializedViewModelAsync();
        var view = new GenHubInfoSectionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 900 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            vm.DemoReplayManager.Should().NotBeNull();
            vm.DemoMapManager.Should().NotBeNull();
            vm.DemoHotkeyEditor.Should().NotBeNull();
            vm.DemoPublisherStudio.Should().NotBeNull();
            vm.DemoModBuilder.Should().NotBeNull();
            vm.DemoWndEditor.Should().NotBeNull();

            vm.NavigateToSectionById(InfoConstants.SectionTools);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(500);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            vm.SelectedSection.Should().NotBeNull();
            vm.SelectedSection!.Id.Should().Be(InfoConstants.SectionTools);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that selecting each per-demo sidebar anchor scrolls to its tool group without crashing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ToolsSection_DemoAnchorsScrollToGroupsAsync()
    {
        var vm = await CreateInitializedViewModelAsync();
        var view = new GenHubInfoSectionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 900 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            vm.NavigateToSectionById(InfoConstants.SectionTools);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(500);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            string[] anchorIds =
            [
                InfoConstants.CardToolsReplayDemo,
                InfoConstants.CardToolsMapDemo,
                InfoConstants.CardToolsHotkeyDemo,
                InfoConstants.CardToolsPublisherDemo,
                InfoConstants.CardToolsModBuilderDemo,
                InfoConstants.CardToolsWndDemo,
            ];
            foreach (var anchorId in anchorIds)
            {
                var anchor = vm.SelectedSection!.Cards.First(c => c.Id == anchorId);
                vm.SelectedCard = anchor;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(250);
                Dispatcher.UIThread.RunJobs();

                vm.SelectedCard.Should().BeSameAs(anchor);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that quick successive clicks on the visible demo panel tab buttons navigate
    /// across profile sections without crashing. Rapid section switches used to overflow the
    /// stack through brush transitions fed by converters that minted a new brush per evaluation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task VisibleDemoTabClicks_QuickSuccession_NavigatesWithoutCrashAsync()
    {
        var vm = await CreateInitializedViewModelAsync();
        var view = new GenHubInfoSectionView { DataContext = vm };
        var window = new Window { Content = view, Width = 1400, Height = 900 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            vm.NavigateToSectionById(InfoConstants.SectionGameProfileSettings);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // Clicks on the VISIBLE demo panel's tab buttons (production click path),
            // pumping layout between clicks but without settling delays.
            foreach (var tabParameter in new[] { "0", "1", "2" })
            {
                var button = FindVisibleDemoTabButton(view, tabParameter);
                button.Should().NotBeNull("tab button {0} must resolve in the visible demo panel", tabParameter);
                button!.Command.Should().NotBeNull("tab button command binding must resolve");
                button.Command!.Execute(button.CommandParameter);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            vm.SelectedSection.Should().NotBeNull();
            vm.SelectedSection!.Id.Should().Be(InfoConstants.SectionGameSettings);
        }
        finally
        {
            window.Close();
        }
    }

    private static Button? FindVisibleDemoTabButton(GenHubInfoSectionView view, string tabParameter)
    {
        foreach (var mock in view.GetLogicalDescendants().OfType<DemoGameProfileSettingsWindowMock>())
        {
            if (!mock.IsVisible || mock.DataContext is not DemoGameProfileSettingsViewModel demo)
            {
                continue;
            }

            var button = mock.GetLogicalDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => ReferenceEquals(b.Command, demo.SelectTabCommand) && Equals(b.CommandParameter?.ToString(), tabParameter));
            if (button != null)
            {
                return button;
            }
        }

        return null;
    }

    private static async Task<GenHubInfoSectionViewModel> CreateInitializedViewModelAsync()
    {
        var gitHubMock = new Mock<IGitHubApiClient>();
        gitHubMock
            .Setup(g => g.GetReleasesAsync("community-outpost", "GenHub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GitHubRelease>
            {
                new() { TagName = "v0.0.3", Name = "GenHub Alpha v0.0.3", PublishedAt = DateTime.UtcNow, Body = "Notes 3" },
                new() { TagName = "v0.0.2", Name = "GenHub Alpha v0.0.2", PublishedAt = DateTime.UtcNow.AddDays(-8), Body = "Notes 2" },
                new() { TagName = "v0.0.1", Name = "GenHub Alpha v0.0.1", PublishedAt = DateTime.UtcNow.AddDays(-8), Body = "Notes 1" },
            });
        var patchNotesMock = new Mock<IGeneralsOnlinePatchNotesService>();
        patchNotesMock
            .Setup(p => p.GetPatchNotesAsync())
            .ReturnsAsync(new List<PatchNote>());

        var vm = new GenHubInfoSectionViewModel(
            new DefaultInfoContentProvider(),
            new ChangelogsViewModel(gitHubMock.Object, Mock.Of<ILogger<ChangelogsViewModel>>()),
            new GeneralsOnlineChangelogViewModel(patchNotesMock.Object, Mock.Of<ILogger<GeneralsOnlineChangelogViewModel>>()));
        await vm.InitializeAsync();
        return vm;
    }
}
