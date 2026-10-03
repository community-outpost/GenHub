using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Parsers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.ViewModels;

/// <summary>
/// Regression tests for in-app video playback routing in <see cref="ContentDetailViewModel"/>.
/// Direct video files must stream inside GenHub instead of opening an external player.
/// </summary>
public sealed class ContentDetailVideoPlaybackTests
{
    /// <summary>
    /// Verifies that direct video file URLs resolve to in-app playback.
    /// </summary>
    /// <param name="url">The video URL under test.</param>
    [Theory]
    [InlineData("https://cdn.example.com/trailer.mp4")]
    [InlineData("https://cdn.example.com/trailer.webm")]
    [InlineData("https://cdn.example.com/trailer.mov")]
    [InlineData("https://dl.dropboxusercontent.com/s/abc/trailer.mp4")]
    public void ResolveMediaDisplay_DirectVideoFileUrl_ReturnsPlayVideo(string url)
    {
        var decision = ContentDetailMediaDisplay.Resolve(url);

        Assert.Equal(MediaDisplayAction.PlayVideo, decision.Action);
        Assert.Equal(url, decision.Url);
    }

    /// <summary>
    /// Verifies that extensionless hosted file URLs from GenHub media hosting resolve
    /// to in-app playback, where the player sniffs the content type.
    /// </summary>
    /// <param name="url">The hosted file URL under test.</param>
    [Theory]
    [InlineData("https://drive.google.com/uc?export=download&id=abc123")]
    [InlineData("https://utfs.io/f/abc123")]
    public void ResolveMediaDisplay_ExtensionlessHostedFileUrl_ReturnsPlayVideo(string url)
    {
        Assert.True(MediaFileHelper.IsExtensionlessHostedFileUrl(url));

        var decision = ContentDetailMediaDisplay.Resolve(url);

        Assert.Equal(MediaDisplayAction.PlayVideo, decision.Action);
        Assert.Equal(url, decision.Url);
    }

    /// <summary>
    /// Verifies that video platform pages resolve to the system browser.
    /// </summary>
    /// <param name="url">The embed page URL under test.</param>
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123")]
    [InlineData("https://youtu.be/abc123")]
    [InlineData("https://www.youtube-nocookie.com/embed/abc123")]
    [InlineData("https://vimeo.com/123456")]
    [InlineData("https://player.vimeo.com/video/123456")]
    public void ResolveMediaDisplay_EmbedPageUrl_ReturnsOpenExternally(string url)
    {
        var decision = ContentDetailMediaDisplay.Resolve(url);

        Assert.Equal(MediaDisplayAction.OpenExternally, decision.Action);
        Assert.Equal(url, decision.Url);
    }

    /// <summary>
    /// Verifies that YouTube embed URLs are normalized to watch URLs for the browser.
    /// </summary>
    [Fact]
    public void ResolveMediaDisplay_YouTubeEmbedRecord_NormalizesToWatchUrl()
    {
        var video = new Video("Trailer", null, "https://www.youtube.com/embed/abc123?rel=0", "YouTube");

        var decision = ContentDetailMediaDisplay.Resolve(video);

        Assert.Equal(MediaDisplayAction.OpenExternally, decision.Action);
        Assert.Equal($"{ApiConstants.YouTubeWatchUrlPrefix}abc123", decision.Url);
    }

    /// <summary>
    /// Verifies that publisher-uploaded direct video records resolve to in-app playback,
    /// including extensionless hosting URLs that carry no file suffix.
    /// </summary>
    /// <param name="url">The video URL under test.</param>
    [Theory]
    [InlineData("https://dl.dropboxusercontent.com/s/abc/trailer.mp4")]
    [InlineData("https://drive.google.com/uc?export=download&id=abc123")]
    public void ResolveMediaDisplay_DirectVideoRecord_ReturnsPlayVideo(string url)
    {
        var video = new Video("Trailer", "https://cdn.example.com/thumb.jpg", url, null);

        var decision = ContentDetailMediaDisplay.Resolve(video);

        Assert.Equal(MediaDisplayAction.PlayVideo, decision.Action);
        Assert.Equal(url, decision.Url);
    }

    /// <summary>
    /// Verifies that videos without a playable URL fall back to their thumbnail image.
    /// </summary>
    [Fact]
    public void ResolveMediaDisplay_VideoWithoutEmbedUrl_ReturnsShowImage()
    {
        var video = new Video("Trailer", "https://cdn.example.com/thumb.jpg", null, null);

        var decision = ContentDetailMediaDisplay.Resolve(video);

        Assert.Equal(MediaDisplayAction.ShowImage, decision.Action);
        Assert.Equal("https://cdn.example.com/thumb.jpg", decision.Url);
    }

    /// <summary>
    /// Verifies that video records pointing at watch pages or articles resolve to the
    /// system browser instead of handing an HTML page to the in-app player.
    /// </summary>
    /// <param name="url">The watch page URL under test.</param>
    [Theory]
    [InlineData("https://www.moddb.com/mods/starcraft/videos/starcraft-showcase")]
    [InlineData("https://example.com/articles/trailer-roundup")]
    public void ResolveMediaDisplay_WatchPageVideoRecord_ReturnsOpenExternally(string url)
    {
        var video = new Video("Showcase", null, url, null);

        var decision = ContentDetailMediaDisplay.Resolve(video);

        Assert.Equal(MediaDisplayAction.OpenExternally, decision.Action);
        Assert.Equal(url, decision.Url);
    }

    /// <summary>
    /// Verifies that image URLs and records resolve to the image viewer.
    /// </summary>
    /// <param name="url">The image URL under test.</param>
    [Theory]
    [InlineData("https://cdn.example.com/shot.jpg")]
    [InlineData("https://cdn.example.com/shot.png")]
    public void ResolveMediaDisplay_ImageUrl_ReturnsShowImage(string url)
    {
        var decision = ContentDetailMediaDisplay.Resolve(url);

        Assert.Equal(MediaDisplayAction.ShowImage, decision.Action);
        Assert.Equal(url, decision.Url);
    }

    /// <summary>
    /// Verifies that local file references never resolve to a presentation.
    /// </summary>
    /// <param name="url">The local reference under test.</param>
    [Theory]
    [InlineData("file:///C:/media/trailer.mp4")]
    [InlineData("C:\\media\\trailer.mp4")]
    [InlineData(null)]
    public void ResolveMediaDisplay_LocalReference_ReturnsNone(string? url)
    {
        var decision = ContentDetailMediaDisplay.Resolve(url);

        Assert.Equal(MediaDisplayAction.None, decision.Action);
        Assert.Null(decision.Url);
    }

    /// <summary>
    /// Verifies that opening a direct video URL raises the in-app player instead of
    /// the image viewer or an external browser.
    /// </summary>
    [Fact]
    public void OpenFullScreenMedia_DirectVideoUrl_OpensInAppPlayer()
    {
        var viewModel = CreateViewModel();
        const string url = "https://dl.dropboxusercontent.com/s/abc/trailer.mp4";

        viewModel.OpenFullScreenMediaCommand.Execute(url);

        Assert.True(viewModel.IsVideoPlayerOpen);
        Assert.Equal(url, viewModel.VideoPlayerUrl);
        Assert.False(viewModel.IsFullScreenMediaOpen);
        Assert.Null(viewModel.FullScreenMediaUrl);
    }

    /// <summary>
    /// Verifies that opening an image URL still raises the image viewer.
    /// </summary>
    [Fact]
    public void OpenFullScreenMedia_ImageUrl_OpensImageViewer()
    {
        var viewModel = CreateViewModel();
        const string url = "https://cdn.example.com/shot.jpg";

        viewModel.OpenFullScreenMediaCommand.Execute(url);

        Assert.True(viewModel.IsFullScreenMediaOpen);
        Assert.Equal(url, viewModel.FullScreenMediaUrl);
        Assert.False(viewModel.IsVideoPlayerOpen);
    }

    /// <summary>
    /// Verifies that closing the player clears its state so playback stops.
    /// </summary>
    [Fact]
    public void CloseVideoPlayer_ClearsPlayerState()
    {
        var viewModel = CreateViewModel();
        viewModel.OpenFullScreenMediaCommand.Execute("https://cdn.example.com/trailer.mp4");

        viewModel.CloseVideoPlayerCommand.Execute(null);

        Assert.False(viewModel.IsVideoPlayerOpen);
        Assert.False(viewModel.IsVideoPlayerFullscreen);
        Assert.Null(viewModel.VideoPlayerUrl);
        Assert.Null(viewModel.VideoPlayerTitle);
    }

    /// <summary>
    /// Verifies that toggling full screen flips the presentation flag.
    /// </summary>
    [Fact]
    public void ToggleVideoPlayerFullscreen_TogglesState()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.IsVideoPlayerFullscreen);

        viewModel.ToggleVideoPlayerFullscreenCommand.Execute(null);
        Assert.True(viewModel.IsVideoPlayerFullscreen);

        viewModel.ToggleVideoPlayerFullscreenCommand.Execute(null);
        Assert.False(viewModel.IsVideoPlayerFullscreen);
    }

    /// <summary>
    /// Verifies that hosted video files without YouTube posters fall back to screenshots.
    /// </summary>
    [Fact]
    public void BuildCatalogVideos_HostedVideo_FallsBackToScreenshot()
    {
        var catalogItem = new CatalogContentItem
        {
            Id = "test-item",
            Name = "Replay Checkpoint & Takeover",
            Metadata = new ContentRichMetadata
            {
                ScreenshotUrls = ["https://drive.google.com/uc?id=screenshot123"],
                VideoUrls = ["https://drive.google.com/uc?id=video123.mp4"],
            },
        };
        var json = JsonSerializer.Serialize(catalogItem);
        var searchResult = new ContentSearchResult
        {
            Id = "video-playback-test",
            Name = "Playback Test",
            ProviderName = "Test",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverMetadata = { [CatalogConstants.CatalogItemJsonMetadataKey] = json },
        };

        var viewModel = CreateViewModel(searchResult);
        viewModel.Initialize();

        Assert.Single(viewModel.Videos);
        Assert.Equal("https://drive.google.com/uc?id=screenshot123", viewModel.Videos[0].ThumbnailUrl);
        Assert.Equal("https://drive.google.com/uc?id=video123.mp4", viewModel.Videos[0].EmbedUrl);
    }

    /// <summary>
    /// Verifies that hosted video files without screenshots fall back to backdrop artwork.
    /// </summary>
    [Fact]
    public void BuildCatalogVideos_HostedVideo_WithoutScreenshots_FallsBackToBackdrop()
    {
        var catalogItem = new CatalogContentItem
        {
            Id = "test-item",
            Name = "Replay Checkpoint & Takeover",
            Metadata = new ContentRichMetadata
            {
                BackdropUrl = "https://cdn.example.com/backdrop.png",
                VideoUrls = ["https://drive.google.com/uc?id=video123.mp4"],
            },
        };
        var json = JsonSerializer.Serialize(catalogItem);
        var searchResult = new ContentSearchResult
        {
            Id = "video-playback-test",
            Name = "Playback Test",
            ProviderName = "Test",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverMetadata = { [CatalogConstants.CatalogItemJsonMetadataKey] = json },
        };

        var viewModel = CreateViewModel(searchResult);
        viewModel.Initialize();

        Assert.Single(viewModel.Videos);
        Assert.Equal("https://cdn.example.com/backdrop.png", viewModel.Videos[0].ThumbnailUrl);
    }

    /// <summary>
    /// Verifies that YouTube videos still resolve to official YouTube thumbnail posters.
    /// </summary>
    [Fact]
    public void BuildCatalogVideos_YouTubeVideo_UsesYouTubeThumbnail()
    {
        var catalogItem = new CatalogContentItem
        {
            Id = "test-item",
            Name = "YouTube Mod",
            Metadata = new ContentRichMetadata
            {
                ScreenshotUrls = ["https://drive.google.com/uc?id=screenshot123"],
                VideoUrls = ["https://www.youtube.com/watch?v=dQw4w9WgXcQ"],
            },
        };
        var json = JsonSerializer.Serialize(catalogItem);
        var searchResult = new ContentSearchResult
        {
            Id = "video-playback-test",
            Name = "Playback Test",
            ProviderName = "Test",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverMetadata = { [CatalogConstants.CatalogItemJsonMetadataKey] = json },
        };

        var viewModel = CreateViewModel(searchResult);
        viewModel.Initialize();

        Assert.Single(viewModel.Videos);
        Assert.Equal("https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg", viewModel.Videos[0].ThumbnailUrl);
    }

    private static ContentDetailViewModel CreateViewModel(ContentSearchResult? customResult = null)
    {
        var searchResult = customResult ?? new ContentSearchResult
        {
            Id = "video-playback-test",
            Name = "Playback Test",
            ProviderName = "Test",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateByManifestIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.Downloaded);
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult result, CancellationToken _) => result.Id);

        return new ContentDetailViewModel(
            searchResult,
            [],
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ITabProviderRegistry>().Object,
            stateService.Object,
            new Mock<IContentDownloadCoordinator>().Object,
            new Mock<IContentManifestPool>().Object,
            new Mock<ILoggerFactory>().Object,
            new Mock<ILogger<ContentDetailViewModel>>().Object);
    }
}
