using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Features.GameProfiles.Services;
using Moq;
using Xunit;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Tests for <see cref="ContentDisplayFormatter"/>.
/// </summary>
public class ContentDisplayFormatterTests
{
    private readonly ContentDisplayFormatter _formatter;
    private readonly Mock<IGameClientHashRegistry> _hashRegistryMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentDisplayFormatterTests"/> class.
    /// </summary>
    public ContentDisplayFormatterTests()
    {
        _hashRegistryMock = new Mock<IGameClientHashRegistry>();
        _hashRegistryMock.Setup(x => x.GetGameInfoFromHash(It.IsAny<string>())).Returns((GameType.Unknown, GameClientConstants.UnknownVersion));
        _formatter = new ContentDisplayFormatter(_hashRegistryMock.Object);
    }

    /// <summary>
    /// Tests that NormalizeVersion handles various formats.
    /// </summary>
    /// <param name="input">The input version string.</param>
    /// <param name="expected">The expected normalized version.</param>
    [Theory]
    [InlineData("v1.08", "1.08")]
    [InlineData("V1.04", "1.04")]
    [InlineData("1.08", "1.08")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeVersion_HandlesVariousFormats(string? input, string expected)
    {
        // Act
        var result = _formatter.NormalizeVersion(input);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that BuildDisplayName formats correctly for game installations.
    /// </summary>
    [Fact]
    public void BuildDisplayName_GameInstallation_FormatsCorrectly()
    {
        // Arrange
        var gameType = GameType.ZeroHour;
        var version = "1.04";

        // Act
        var result = _formatter.BuildDisplayName(gameType, version);

        // Assert
        Assert.Equal("Zero Hour v1.04", result);
    }

    /// <summary>
    /// Tests that BuildDisplayName formats correctly when a content name is provided.
    /// </summary>
    [Fact]
    public void BuildDisplayName_WithContentName_FormatsCorrectly()
    {
        // Arrange
        var gameType = GameType.Generals;
        var version = "1.08";
        var contentName = "GenTool Mod";

        // Act
        var result = _formatter.BuildDisplayName(gameType, version, contentName);

        // Assert
        Assert.Equal("GenTool Mod v1.08", result);
    }

    /// <summary>
    /// Tests that BuildDisplayName handles empty versions gracefully.
    /// </summary>
    [Fact]
    public void BuildDisplayName_EmptyVersion_HandlesGracefully()
    {
        // Arrange
        var gameType = GameType.ZeroHour;
        var version = string.Empty;

        // Act
        var result = _formatter.BuildDisplayName(gameType, version);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Zero Hour", result);
    }

    /// <summary>
    /// Tests that GetPublisherFromInstallationType returns the correct publisher for each installation type.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <param name="expected">The expected publisher name.</param>
    [Theory]
    [InlineData(GameInstallationType.Steam, "Steam")]
    [InlineData(GameInstallationType.EaApp, "EA App")]
    [InlineData(GameInstallationType.TheFirstDecade, "The First Decade")]
    [InlineData(GameInstallationType.Retail, "Retail Installation")]
    [InlineData(GameInstallationType.Unknown, GameClientConstants.UnknownVersion)]
    public void GetPublisherFromInstallationType_ReturnsCorrectPublisher(GameInstallationType installationType, string expected)
    {
        // Act
        var result = _formatter.GetPublisherFromInstallationType(installationType);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that GetPublisherFromManifest returns the publisher name when publisher info is available.
    /// </summary>
    [Fact]
    public void GetPublisherFromManifest_WithPublisherInfo_ReturnsPublisherName()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.mod.testcontent"),
            Name = "Test Content",
            Version = "1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { Name = "Test Publisher" },
            Files = new List<ManifestFile>(),
        };

        // Act
        var result = _formatter.GetPublisherFromManifest(manifest);

        // Assert
        Assert.Equal("Test Publisher", result);
    }

    /// <summary>
    /// Tests that GetPublisherFromManifest infers the publisher from the manifest name.
    /// </summary>
    /// <param name="manifestName">The manifest name.</param>
    /// <param name="expectedPublisher">The expected publisher name.</param>
    [Theory]
    [InlineData("Steam Zero Hour", "Steam")]
    [InlineData("EA App Generals", "EA App")]
    [InlineData("Origin Content", "EA App")]
    [InlineData("GeneralsOnline Mod", "GeneralsOnline")]
    [InlineData("CNClabs Map Pack", "CNClabs")]
    public void GetPublisherFromManifest_InfersFromName(string manifestName, string expectedPublisher)
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.mod.testcontent"),
            Name = manifestName,
            Version = "1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Files = new List<ManifestFile>(),
        };

        // Act
        var result = _formatter.GetPublisherFromManifest(manifest);

        // Assert
        Assert.Equal(expectedPublisher, result);
    }

    /// <summary>
    /// Tests that GetInstallationTypeFromManifest infers the correct installation type from the manifest name.
    /// </summary>
    /// <param name="manifestName">The manifest name.</param>
    /// <param name="expected">The expected installation type.</param>
    [Theory]
    [InlineData("Steam Zero Hour", GameInstallationType.Steam)]
    [InlineData("EA App Generals", GameInstallationType.EaApp)]
    [InlineData("Origin Content", GameInstallationType.EaApp)]
    [InlineData("TFD Edition", GameInstallationType.TheFirstDecade)]
    [InlineData("Wine Installation", GameInstallationType.Wine)]
    [InlineData("Random Content", GameInstallationType.Retail)]
    public void GetInstallationTypeFromManifest_InfersCorrectType(string manifestName, GameInstallationType expected)
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.gameinstallation.testcontent"),
            Name = manifestName,
            Version = "1.0",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
            Files = new List<ManifestFile>(),
        };

        // Act
        var result = _formatter.GetInstallationTypeFromManifest(manifest);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that CreateDisplayItem creates a correct display item from a manifest.
    /// </summary>
    [Fact]
    public void CreateDisplayItem_FromManifest_CreatesCorrectItem()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.mod.testmod"),
            Name = "Test Mod",
            Version = "v2.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { Name = "Test Publisher" },
            Files = new List<ManifestFile>(),
        };

        // Act
        var result = _formatter.CreateDisplayItem(manifest, isEnabled: true);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.genhub.mod.testmod", result.ManifestId);
        Assert.Contains("Test Mod", result.DisplayName);
        Assert.Contains("2.0", result.DisplayName);
        Assert.Equal(ContentType.Mod, result.ContentType);
        Assert.Equal(GameType.ZeroHour, result.GameType);
        Assert.True(result.IsEnabled);
    }

    /// <summary>
    /// Tests that CreateDisplayItemFromInstallation creates a correct display item from an installation.
    /// </summary>
    [Fact]
    public void CreateDisplayItemFromInstallation_CreatesCorrectItem()
    {
        // Arrange
        var installation = new GameInstallation(@"C:\Games\Generals", GameInstallationType.Steam, null)
        {
            Id = "install-guid",
            HasGenerals = true,
            HasZeroHour = true,
        };

        var gameClient = new GameClient
        {
            Id = "client-id",
            Name = "Zero Hour",
            Version = "1.04",
            GameType = GameType.ZeroHour,
        };

        installation.AvailableGameClients.Add(gameClient);

        var manifestId = ManifestId.Create("1.104.steam.gameinstallation.zerohour");

        // Act
        var result = _formatter.CreateDisplayItemFromInstallation(installation, gameClient, manifestId, isEnabled: true);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.104.steam.gameinstallation.zerohour", result.ManifestId);
        Assert.Contains("Zero Hour", result.DisplayName);
        Assert.Contains("1.04", result.DisplayName);
        Assert.Equal(ContentType.GameInstallation, result.ContentType);
        Assert.Equal(GameType.ZeroHour, result.GameType);
        Assert.Equal(GameInstallationType.Steam, result.InstallationType);
        Assert.Equal("Steam", result.Publisher);
        Assert.True(result.IsEnabled);
    }

    /// <summary>
    /// Tests that GetGameTypeDisplayName returns the correct name for each game type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <param name="useShortName">Whether to use short name.</param>
    /// <param name="expected">The expected display name.</param>
    [Theory]
    [InlineData(GameType.Generals, false, "Command & Conquer: Generals")]
    [InlineData(GameType.ZeroHour, false, "Command & Conquer: Generals Zero Hour")]
    [InlineData(GameType.Generals, true, "Generals")]
    [InlineData(GameType.ZeroHour, true, "Zero Hour")]
    [InlineData(GameType.Unknown, false, GameClientConstants.UnknownVersion)]
    public void GetGameTypeDisplayName_ReturnsCorrectName(GameType gameType, bool useShortName, string expected)
    {
        // Act
        var result = _formatter.GetGameTypeDisplayName(gameType, useShortName);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that GetContentTypeDisplayName returns the correct name for each content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="expected">The expected display name.</param>
    [Theory]
    [InlineData(ContentType.GameInstallation, "Game Installation")]
    [InlineData(ContentType.GameClient, "Game Client")]
    [InlineData(ContentType.Mod, "Modification")]
    [InlineData(ContentType.MapPack, "Map Pack")]
    [InlineData(ContentType.Patch, "Patch")]
    public void GetContentTypeDisplayName_ReturnsCorrectName(ContentType contentType, string expected)
    {
        // Act
        var result = _formatter.GetContentTypeDisplayName(contentType);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that FormatVersion formats the version correctly.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <param name="expected">The expected formatted version.</param>
    [Theory]
    [InlineData("1.08", "v1.08")]
    [InlineData("v1.08", "v1.08")]
    [InlineData("2.0", "v2.0")]
    [InlineData("", "")]
    public void FormatVersion_FormatsCorrectly(string version, string expected)
    {
        // Act
        var result = _formatter.FormatVersion(version);

        // Assert
        Assert.Equal(expected, result);
    }
}
