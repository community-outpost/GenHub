using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Features.GameProfiles.ViewModels;
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
    /// Verifies that CreateDemoAddLocalContent returns a populated DemoAddLocalContentViewModel.
    /// </summary>
    [Fact]
    public void CreateDemoAddLocalContent_ReturnsPopulatedDemoViewModel()
    {
        var vm = DemoViewModelFactory.CreateDemoAddLocalContent();

        vm.Should().NotBeNull();
        vm.Should().BeOfType<DemoAddLocalContentViewModel>();
        vm.IsDemoMode.Should().BeTrue();
        vm.ContentName.Should().NotBeNullOrWhiteSpace();
        vm.FileTree.Should().NotBeEmpty();
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
