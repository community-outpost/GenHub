using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Tools.Checksum;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ReplayCrcMatchingHelper"/>.
/// </summary>
public class ReplayCrcMatchingHelperTests
{
    /// <summary>
    /// Verifies that NormalizeCrcHex normalizes strings correctly.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <param name="expected">The expected normalized output.</param>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("0x1a2b3c4d", "1A2B3C4D")]
    [InlineData("0XABCDEF", "ABCDEF")]
    [InlineData("1a2b3c4d", "1A2B3C4D")]
    public void NormalizeCrcHex_NormalizesCorrectly(string? input, string expected)
    {
        var result = ReplayCrcMatchingHelper.NormalizeCrcHex(input);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that retail Zero Hour EXE CRCs are recognized.
    /// </summary>
    [Fact]
    public void IsZeroHourRetailExeCrc_RecognizesFirstDecadeAndSteam()
    {
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailExeCrc(ReplayManagerConstants.RetailZeroHourExeCrcFirstDecade));
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailExeCrc(ReplayManagerConstants.RetailZeroHourExeCrcSteam));
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailExeCrc("0xDEADBEEF"));
    }

    /// <summary>
    /// Verifies that retail Generals EXE CRCs are recognized.
    /// </summary>
    [Fact]
    public void IsGeneralsRetailExeCrc_RecognizesFirstDecadeAndSteam()
    {
        Assert.True(ReplayCrcMatchingHelper.IsGeneralsRetailExeCrc(ReplayManagerConstants.RetailGeneralsExeCrcFirstDecade));
        Assert.True(ReplayCrcMatchingHelper.IsGeneralsRetailExeCrc(ReplayManagerConstants.RetailGeneralsExeCrcSteam));
        Assert.False(ReplayCrcMatchingHelper.IsGeneralsRetailExeCrc("0xDEADBEEF"));
    }

    /// <summary>
    /// Verifies that AreExeCrcsEquivalent matches identical or retail-equivalent CRCs.
    /// </summary>
    [Fact]
    public void AreExeCrcsEquivalent_MatchesExactAndRetailEquivalent()
    {
        Assert.True(ReplayCrcMatchingHelper.AreExeCrcsEquivalent("0x12345678", "0x12345678"));
        Assert.True(ReplayCrcMatchingHelper.AreExeCrcsEquivalent(
            ReplayManagerConstants.RetailZeroHourExeCrcFirstDecade,
            ReplayManagerConstants.RetailZeroHourExeCrcSteam,
            GameType.ZeroHour));
        Assert.False(ReplayCrcMatchingHelper.AreExeCrcsEquivalent("0x12345678", "0x87654321"));
    }

    /// <summary>
    /// Verifies that IsDedicatedToAnotherReplay identifies dedicated profile naming/description patterns.
    /// </summary>
    [Fact]
    public void IsDedicatedToAnotherReplay_IdentifiesReplayMarkers()
    {
        var dedicatedByName = new GameProfile { Name = "ZH (Replay: match1.rep)" };
        var dedicatedByDesc = new GameProfile { Name = "ZH Custom", Description = "[replay:match1.rep] Test profile" };
        var regularProfile = new GameProfile { Name = "Standard Zero Hour", Description = "Clean profile" };

        Assert.True(ReplayCrcMatchingHelper.IsDedicatedToAnotherReplay(dedicatedByName));
        Assert.True(ReplayCrcMatchingHelper.IsDedicatedToAnotherReplay(dedicatedByDesc));
        Assert.False(ReplayCrcMatchingHelper.IsDedicatedToAnotherReplay(regularProfile));
    }

    /// <summary>
    /// Verifies that GetDefaultExecutableName returns the appropriate executable for game types and publishers.
    /// </summary>
    [Fact]
    public void GetDefaultExecutableName_ReturnsExpectedBinaryNames()
    {
        Assert.Equal(
            GameClientConstants.GeneralsExecutable,
            ReplayCrcMatchingHelper.GetDefaultExecutableName(GameType.Generals, null));

        Assert.Equal(
            GameClientConstants.SuperHackersZeroHourExecutable,
            ReplayCrcMatchingHelper.GetDefaultExecutableName(GameType.ZeroHour, PublisherTypeConstants.TheSuperHackers));

        Assert.Equal(
            GameClientConstants.ZeroHourExecutable,
            ReplayCrcMatchingHelper.GetDefaultExecutableName(GameType.ZeroHour, null));
    }

    /// <summary>
    /// Verifies that GetOrCalculateProfileIniCrcAsync delegates to the CRC calculator.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetOrCalculateProfileIniCrcAsync_DelegatesToCalculator()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ReplayCrcHelperTest_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var mockCalculator = new Mock<IGameCrcCalculatorService>();
            mockCalculator
                .Setup(c => c.CalculateIniCrcAsync(
                    tempDir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0xCAFEBABE"));

            var result = await ReplayCrcMatchingHelper.GetOrCalculateProfileIniCrcAsync(
                tempDir,
                GameType.ZeroHour,
                mockCalculator.Object);

            Assert.Equal("0xCAFEBABE", result);
            mockCalculator.Verify(
                c => c.CalculateIniCrcAsync(
                    tempDir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Regression test verifying that modifying a nested INI file returns an updated CRC
    /// through the helper by delegating freshness checks to the calculator.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetOrCalculateProfileIniCrcAsync_WhenNestedIniFileEdited_ReturnsUpdatedCrcAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_IniFreshnessTest_" + Guid.NewGuid().ToString("N"));
        var iniDir = Path.Combine(tempDir, "Data", "INI");
        Directory.CreateDirectory(iniDir);

        try
        {
            ReplayCrcMatchingHelper.ClearCrcCaches();
            var calculator = new GameCrcCalculatorService();
            var iniPath = Path.Combine(iniDir, "GameData.ini");

            await File.WriteAllTextAsync(iniPath, "GameData\r\n  Windowed = Yes\r\nEnd\r\n");
            File.SetLastWriteTimeUtc(iniPath, DateTime.UtcNow.AddMinutes(-10));

            var firstCrc = await ReplayCrcMatchingHelper.GetOrCalculateProfileIniCrcAsync(
                tempDir,
                GameType.ZeroHour,
                calculator);

            Assert.NotNull(firstCrc);

            // Second call with untouched files returns cached result from calculator
            var secondCrc = await ReplayCrcMatchingHelper.GetOrCalculateProfileIniCrcAsync(
                tempDir,
                GameType.ZeroHour,
                calculator);

            Assert.Equal(firstCrc, secondCrc);

            // Modify nested GameData.ini and update timestamp (root directory timestamp remains unchanged)
            await File.WriteAllTextAsync(iniPath, "GameData\r\n  Windowed = No\r\n  MaxFPS = 144\r\nEnd\r\n");
            File.SetLastWriteTimeUtc(iniPath, DateTime.UtcNow);

            var updatedCrc = await ReplayCrcMatchingHelper.GetOrCalculateProfileIniCrcAsync(
                tempDir,
                GameType.ZeroHour,
                calculator);

            Assert.NotNull(updatedCrc);
            Assert.NotEqual(firstCrc, updatedCrc);
        }
        finally
        {
            ReplayCrcMatchingHelper.ClearCrcCaches();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies that PreloadProfileCrcsAsync handles null or empty arguments gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PreloadProfileCrcsAsync_WhenProfilesOrCalculatorNull_CompletesWithoutError()
    {
        var mockCalculator = new Mock<IGameCrcCalculatorService>();

        await ReplayCrcMatchingHelper.PreloadProfileCrcsAsync(null!, mockCalculator.Object);
        await ReplayCrcMatchingHelper.PreloadProfileCrcsAsync([], null!);
        await ReplayCrcMatchingHelper.PreloadProfileCrcsAsync([], mockCalculator.Object);

        mockCalculator.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that PreloadProfileCrcsAsync calculates CRCs for valid profile game clients.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PreloadProfileCrcsAsync_WhenValidProfilesProvided_PreloadsCrcs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var exePath = Path.Combine(tempDir, "generals.exe");
        File.WriteAllText(exePath, "dummy binary");

        try
        {
            var mockCalculator = new Mock<IGameCrcCalculatorService>();
            mockCalculator
                .Setup(c => c.CalculateExeCrcAsync(exePath, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0x12345678"));
            mockCalculator
                .Setup(c => c.CalculateIniCrcAsync(
                    tempDir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0x87654321"));

            var profile = new GameProfile
            {
                Id = "test-profile",
                Name = "Test Profile",
                GameClient = new GameClient
                {
                    Id = "client-1",
                    Name = "ZH Client",
                    ExecutablePath = exePath,
                    GameType = GameType.ZeroHour,
                },
            };

            await ReplayCrcMatchingHelper.PreloadProfileCrcsAsync([profile], mockCalculator.Object);

            mockCalculator.Verify(
                c => c.CalculateExeCrcAsync(exePath, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
                Times.Once);
            mockCalculator.Verify(
                c => c.CalculateIniCrcAsync(
                    tempDir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
    /// <summary>
    /// Verifies that IsZeroHourRetailExeCrc handles hex prefix and case insensitivity.
    /// </summary>
    [Theory]
    [InlineData("0x401D89EA", true)]
    [InlineData("401d89ea", true)]
    [InlineData("0xda2b4b18", true)]
    [InlineData("DA2B4B18", true)]
    [InlineData("0xB9DB8815", false)]
    [InlineData("0xE3DB8319", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsZeroHourRetailExeCrc_HandlesPrefixAndCase(string? crc, bool expected)
    {
        var result = ReplayCrcMatchingHelper.IsZeroHourRetailExeCrc(crc);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that IsZeroHourRetailCompatible accurately differentiates retail vs non-retail clients.
    /// </summary>
    [Fact]
    public void IsZeroHourRetailCompatible_DifferentiatesClients()
    {
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(null));

        var goClient = new GameClient
        {
            Id = "1.000104.generalsonline.gameclient.zerohour",
            Name = "Generals Online",
            PublisherType = "GeneralsOnline",
            GameType = GameType.ZeroHour,
        };
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(goClient));

        var nonRetailClient = new GameClient
        {
            Id = "1.106.communityoutpost.gameclient.zerohour.nonretail",
            Name = "Community Patch 1.06 (Non-Retail)",
            PublisherType = CommunityOutpostConstants.PublisherType,
            GameType = GameType.ZeroHour,
        };
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(nonRetailClient));

        var retailClient = new GameClient
        {
            Id = "1.106.communityoutpost.gameclient.zerohour.retail",
            Name = "Community Patch 1.06 (Retail)",
            PublisherType = CommunityOutpostConstants.PublisherType,
            GameType = GameType.ZeroHour,
        };
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(retailClient));

        var steamClient = new GameClient
        {
            Id = "steam",
            Name = "Command & Conquer Generals Zero Hour (Steam)",
            PublisherType = "Steam",
            GameType = GameType.ZeroHour,
        };
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(steamClient));

        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(
            steamClient,
            new[] { "1.106.communityoutpost.patch.zerohour.nonretail" }));
    }
}
