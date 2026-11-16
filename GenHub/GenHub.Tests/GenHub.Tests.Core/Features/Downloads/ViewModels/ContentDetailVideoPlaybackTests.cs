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
    /// to in-app playback in a video context, where the player sniffs the content type.
    /// Without video context the same URL is an image (see screenshot test below).
    /// </summary>
    /// <param name="url">The hosted file URL under test.</param>
    [Theory]
    [InlineData("https://drive.google.com/uc?export=download&id=abc123")]
    [InlineData("https://utfs.io/f/abc123")]
    public void ResolveMediaDisplay_ExtensionlessHostedFileUrl_ReturnsPlayVideo(string url)
    {
        Assert.True(MediaFileHelper.IsExtensionlessHostedFileUrl(url));

        var decision = ContentDetailMediaDisplay.Resolve(url, inVideoContext: true);

        Assert.Equal(MediaDisplayAction.PlayVideo, decision.Action);
        Assert.Equal(url, decision.Url);
    }

    /// <summary>
    /// Verifies that a known image file never goes to the video player, even in a video context.
    /// </summary>
    [Fact]
    public void ResolveMediaDisplay_ImageFileInVideoContext_ReturnsShowImage()
    {
        var decision = ContentDetailMediaDisplay.Resolve("https://cdn.example.com/shot.jpg", inVideoContext: true);

        Assert.Equal(MediaDisplayAction.ShowImage, decision.Action);
    }

    /// <summary>
    /// Verifies that a known video file never goes to the image viewer, even in an image context.
    /// </summary>
    [Fact]
    public void ResolveMediaDisplay_VideoFileInImageContext_ReturnsPlayVideo()
    {
        var decision = ContentDetailMediaDisplay.Resolve("https://cdn.example.com/trailer.mp4", inVideoContext: false);

        Assert.Equal(MediaDisplayAction.PlayVideo, decision.Action);
    }

    /// <summary>
    /// Verifies that a video record whose URL is actually an image file shows as an image.
    /// </summary>
    [Fact]
    public void ResolveMediaDisplay_VideoRecordWithImageUrl_ReturnsShowImage()
    {
        var video = new Video("Shot", null, "https://cdn.example.com/shot.png", null);

        var decision = ContentDetailMediaDisplay.Resolve(video);

        Assert.Equal(MediaDisplayAction.ShowImage, decision.Action);
    }

    /// <summary>
    /// Verifies that an image record pointing at an embed page opens in the browser,
    /// matching the string and video paths instead of rendering a broken image.
    /// </summary>
    [Fact]
    public void ResolveMediaDisplay_ImageRecordWithEmbedUrl_ReturnsOpenExternally()
    {
        var image = new Image("Trailer Page", null, "https://www.youtube.com/watch?v=abc123");

        var decision = ContentDetailMediaDisplay.Resolve(image);

        Assert.Equal(MediaDisplayAction.OpenExternally, decision.Action);
    }

    /// <summary>
    /// Verifies that an unknown bare URL in a video context opens in the browser
    /// instead of showing as an image or handing an HTML page to the player.
    /// </summary>
    [Fact]
    public void ResolveMediaDisplay_UnknownUrlInVideoContext_ReturnsOpenExternally()
    {
        var decision = ContentDetailMediaDisplay.Resolve("https://www.moddb.com/mods/x/videos/y", inVideoContext: true);

        Assert.Equal(MediaDisplayAction.OpenExternally, decision.Action);
    }

    /// <summary>
    /// Verifies that extensionless hosted URLs clicked from an image context
    /// (summary screenshots and preview images are bare strings) resolve to the
    /// image viewer, not the video player. Extensionless hosted files carry no
    /// file type, so they must not be assumed to be videos without video context.
    /// </summary>
    /// <param name="url">The hosted image URL under test.</param>
    [Theory]
    [InlineData("https://drive.google.com/uc?export=download&id=abc123")]
    [InlineData("https://utfs.io/f/abc123")]
    public void ResolveMediaDisplay_ExtensionlessHostedScreenshotUrl_ReturnsShowImage(string url)
    {
        var decision = ContentDetailMediaDisplay.Resolve(url);

        Assert.Equal(MediaDisplayAction.ShowImage, decision.Action);
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
    /// Verifies that clicking an extensionless hosted screenshot in the summary
    /// gallery opens the image viewer, not the video player.
    /// </summary>
    [Fact]
    public void OpenFullScreenMedia_ExtensionlessHostedScreenshot_OpensImageViewer()
    {
        var viewModel = CreateViewModel();
        const string url = "https://utfs.io/f/abc123";

        viewModel.OpenFullScreenMediaCommand.Execute(url);

        Assert.True(viewModel.IsFullScreenMediaOpen);
        Assert.Equal(url, viewModel.FullScreenMediaUrl);
        Assert.False(viewModel.IsVideoPlayerOpen);
        Assert.Null(viewModel.VideoPlayerUrl);
    }

    /// <summary>
    /// Verifies that clicking an extensionless hosted preview video opens the player.
    /// </summary>
    [Fact]
    public void OpenFullScreenVideo_ExtensionlessHostedUrl_OpensInAppPlayer()
    {
        var viewModel = CreateViewModel();
        const string url = "https://utfs.io/f/abc123";

        viewModel.OpenFullScreenVideoCommand.Execute(url);

        Assert.True(viewModel.IsVideoPlayerOpen);
        Assert.Equal(url, viewModel.VideoPlayerUrl);
        Assert.False(viewModel.IsFullScreenMediaOpen);
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

    /// <summary>
    /// Verifies publisher-provided screenshot names replace the generic numbered name.
    /// </summary>
    [Fact]
    public void BuildCatalogImages_PublisherScreenshotTitle_UsesCustomName()
    {
        var catalogItem = new CatalogContentItem
        {
            Id = "test-item",
            Name = "Named Shots",
            Metadata = new ContentRichMetadata
            {
                ScreenshotUrls = ["https://cdn.example.com/a.jpg", "https://cdn.example.com/b.jpg"],
                ScreenshotTitles = new Dictionary<string, string> { ["https://cdn.example.com/b.jpg"] = "Main Menu" },
            },
        };
        var json = JsonSerializer.Serialize(catalogItem);
        var searchResult = new ContentSearchResult
        {
            Id = "named-shots-test",
            Name = "Named Shots",
            ProviderName = "Test",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverMetadata = { [CatalogConstants.CatalogItemJsonMetadataKey] = json },
        };

        var viewModel = CreateViewModel(searchResult);
        viewModel.Initialize();

        Assert.Equal(2, viewModel.Images.Count);
        Assert.Equal("Screenshot 1", viewModel.Images[0].Title);
        Assert.Equal("Main Menu", viewModel.Images[1].Title);
    }

    /// <summary>
    /// Verifies screenshot titles can be set, cleared, and remapped to a hosted URL.
    /// </summary>
    [Fact]
    public void ScreenshotTitles_SetClearAndRemap_RoundTrips()
    {
        var metadata = new ContentRichMetadata();
        metadata.SetScreenshotTitle("media/a.jpg", "  Lobby  ");
        Assert.Equal("Lobby", metadata.GetScreenshotTitle("MEDIA/A.JPG"));

        metadata.RemapScreenshotTitle("media/a.jpg", "https://utfs.io/f/abc");
        Assert.Null(metadata.GetScreenshotTitle("media/a.jpg"));
        Assert.Equal("Lobby", metadata.GetScreenshotTitle("https://utfs.io/f/abc"));

        metadata.SetScreenshotTitle("https://utfs.io/f/abc", "   ");
        Assert.Null(metadata.GetScreenshotTitle("https://utfs.io/f/abc"));
    }

    /// <summary>
    /// Verifies the full-screen viewer navigates next/previous through summary screenshots.
    /// </summary>
    [Fact]
    public void OpenFullScreenMedia_ScreenshotGallery_NavigatesNextAndPrevious()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "gallery-test",
            Name = "Gallery",
            ProviderName = "Test",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        searchResult.ScreenshotUrls.Add("https://cdn.example.com/1.jpg");
        searchResult.ScreenshotUrls.Add("https://cdn.example.com/2.jpg");
        searchResult.ScreenshotUrls.Add("https://cdn.example.com/3.jpg");
        var viewModel = CreateViewModel(searchResult);

        viewModel.OpenFullScreenMediaCommand.Execute("https://cdn.example.com/1.jpg");
        Assert.True(viewModel.IsFullScreenMediaOpen);
        Assert.Equal("1 / 3", viewModel.FullScreenMediaPosition);
        Assert.False(viewModel.HasPreviousFullScreenImage);
        Assert.True(viewModel.HasNextFullScreenImage);

        viewModel.NextFullScreenImageCommand.Execute(null);
        Assert.Equal("https://cdn.example.com/2.jpg", viewModel.FullScreenMediaUrl);
        Assert.Equal("Screenshot 2", viewModel.FullScreenMediaTitle);
        Assert.Equal("2 / 3", viewModel.FullScreenMediaPosition);

        viewModel.NextFullScreenImageCommand.Execute(null);
        viewModel.NextFullScreenImageCommand.Execute(null);
        Assert.Equal("https://cdn.example.com/3.jpg", viewModel.FullScreenMediaUrl);
        Assert.False(viewModel.HasNextFullScreenImage);

        viewModel.PreviousFullScreenImageCommand.Execute(null);
        Assert.Equal("https://cdn.example.com/2.jpg", viewModel.FullScreenMediaUrl);

        viewModel.CloseFullScreenMediaCommand.Execute(null);
        Assert.False(viewModel.IsFullScreenMediaOpen);
        Assert.Equal(string.Empty, viewModel.FullScreenMediaPosition);
    }

    /// <summary>
    /// Verifies gallery navigation never shows a video file as an image.
    /// </summary>
    [Fact]
    public void OpenFullScreenMedia_Gallery_ExcludesVideoFiles()
    {
        var catalogItem = new CatalogContentItem
        {
            Id = "test-item",
            Name = "Mixed Media",
            Metadata = new ContentRichMetadata
            {
                ScreenshotUrls = ["https://cdn.example.com/clip.mp4", "https://cdn.example.com/shot.jpg"],
            },
        };
        var json = JsonSerializer.Serialize(catalogItem);
        var searchResult = new ContentSearchResult
        {
            Id = "mixed-media-test",
            Name = "Mixed Media",
            ProviderName = "Test",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ResolverMetadata = { [CatalogConstants.CatalogItemJsonMetadataKey] = json },
        };
        var viewModel = CreateViewModel(searchResult);
        viewModel.Initialize();
        Assert.Equal(2, viewModel.Images.Count);

        viewModel.OpenFullScreenMediaCommand.Execute(viewModel.Images[1]);

        Assert.True(viewModel.IsFullScreenMediaOpen);
        Assert.Equal("https://cdn.example.com/shot.jpg", viewModel.FullScreenMediaUrl);
        Assert.Equal("1 / 1", viewModel.FullScreenMediaPosition);
        Assert.False(viewModel.HasNextFullScreenImage);
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
