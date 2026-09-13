using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Tests that the ids offered by the profile content picker resolve to the manifests that
/// installation registration actually pools.
/// </summary>
public class ProfileContentLoaderManifestIdTests
{
    /// <summary>
    /// Verifies that an installation whose client version could not be detected is offered under the
    /// same manifest id that registration pools it as. Registration falls back to the game-type
    /// default version, so a picker that falls back to 0 produces an id resolving to no manifest,
    /// and the profile then fails launch validation.
    /// </summary>
    /// <param name="gameType">The game type under test.</param>
    /// <param name="defaultVersion">The default manifest version registration falls back to.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(GameType.ZeroHour, ManifestConstants.ZeroHourManifestVersion)]
    [InlineData(GameType.Generals, ManifestConstants.GeneralsManifestVersion)]
    public async Task UnknownClientVersion_OffersTheManifestIdRegistrationPoolsAsync(
        GameType gameType, string defaultVersion)
    {
        var installation = BuildInstallation(gameType, clientVersion: string.Empty);
        var loader = BuildLoader(installation);

        var items = await loader.LoadAvailableGameInstallationsAsync();

        var expected = ManifestIdGenerator.GenerateGameInstallationId(
            installation, gameType, GameVersionHelper.NormalizeVersion(defaultVersion));

        var item = Assert.Single(items);
        Assert.Equal(expected, item.ManifestId);
        Assert.Equal(expected, item.Id);
    }

    /// <summary>
    /// Verifies that a detected client version is still used verbatim, so the fallback does not
    /// mask a real version.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectedClientVersion_IsUsedForTheManifestIdAsync()
    {
        var installation = BuildInstallation(GameType.ZeroHour, clientVersion: "1.06");
        var loader = BuildLoader(installation);

        var items = await loader.LoadAvailableGameInstallationsAsync();

        var expected = ManifestIdGenerator.GenerateGameInstallationId(
            installation, GameType.ZeroHour, "1.06");

        Assert.Equal(expected, Assert.Single(items).ManifestId);
    }

    private static GameInstallation BuildInstallation(GameType gameType, string clientVersion)
    {
        var installation = new GameInstallation("C:\\custom-install", GameInstallationType.Custom)
        {
            AvailableGameClients =
            [
                new GameClient { GameType = gameType, Version = clientVersion, Name = "test client" },
            ],
        };

        return installation;
    }

    private static ProfileContentLoader BuildLoader(GameInstallation installation)
    {
        var installations = new Mock<IGameInstallationService>();
        installations
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));

        var formatter = new Mock<IContentDisplayFormatter>();
        formatter.Setup(f => f.GetPublisherFromInstallationType(It.IsAny<GameInstallationType>()))
            .Returns("GenHub Local");
        formatter.Setup(f => f.NormalizeVersion(It.IsAny<string?>()))
            .Returns<string?>(v => v ?? string.Empty);
        formatter.Setup(f => f.BuildDisplayName(It.IsAny<GameType>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns("display");

        return new ProfileContentLoader(
            installations.Object,
            Mock.Of<IContentManifestPool>(),
            formatter.Object,
            Mock.Of<ILogger<ProfileContentLoader>>());
    }
}
