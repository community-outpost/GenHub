using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Info.Views;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit and headless UI tests for Info tab responsiveness and demo views.
/// </summary>
public class InfoTabResponsivenessTests
{
    /// <summary>
    /// Verifies that CreateDemoAddLocalContent returns a populated DemoAddLocalContentViewModel with default Mod preset.
    /// </summary>
    [Fact]
    public void CreateDemoAddLocalContent_ReturnsPopulatedDemoViewModel()
    {
        var vm = DemoViewModelFactory.CreateDemoAddLocalContent();

        vm.Should().NotBeNull();
        vm.Should().BeOfType<DemoAddLocalContentViewModel>();
        vm.IsDemoMode.Should().BeTrue();
        vm.ActivePreset.Should().Be(LocalContentDemoPreset.Mod);
        vm.ContentName.Should().Be("ShockWave v1.201");
        vm.SelectedContentType.Should().Be(ContentType.Mod);
        vm.ShowExecutableSelection.Should().BeFalse();
        vm.FileTree.Should().NotBeEmpty();
        vm.CanAdd.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that switching to Game Client preset selects generals.exe and enables executable selection.
    /// </summary>
    [Fact]
    public void DemoAddLocalContent_GameClientPreset_SetsGeneralsExeAsSelectedExecutable()
    {
        var vm = DemoViewModelFactory.CreateDemoAddLocalContent();

        vm.LoadGameClientPreset();

        vm.ActivePreset.Should().Be(LocalContentDemoPreset.GameClient);
        vm.IsGameClientPresetActive.Should().BeTrue();
        vm.ContentName.Should().Be("TheSuperHackers Engine Build (v1.06 Beta)");
        vm.SelectedContentType.Should().Be(ContentType.GameClient);
        vm.ShowExecutableSelection.Should().BeTrue();
        vm.ExecutableCount.Should().Be(1);
        vm.SelectedExecutableItem.Should().NotBeNull();
        vm.SelectedExecutableItem!.Name.Should().Be("generals.exe");
        vm.SelectedExecutableItem.IsSelectedExecutable.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that switching to Modding Tool preset loads GenHotkeys with selectable executables.
    /// </summary>
    [Fact]
    public void DemoAddLocalContent_ModdingToolPreset_HasSelectableExecutables()
    {
        var vm = DemoViewModelFactory.CreateDemoAddLocalContent();

        vm.LoadModdingToolPreset();

        vm.ActivePreset.Should().Be(LocalContentDemoPreset.ModdingTool);
        vm.IsModdingToolPresetActive.Should().BeTrue();
        vm.ContentName.Should().Be("GenHotkeys v2.1");
        vm.SelectedContentType.Should().Be(ContentType.ModdingTool);
        vm.ShowExecutableSelection.Should().BeTrue();
        vm.ExecutableCount.Should().Be(2);
        vm.SelectedExecutableItem.Should().NotBeNull();
        vm.SelectedExecutableItem!.Name.Should().Be("GenHotkeys.exe");
    }

    /// <summary>
    /// Verifies that switching to Executable preset loads WorldBuilder.
    /// </summary>
    [Fact]
    public void DemoAddLocalContent_ExecutablePreset_HasWorldBuilder()
    {
        var vm = DemoViewModelFactory.CreateDemoAddLocalContent();

        vm.LoadExecutablePreset();

        vm.ActivePreset.Should().Be(LocalContentDemoPreset.Executable);
        vm.IsExecutablePresetActive.Should().BeTrue();
        vm.ContentName.Should().Be("WorldBuilder Zero Hour 1.04");
        vm.SelectedContentType.Should().Be(ContentType.Executable);
        vm.ShowExecutableSelection.Should().BeTrue();
        vm.ExecutableCount.Should().Be(1);
        vm.SelectedExecutableItem.Should().NotBeNull();
        vm.SelectedExecutableItem!.Name.Should().Be("WorldBuilder.exe");
    }

    /// <summary>
    /// Verifies that AddContentCommand succeeds in demo mode and updates the status message.
    /// </summary>
    [Fact]
    public async Task DemoAddLocalContent_AddContentCommand_ExecutesSuccessfully()
    {
        var vm = DemoViewModelFactory.CreateDemoAddLocalContent();

        await vm.AddContentCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("registered in library");
        vm.CanAdd.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that Local Content info cards contain real-world details on GenLauncher normalisation,
    /// profile linking, modding tools, and custom engine test builds.
    /// </summary>
    [Fact]
    public void DefaultInfoContentProvider_CreateLocalContentSection_ContainsDetailedUnslopCards()
    {
        var provider = new DefaultInfoContentProvider();
        var sections = provider.GetSections();
        var localContentSection = sections.FirstOrDefault(s => s.Id == InfoConstants.SectionLocalContent);

        localContentSection.Should().NotBeNull();
        localContentSection!.Cards.Should().HaveCount(4);

        var allContent = string.Join(" ", localContentSection.Cards.Select(c => $"{c.Title} {c.Content} {c.DetailedContent}"));

        // Normalisation details
        allContent.Should().Contain("GenLauncher");
        allContent.Should().Contain(".gib");
        allContent.Should().Contain(".big");
        allContent.Should().Contain(".GLR");
        allContent.Should().Contain(".GOF");
        allContent.Should().Contain(".GLTC");

        // Real-world entities
        allContent.Should().Contain("TheSuperHackers");
        allContent.Should().Contain("GenHotkeys");
        allContent.Should().Contain("WorldBuilder");

        // Profile linking and isolation
        allContent.Should().Contain("Profile Settings");
        allContent.Should().Contain("isolated workspace");

        // Verification of slop removal
        allContent.Should().NotContain("Endless Possibilities");
        allContent.Should().NotContain("Universal Import");
        allContent.Should().NotContain("Smart Management");
    }

    /// <summary>
    /// Verifies that ChangelogsView release notes scrollviewer does not unconstrain horizontal layout.
    /// </summary>
    [AvaloniaFact]
    public void ChangelogsView_ReleaseNotes_DoesNotExceedAvailableWidth()
    {
        var gitHubMock = new Mock<IGitHubApiClient>();
        var loggerMock = new Mock<ILogger<ChangelogsViewModel>>();
        var changelogVm = new ChangelogsViewModel(gitHubMock.Object, loggerMock.Object);

        changelogVm.Releases.Add(new GitHubRelease
        {
            TagName = "v1.0.0",
            Name = "Release 1.0.0",
            Body = "This is a very long line of changelog notes that would definitely overflow if horizontal scroll or infinity measure was allowed. " +
                   "It contains many details about features, bugfixes, improvements, and other changes that developers have made to the codebase over the course of the release cycle.",
            Assets = new List<GitHubReleaseAsset>(),
        });

        var view = new ChangelogsView { DataContext = changelogVm };
        var window = new Window { Width = 700, Height = 800, Content = view };
        window.Show();

        view.DesiredSize.Width.Should().BeLessOrEqualTo(700);
    }
}
