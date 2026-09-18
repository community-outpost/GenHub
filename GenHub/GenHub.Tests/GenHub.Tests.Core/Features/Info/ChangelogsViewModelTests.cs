using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Features.Info.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for <see cref="ChangelogsViewModel"/> and <see cref="ChangelogItemViewModel"/>.
/// </summary>
public sealed class ChangelogsViewModelTests : IDisposable
{
    private readonly Mock<IGitHubApiClient> _gitHubApiClientMock = new();
    private readonly Mock<ILogger<ChangelogsViewModel>> _loggerMock = new();
    private readonly Mock<IConfigurationProviderService> _configProviderMock = new();
    private readonly Mock<ILocalizationService> _localizationServiceMock = new();
    private readonly string _tempCacheDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangelogsViewModelTests"/> class.
    /// </summary>
    public ChangelogsViewModelTests()
    {
        _tempCacheDir = Path.Combine(Path.GetTempPath(), "GenHubChangelogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempCacheDir);
        _configProviderMock.Setup(c => c.GetCachePath()).Returns(_tempCacheDir);
    }

    /// <summary>
    /// Disposes resources used by the unit test fixture.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempCacheDir))
            {
                Directory.Delete(_tempCacheDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in unit tests
        }
    }

    /// <summary>
    /// Tests that releases are loaded and sorted with the first release marked as latest.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadChangelogsAsync_WhenApiReturnsReleases_PopulatesAndMarksFirstAsLatest()
    {
        var sampleReleases = new List<GitHubRelease>
        {
            new() { TagName = "v0.0.4", Name = "Alpha 4", PublishedAt = DateTime.UtcNow, Body = "Notes 4" },
            new() { TagName = "v0.0.3", Name = "Alpha 3", PublishedAt = DateTime.UtcNow.AddDays(-10), Body = "Notes 3" },
        };

        _gitHubApiClientMock
            .Setup(g => g.GetReleasesAsync("community-outpost", "GenHub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleReleases);

        var vm = new ChangelogsViewModel(
            _gitHubApiClientMock.Object,
            _loggerMock.Object,
            _configProviderMock.Object,
            _localizationServiceMock.Object);

        await vm.LoadChangelogsAsync();

        vm.Releases.Should().HaveCount(2);
        vm.Releases[0].Release.TagName.Should().Be("v0.0.4");
        vm.Releases[0].IsLatest.Should().BeTrue();
        vm.Releases[0].IsExpanded.Should().BeTrue();

        vm.Releases[1].Release.TagName.Should().Be("v0.0.3");
        vm.Releases[1].IsLatest.Should().BeFalse();
        vm.Releases[1].IsExpanded.Should().BeFalse();

        vm.IsUsingCachedData.Should().BeFalse();
        vm.HasError.Should().BeFalse();
    }

    /// <summary>
    /// Tests that when GitHub API throws, the view model falls back to local disk cache.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadChangelogsAsync_WhenApiThrows_FallsBackToCachedData()
    {
        var cachedReleases = new List<GitHubRelease>
        {
            new() { TagName = "v0.0.4-cached", Name = "Alpha 4 Cached", PublishedAt = DateTime.UtcNow, Body = "Cached body" },
        };
        var cacheFilePath = Path.Combine(_tempCacheDir, InfoConstants.ChangelogsCacheFileName);
        await File.WriteAllTextAsync(cacheFilePath, JsonSerializer.Serialize(cachedReleases));

        _gitHubApiClientMock
            .Setup(g => g.GetReleasesAsync("community-outpost", "GenHub", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("GitHub unreachable"));

        var vm = new ChangelogsViewModel(
            _gitHubApiClientMock.Object,
            _loggerMock.Object,
            _configProviderMock.Object,
            _localizationServiceMock.Object);

        await vm.LoadChangelogsAsync();

        vm.Releases.Should().HaveCount(1);
        vm.Releases[0].Release.TagName.Should().Be("v0.0.4-cached");
        vm.IsUsingCachedData.Should().BeTrue();
        vm.HasError.Should().BeFalse();
    }

    /// <summary>
    /// Tests that when rate limited and no cache is present, an appropriate rate limit error message is shown.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoadChangelogsAsync_WhenRateLimitedAndNoCache_ShowsRateLimitError()
    {
        _gitHubApiClientMock.Setup(g => g.IsRateLimited).Returns(true);
        _gitHubApiClientMock
            .Setup(g => g.GetReleasesAsync("community-outpost", "GenHub", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var vm = new ChangelogsViewModel(
            _gitHubApiClientMock.Object,
            _loggerMock.Object,
            _configProviderMock.Object,
            _localizationServiceMock.Object);

        await vm.LoadChangelogsAsync();

        vm.Releases.Should().BeEmpty();
        vm.HasError.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("rate limit");
    }

    /// <summary>
    /// Tests that toggling expanded inverts the state.
    /// </summary>
    [Fact]
    public void ChangelogItemViewModel_ToggleExpanded_InvertsState()
    {
        var release = new GitHubRelease { TagName = "v0.0.4", Body = "Test" };
        var item = new ChangelogItemViewModel(release, isLatest: false, _ => { });

        item.IsExpanded.Should().BeFalse();
        item.ToggleExpanded();
        item.IsExpanded.Should().BeTrue();
        item.ToggleExpanded();
        item.IsExpanded.Should().BeFalse();
    }
}
