using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Results;
using GenHub.Features.Launching.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for publisher launch handlers and registry.
/// </summary>
public sealed class PublisherLaunchHandlerRegistryTests
{
    /// <summary>
    /// Verifies registry resolves matching handler when profile matches.
    /// </summary>
    [Fact]
    public void GetHandler_ReturnsMatchingHandler_WhenProfileMatches()
    {
        // arrange
        var (registry, generalsOnlineHandler, _) = CreateTestRegistry();
        var profile = new GameProfile
        {
            GameClient = new GameClient
            {
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                GameType = GameType.ZeroHour,
            },
        };

        // act
        var handler = registry.GetHandler(profile);

        // assert
        Assert.Same(generalsOnlineHandler, handler);
    }

    /// <summary>
    /// Verifies registry returns default handler when no publisher matches.
    /// </summary>
    [Fact]
    public void GetHandler_ReturnsDefaultHandler_WhenNoSpecificHandlerMatches()
    {
        // arrange
        var (registry, _, defaultHandler) = CreateTestRegistry();
        var profile = new GameProfile
        {
            GameClient = new GameClient
            {
                PublisherType = PublisherTypeConstants.Unknown,
                GameType = GameType.Generals,
            },
        };

        // act
        var handler = registry.GetHandler(profile);

        // assert
        Assert.Same(defaultHandler, handler);
    }

    /// <summary>
    /// Verifies registry finds handler by publisher type string.
    /// </summary>
    [Fact]
    public void GetHandlerByPublisherType_ReturnsHandler_WhenTypeMatchesCaseInsensitively()
    {
        // arrange
        var (registry, generalsOnlineHandler, _) = CreateTestRegistry();

        // act
        var handler = registry.GetHandlerByPublisherType("generalsonline");

        // assert
        Assert.Same(generalsOnlineHandler, handler);
    }

    /// <summary>
    /// Verifies registry returns null for invalid publisher types.
    /// </summary>
    /// <param name="publisherType">The publisher type string under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NonExistentPublisher")]
    public void GetHandlerByPublisherType_ReturnsNull_WhenNotFoundOrEmpty(string? publisherType)
    {
        // arrange
        var defaultHandler = new DefaultPublisherLaunchHandler();
        var registry = new PublisherLaunchHandlerRegistry(
            [defaultHandler],
            NullLogger<PublisherLaunchHandlerRegistry>.Instance);

        // act
        var handler = registry.GetHandlerByPublisherType(publisherType!);

        // assert
        Assert.Null(handler);
    }

    /// <summary>
    /// Verifies constructor throws InvalidOperationException when DefaultPublisherLaunchHandler is not registered.
    /// </summary>
    [Fact]
    public void Constructor_ThrowsInvalidOperationException_WhenDefaultHandlerMissing()
    {
        // arrange
        var settingsMock = new Mock<IGameSettingsService>();
        var goHandler = new GeneralsOnlineLaunchHandler(
            settingsMock.Object,
            NullLogger<GeneralsOnlineLaunchHandler>.Instance);

        // act & assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new PublisherLaunchHandlerRegistry(
                [goHandler],
                NullLogger<PublisherLaunchHandlerRegistry>.Instance));
        Assert.Contains("DefaultPublisherLaunchHandler", ex.Message);
    }

    /// <summary>
    /// Verifies GetAllHandlers returns a defensive copy preventing external mutation.
    /// </summary>
    [Fact]
    public void GetAllHandlers_ReturnsDefensiveCopy()
    {
        // arrange
        var defaultHandler = new DefaultPublisherLaunchHandler();
        var list = new List<IPublisherLaunchHandler> { defaultHandler };
        var registry = new PublisherLaunchHandlerRegistry(
            list,
            NullLogger<PublisherLaunchHandlerRegistry>.Instance);

        // act
        list.Clear();
        var handlers = registry.GetAllHandlers();

        // assert
        Assert.Single(handlers);
        Assert.Same(defaultHandler, handlers[0]);
    }

    /// <summary>
    /// Verifies GetAllHandlers returns all registered handlers.
    /// </summary>
    [Fact]
    public void GetAllHandlers_ReturnsAllRegisteredHandlers()
    {
        // arrange
        var defaultHandler = new DefaultPublisherLaunchHandler();
        var list = new List<IPublisherLaunchHandler> { defaultHandler };
        var registry = new PublisherLaunchHandlerRegistry(
            list,
            NullLogger<PublisherLaunchHandlerRegistry>.Instance);

        // act
        var all = registry.GetAllHandlers();

        // assert
        Assert.Single(all);
        Assert.Same(defaultHandler, all[0]);
    }

    /// <summary>
    /// Verifies Generals Online handler disables camera settings override.
    /// </summary>
    [Fact]
    public void GeneralsOnlineLaunchHandler_SupportsCameraSettingsOverride_ReturnsFalse()
    {
        // arrange
        var settingsMock = new Mock<IGameSettingsService>();
        var handler = new GeneralsOnlineLaunchHandler(
            settingsMock.Object,
            NullLogger<GeneralsOnlineLaunchHandler>.Instance);
        var profile = new GameProfile
        {
            GameClient = new GameClient
            {
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                GameType = GameType.ZeroHour,
            },
        };

        // act & assert
        Assert.False(handler.SupportsCameraSettingsOverride(profile));
    }

    /// <summary>
    /// Verifies default publisher handler supports camera settings override.
    /// </summary>
    [Fact]
    public void DefaultPublisherLaunchHandler_SupportsCameraSettingsOverride_ReturnsTrue()
    {
        // arrange
        var handler = new DefaultPublisherLaunchHandler();
        var profile = new GameProfile();

        // act & assert
        Assert.True(handler.SupportsCameraSettingsOverride(profile));
    }

    /// <summary>
    /// Verifies Generals Online handler synchronizes settings on BeforeLaunchAsync.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GeneralsOnlineLaunchHandler_BeforeLaunchAsync_AppliesSettingsForGeneralsOnlineProfileAsync()
    {
        // arrange
        var existingSettings = new GeneralsOnlineSettings { ShowFps = false };
        var settingsMock = new Mock<IGameSettingsService>();
        settingsMock
            .Setup(x => x.LoadGeneralsOnlineSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GeneralsOnlineSettings>.CreateSuccess(existingSettings));
        settingsMock
            .Setup(x => x.SaveGeneralsOnlineSettingsAsync(It.IsAny<GeneralsOnlineSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var handler = new GeneralsOnlineLaunchHandler(
            settingsMock.Object,
            NullLogger<GeneralsOnlineLaunchHandler>.Instance);

        var profile = new GameProfile
        {
            GameClient = new GameClient
            {
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                GameType = GameType.ZeroHour,
            },
            GoShowFps = true,
        };

        // act
        var result = await handler.BeforeLaunchAsync(profile, CancellationToken.None);

        // assert
        Assert.True(result.Success);
        settingsMock.Verify(
            x => x.SaveGeneralsOnlineSettingsAsync(It.Is<GeneralsOnlineSettings>(s => s.ShowFps), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static (PublisherLaunchHandlerRegistry Registry, GeneralsOnlineLaunchHandler GoHandler, DefaultPublisherLaunchHandler DefaultHandler) CreateTestRegistry(
        Mock<IGameSettingsService>? settingsMock = null)
    {
        var settings = settingsMock ?? new Mock<IGameSettingsService>();
        var goHandler = new GeneralsOnlineLaunchHandler(
            settings.Object,
            NullLogger<GeneralsOnlineLaunchHandler>.Instance);
        var defHandler = new DefaultPublisherLaunchHandler();
        var reg = new PublisherLaunchHandlerRegistry(
            [defHandler, goHandler],
            NullLogger<PublisherLaunchHandlerRegistry>.Instance);

        return (reg, goHandler, defHandler);
    }
}
