using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameProfiles;
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
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailExeCrc("0x391259B0"));
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailExeCrc("391259B0"));
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
        Assert.True(ReplayCrcMatchingHelper.IsGeneralsRetailExeCrc(ReplayManagerConstants.RetailGeneralsExeCrcEaApp));
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
    /// Returns the default executable name for a given game version and publisher.
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
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
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
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
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
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
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
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
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
    /// <param name="crc">The CRC hex string to test.</param>
    /// <param name="expected">Expected compatibility result.</param>
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
            Version = "1.04",
            GameType = GameType.ZeroHour,
        };
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(steamClient));

        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(
            steamClient,
            new[] { "1.106.communityoutpost.patch.zerohour.nonretail" }));

        var generalsXClient = new GameClient
        {
            Id = "1.100.fbraz3.gameclient.generalsxlinuxgeneralsxzh",
            Name = "GeneralsXLinux-GeneralsXZH",
            PublisherType = "github",
            ExecutablePath = "Linux-GeneralsXZH.flatpak",
            GameType = GameType.ZeroHour,
        };
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(generalsXClient));
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(generalsXClient));
        Assert.True(ReplayCrcMatchingHelper.IsGeneralsXClient(generalsXClient));
        Assert.True(ReplayCrcMatchingHelper.IsNonRetailEngineClient(generalsXClient));

        var communityFlatpakClient = new GameClient
        {
            Id = "community.linux.client",
            Name = "Community Linux Client",
            PublisherType = PublisherTypeConstants.Community,
            ExecutablePath = "game.flatpak",
            GameType = GameType.ZeroHour,
        };
        Assert.False(ReplayCrcMatchingHelper.IsGeneralsXClient(communityFlatpakClient));
        Assert.True(ReplayCrcMatchingHelper.IsNonRetailEngineClient(communityFlatpakClient));

        var superHackersClient = new GameClient
        {
            Id = "1.100.thesuperhackers.gameclient.zerohour",
            Name = "TheSuperHackers Zero Hour",
            PublisherType = PublisherTypeConstants.TheSuperHackers,
            ExecutablePath = "generalszh.exe",
            GameType = GameType.ZeroHour,
        };
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(superHackersClient));
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(superHackersClient));
        Assert.True(ReplayCrcMatchingHelper.IsSuperHackersRetailClient(superHackersClient));
        Assert.False(ReplayCrcMatchingHelper.IsLegacySuperHackersClient(superHackersClient));
        Assert.False(ReplayCrcMatchingHelper.IsNonRetailEngineClient(superHackersClient));

        var nameOnlySuperHackersClient = new GameClient
        {
            Id = "custom.legacy.client",
            Name = "SuperHackers Build",
            PublisherType = null,
            ExecutablePath = "generalszh.exe",
            GameType = GameType.ZeroHour,
        };
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(nameOnlySuperHackersClient));
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(nameOnlySuperHackersClient));
        Assert.True(ReplayCrcMatchingHelper.IsSuperHackersRetailClient(nameOnlySuperHackersClient));
        Assert.False(ReplayCrcMatchingHelper.IsLegacySuperHackersClient(nameOnlySuperHackersClient));
        Assert.False(ReplayCrcMatchingHelper.IsNonRetailEngineClient(nameOnlySuperHackersClient));

        var theSuperHackersNameClient = new GameClient
        {
            Id = "custom.legacy.client.2",
            Name = "TheSuperHackers Zero Hour",
            PublisherType = null,
            ExecutablePath = "generalszh.exe",
            GameType = GameType.ZeroHour,
        };
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(theSuperHackersNameClient));
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(theSuperHackersNameClient));
        Assert.True(ReplayCrcMatchingHelper.IsSuperHackersRetailClient(theSuperHackersNameClient));
        Assert.False(ReplayCrcMatchingHelper.IsLegacySuperHackersClient(theSuperHackersNameClient));
        Assert.False(ReplayCrcMatchingHelper.IsNonRetailEngineClient(theSuperHackersNameClient));

        var superHackersNonRetailClient = new GameClient
        {
            Id = "1.100.thesuperhackers.gameclient.zerohour.nonret",
            Name = "TheSuperHackers Zero Hour (Non-Retail)",
            PublisherType = PublisherTypeConstants.TheSuperHackers,
            ExecutablePath = "generalszh.exe",
            GameType = GameType.ZeroHour,
        };
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(superHackersNonRetailClient));
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(superHackersNonRetailClient));
        Assert.False(ReplayCrcMatchingHelper.IsSuperHackersRetailClient(superHackersNonRetailClient));
        Assert.True(ReplayCrcMatchingHelper.IsLegacySuperHackersClient(superHackersNonRetailClient));
        Assert.True(ReplayCrcMatchingHelper.IsNonRetailEngineClient(superHackersNonRetailClient));

        var elfClient = new GameClient
        {
            Id = "custom-linux-zh",
            Name = "Custom Linux Zero Hour",
            PublisherType = "custom",
            ExecutablePath = "/usr/bin/generalszh",
            GameType = GameType.ZeroHour,
        };
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(elfClient));
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(elfClient));
        Assert.True(ReplayCrcMatchingHelper.HasNonRetailExecutableFormat(elfClient));
        Assert.True(ReplayCrcMatchingHelper.IsNonRetailEngineClient(elfClient));

        // Null guards
        Assert.False(ReplayCrcMatchingHelper.IsNonRetailEngineClient(null));
        Assert.False(ReplayCrcMatchingHelper.IsGeneralsOnlineClient(null));
        Assert.False(ReplayCrcMatchingHelper.IsGeneralsXClient(null));
        Assert.False(ReplayCrcMatchingHelper.IsLegacySuperHackersClient(null));

        // Mixed: non-retail binary format or engine fork combined with CO name
        var coElfClient = new GameClient
        {
            Id = "community-outpost-linux-zh",
            Name = "Community Patch 1.06 (Linux)",
            PublisherType = PublisherTypeConstants.CommunityOutpost,
            ExecutablePath = "/usr/bin/generalszh",
            GameType = GameType.ZeroHour,
        };
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(coElfClient));
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(coElfClient));
        Assert.True(ReplayCrcMatchingHelper.IsNonRetailEngineClient(coElfClient));
    }

    /// <summary>
    /// Verifies that Steam launch eligibility requires Steam installation and a Windows PE executable format.
    /// </summary>
    [Fact]
    public void IsSteamLaunchEligible_EvaluatesInstallationTypeAndBinaryFormatCorrectly()
    {
        var windowsClient = new GameClient
        {
            Id = "win-zh",
            Name = "Retail Zero Hour",
            PublisherType = "retail",
            ExecutablePath = "generals.exe",
            GameType = GameType.ZeroHour,
        };

        var nonRetailClient = new GameClient
        {
            Id = "flatpak-zh",
            Name = "Flatpak Zero Hour",
            PublisherType = "flatpak",
            ExecutablePath = "com.fbraz3.GeneralsXZH.flatpakref",
            GameType = GameType.ZeroHour,
        };

        // Client overloads
        Assert.True(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.Steam, windowsClient));
        Assert.False(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.Steam, nonRetailClient));
        Assert.False(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.EaApp, windowsClient));
        Assert.False(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.CDISO, windowsClient));

        // Bool flag overloads
        Assert.True(ReplayCrcMatchingHelper.IsSteamLaunchEligible(true, windowsClient));
        Assert.False(ReplayCrcMatchingHelper.IsSteamLaunchEligible(true, nonRetailClient));
        Assert.False(ReplayCrcMatchingHelper.IsSteamLaunchEligible(false, windowsClient));

        // Executable path overload
        Assert.True(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.Steam, "generals.exe"));
        Assert.True(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.Steam, "Game.dat"));
        Assert.False(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.Steam, "generals.flatpakref"));
        Assert.False(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.Steam, "/usr/bin/generalszh"));
        Assert.False(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.EaApp, "generals.exe"));
    }

    /// <summary>
    /// Verifies that official base clients for Generals (1.08 EA App, 1.09 Steam) and Zero Hour (1.04, 1.05)
    /// are consistently recognized as retail compatible, while non-retail clients like Generals Online are not.
    /// </summary>
    [Fact]
    public void IsRetailCompatible_OfficialBaseClients_AreRetailCompatible()
    {
        // Generals 1.08 EA App with known raw checksum 0x8F98E20A
        var eaGenerals = new GameClient
        {
            Id = "1.108.ea.gameclient.generals",
            Name = "Command & Conquer Generals (EA)",
            PublisherType = "EA",
            GameType = GameType.Generals,
            Version = "1.08",
        };
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(eaGenerals));
        Assert.True(ReplayCrcMatchingHelper.IsGeneralsRetailExeCrc(ReplayManagerConstants.RetailGeneralsExeCrcEaApp));

        // Generals 1.09 Steam
        var steamGenerals = new GameClient
        {
            Id = "1.109.steam.gameclient.generals",
            Name = "Command & Conquer Generals (Steam)",
            PublisherType = "Steam",
            GameType = GameType.Generals,
            Version = "1.09",
        };
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(steamGenerals));

        // Zero Hour 1.05
        var zh105 = new GameClient
        {
            Id = "1.105.communityoutpost.gameclient.zerohour",
            Name = "Command & Conquer Generals Zero Hour 1.05",
            PublisherType = "communityoutpost",
            GameType = GameType.ZeroHour,
            Version = "1.05",
        };
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(zh105));
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(zh105));

        // Steam Zero Hour 1.05 with Game.dat
        var steamZh105 = new GameClient
        {
            Id = "1.105.steam.gameclient.zerohour",
            Name = "Command & Conquer Generals Zero Hour (Steam)",
            PublisherType = "Steam",
            ExecutablePath = "Game.dat",
            GameType = GameType.ZeroHour,
            Version = "1.05",
        };
        Assert.False(ReplayCrcMatchingHelper.HasNonRetailExecutableFormat(steamZh105));
        Assert.False(ReplayCrcMatchingHelper.IsNonRetailEngineClient(steamZh105));
        Assert.True(ReplayCrcMatchingHelper.IsOfficialBaseClient(steamZh105));
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(steamZh105));
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(steamZh105));
        Assert.True(ReplayCrcMatchingHelper.IsSteamLaunchEligible(GameInstallationType.Steam, steamZh105));

        // Generals Online (must be non-retail)
        var goClient = new GameClient
        {
            Id = "1.828261.generalsonline.gameclient.zerohour",
            Name = "Generals Online",
            PublisherType = "generalsonline",
            GameType = GameType.ZeroHour,
            Version = "1.828261",
        };
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(goClient));
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(goClient));

        // EA App auto-created Zero Hour profile (v1.04)
        var eaZeroHourWithV = new GameClient
        {
            Id = "1.104.ea.gameclient.zerohour",
            Name = "zerohour",
            PublisherType = "EA App",
            GameType = GameType.ZeroHour,
            Version = "v1.04",
        };
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(eaZeroHourWithV));

        // EA App auto-created Generals profile (v1.08)
        var eaGeneralsWithV = new GameClient
        {
            Id = "1.108.ea.gameclient.generals",
            Name = "generals",
            PublisherType = "EA App",
            GameType = GameType.Generals,
            Version = "v1.08",
        };
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(eaGeneralsWithV));

        // SuperHackers Zero Hour (The Super Hackers)
        var tshZeroHour = new GameClient
        {
            Id = "1.20260918.thesuperhackers.gameclient.zerohour",
            Name = "SuperHackers - Zero Hour",
            PublisherType = PublisherTypeConstants.TheSuperHackers,
            GameType = GameType.ZeroHour,
            Version = "20260918",
        };
        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatible(tshZeroHour));
        Assert.True(ReplayCrcMatchingHelper.IsSuperHackersRetailClient(tshZeroHour));

        // INI CRC verification
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailIniCrc(ReplayManagerConstants.RetailZeroHourIniCrcVanilla));
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailIniCrc("76B251A3"));
        Assert.True(ReplayCrcMatchingHelper.IsRetailIniCrc("0x76B251A3", GameType.ZeroHour));
        Assert.True(ReplayCrcMatchingHelper.IsGeneralsRetailIniCrc(ReplayManagerConstants.RetailGeneralsIniCrcVanilla));
        Assert.True(ReplayCrcMatchingHelper.IsGeneralsRetailIniCrc(ReplayManagerConstants.RetailGeneralsIniCrcGerman));
        Assert.True(ReplayCrcMatchingHelper.IsGeneralsRetailIniCrc("0x5CB7992C"));
        Assert.True(ReplayCrcMatchingHelper.IsZeroHourRetailExeCrc(ReplayManagerConstants.RetailZeroHourExeCrcCommunityPatch));
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailIniCrc("0x12345678"));
    }

    /// <summary>
    /// Verifies that launcher wrapper SHA256 hashes are recognized as retail compatible.
    /// </summary>
    [Fact]
    public void IsRetailExeSha256_RecognizesLauncherWrappers()
    {
        Assert.True(ReplayCrcMatchingHelper.IsRetailExeSha256(GameClientConstants.EaAppGeneralsLauncherWrapperSha256));
        Assert.True(ReplayCrcMatchingHelper.IsRetailExeSha256(GameClientConstants.ModernLauncherStubSha256));
    }

    /// <summary>
    /// Verifies that IsRetailCompatibleAsync returns false when the calculated INI CRC is dirty / non-retail,
    /// ensuring modified game rules or altered INIs are correctly flagged as non-retail compatible.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatibleAsync_WhenRootIniDirty_ReturnsFalse()
    {
        await RunProfileIniCompatibilityScenarioAsync(
            "1.104.steam.gameclient.zerohour",
            "Command & Conquer Generals Zero Hour (Steam)",
            "Steam",
            "0xAC76387F",
            isRetail => Assert.False(isRetail));
    }

    /// <summary>
    /// Verifies that IsRetailCompatibleAsync recognizes Steam Zero Hour 1.05 with Game.dat as retail compatible.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatibleAsync_SteamZeroHour105WithGameDat_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_AsyncTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var datPath = Path.Combine(tempDir, "Game.dat");
        await File.WriteAllTextAsync(datPath, "dummy steam binary");

        try
        {
            var profile = new GameProfile
            {
                Id = "test-profile-steam-105",
                Name = "Command & Conquer Generals Zero Hour (Steam)",
                GameClient = new GameClient
                {
                    Id = "1.105.steam.gameclient.zerohour",
                    Name = "Command & Conquer Generals Zero Hour (Steam)",
                    PublisherType = "Steam",
                    GameType = GameType.ZeroHour,
                    Version = "1.05",
                    ExecutablePath = datPath,
                },
            };

            var mockCalculator = new Mock<IGameCrcCalculatorService>();
            mockCalculator
                .Setup(c => c.CalculateIniCrcAsync(
                    tempDir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess(ReplayManagerConstants.RetailZeroHourIniCrcVanilla));

            var isRetail = await ReplayCrcMatchingHelper.IsRetailCompatibleAsync(profile, mockCalculator.Object);
            Assert.True(isRetail);
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
    /// Verifies that IsRetailCompatibleAsync returns false for custom mod profiles with non-retail INI CRC.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatibleAsync_WhenCustomModAndIniCrcNonRetail_ReturnsFalse()
    {
        await RunProfileIniCompatibilityScenarioAsync(
            "custom.mod.client",
            "Custom Mod",
            "Custom",
            "0xAC76387F",
            isRetail => Assert.False(isRetail));
    }

    /// <summary>
    /// Verifies that IsRetailCompatibleAsync returns true when the INI CRC matches retail.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatibleAsync_WhenIniCrcRetail_ReturnsTrue()
    {
        await RunProfileIniCompatibilityScenarioAsync(
            "1.104.custom.gameclient.zerohour",
            "Command & Conquer Generals Zero Hour (Custom)",
            "Custom",
            ReplayManagerConstants.RetailZeroHourIniCrcVanilla,
            isRetail => Assert.True(isRetail),
            (mock, dir) => mock.Verify(
                c => c.CalculateIniCrcAsync(
                    dir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<CancellationToken>()),
                Times.Once));
    }

    /// <summary>
    /// Verifies that a profile enabling a mod is recognized as non-retail compatible both synchronously and asynchronously.
    /// (Evidence 1: Mods break INI CRC / game balance and must never show retail compatible).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatible_WithModEnabledInProfile_ReturnsFalse()
    {
        var client = new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Command & Conquer Generals Zero Hour (Steam)",
            PublisherType = "Steam",
            GameType = GameType.ZeroHour,
            Version = "1.04",
        };

        var profile = new GameProfile
        {
            Id = "test-profile-with-mod",
            Name = "ZH with Shockwave Mod",
            GameClient = client,
            EnabledContentIds = new List<string>
            {
                "1.0.moddb.mod.shockwave",
                "1.104.steam.gameinstallation.zerohour",
            },
        };

        // Synchronous check on client + enabled content
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(client, profile.EnabledContentIds));

        // Synchronous check on profile
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(profile));

        // Asynchronous check on profile
        var mockCalculator = new Mock<IGameCrcCalculatorService>();
        var isRetail = await ReplayCrcMatchingHelper.IsRetailCompatibleAsync(profile, mockCalculator.Object);
        Assert.False(isRetail);
    }

    /// <summary>
    /// Verifies that an uploaded/local custom Generals executable that does not match retail hashes
    /// is recognized as non-retail compatible, even if an adjacent Game.dat exists in the folder.
    /// (Evidence 2: Uploaded generals.exe with non-retail CRC must not falsely match retail or Game.dat).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatible_WithUploadedCustomNonRetailExe_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_CustomExeTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var customExePath = Path.Combine(tempDir, "generals.exe");
        var adjacentGameDat = Path.Combine(tempDir, "Game.dat");
        await File.WriteAllTextAsync(customExePath, "custom non-retail uploaded generals.exe binary");
        await File.WriteAllTextAsync(adjacentGameDat, "steam adjacent game dat");

        try
        {
            var client = new GameClient
            {
                Id = "1.0.local.gameclient.generalszh",
                Name = "Custom Uploaded Zero Hour Client",
                PublisherType = "Local",
                GameType = GameType.ZeroHour,
                Version = "1.0",
                ExecutablePath = customExePath,
            };

            var profile = new GameProfile
            {
                Id = "test-profile-custom-exe",
                Name = "Custom Executable Profile",
                GameClient = client,
                CustomExecutablePath = customExePath,
                WorkingDirectory = tempDir,
            };

            // Synchronous check on client
            Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(client));

            // Synchronous check on profile
            Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(profile));

            // Asynchronous check with non-retail INI CRC calculator
            var mockCalculator = new Mock<IGameCrcCalculatorService>();
            mockCalculator
                .Setup(c => c.CalculateIniCrcAsync(
                    tempDir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess(ReplayManagerConstants.RetailZeroHourIniCrcVanilla));

            var isRetail = await ReplayCrcMatchingHelper.IsRetailCompatibleAsync(profile, mockCalculator.Object);
            Assert.False(isRetail);
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
    /// Verifies that a profile specifying an inaccessible or non-existent CustomExecutablePath is rejected as non-retail.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatible_WithUnresolvedCustomExecutable_ReturnsFalse()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "GenHub_NonExistent_" + Guid.NewGuid().ToString("N"), "generals.exe");
        var client = new GameClient
        {
            Id = "1.04.ea.game.zerohour",
            Name = "Zero Hour 1.04",
            Version = "1.04",
            PublisherType = PublisherTypeConstants.Ea,
            GameType = GameType.ZeroHour,
        };

        var profile = new GameProfile
        {
            Id = "profile-unresolved-exe",
            Name = "Unresolved Custom Exe Profile",
            GameClient = client,
            CustomExecutablePath = nonExistentPath,
        };

        // Synchronous check must fail
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(profile));

        // Asynchronous check must fail without attempting CRC
        var mockCalculator = new Mock<IGameCrcCalculatorService>();
        var isRetailAsync = await ReplayCrcMatchingHelper.IsRetailCompatibleAsync(profile, mockCalculator.Object);
        Assert.False(isRetailAsync);
    }

    /// <summary>
    /// Verifies that a profile specifying a relative CustomExecutablePath resolves against the working directory.
    /// </summary>
    [Fact]
    public void IsRetailCompatible_WithRelativeCustomExecutable_ResolvesAgainstWorkingDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_RelCustomExe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var exePath = Path.Combine(tempDir, "custom.exe");
        File.WriteAllText(exePath, "non retail binary data");

        try
        {
            var client = new GameClient
            {
                Id = "1.04.ea.game.zerohour",
                Name = "Zero Hour 1.04",
                Version = "1.04",
                PublisherType = PublisherTypeConstants.Ea,
                GameType = GameType.ZeroHour,
                WorkingDirectory = tempDir,
            };

            var profile = new GameProfile
            {
                Id = "profile-relative-exe",
                Name = "Relative Custom Exe Profile",
                GameClient = client,
                WorkingDirectory = tempDir,
                CustomExecutablePath = "custom.exe",
            };

            // Custom exe exists via relative path but is non-retail binary, so it must return false
            Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(profile));
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
    /// Verifies that an official publisher client with empty version metadata is not assumed to be retail.
    /// </summary>
    [Fact]
    public void IsOfficialBaseClient_WithEmptyVersion_ReturnsFalse()
    {
        var client = new GameClient
        {
            Id = "client-empty-ver",
            Name = "Zero Hour Unknown",
            Version = string.Empty,
            PublisherType = PublisherTypeConstants.Ea,
            GameType = GameType.ZeroHour,
        };

        Assert.False(ReplayCrcMatchingHelper.IsOfficialBaseClient(client));
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(client));
    }

    /// <summary>
    /// Verifies that HasNonRetailContent correctly identifies mods and non-retail patches while allowing retail addons.
    /// </summary>
    [Fact]
    public void HasNonRetailContent_DetectsModsAndNonRetailPatches()
    {
        // 5-segment mod ID
        Assert.True(ReplayCrcMatchingHelper.HasNonRetailContent(new[] { "1.0.moddb.mod.shockwave", }));

        // .mod. containing ID
        Assert.True(ReplayCrcMatchingHelper.HasNonRetailContent(new[] { "some.mod.pack", }));

        // Non-retail patch
        Assert.True(ReplayCrcMatchingHelper.HasNonRetailContent(new[] { "1.06.communityoutpost.patch.nonret", }));

        // Retail addon / map should not be flagged as non-retail
        Assert.False(ReplayCrcMatchingHelper.HasNonRetailContent(new[]
        {
            "1.100.local.addon.improvedmenusenglish",
            "1.20260701.genericcatalog.addon.competitivehotkeys",
            "1.103.genericcatalog.addon.l3mcontrolbarresolution1080p",
        }));

        // Null / empty
        Assert.False(ReplayCrcMatchingHelper.HasNonRetailContent(null));
        Assert.False(ReplayCrcMatchingHelper.HasNonRetailContent(Array.Empty<string>()));
    }

    /// <summary>
    /// Verifies that async verification returns false when no verification directory can be
    /// resolved, even for an official client and even when a CRC calculator is available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatibleAsync_WithMissingVerificationDirectory_ReturnsFalse()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), "GenHub_Missing_" + Guid.NewGuid().ToString("N"));
        var client = new GameClient
        {
            Id = "1.104.steam.gameclient.zerohour",
            Name = "Command & Conquer Generals Zero Hour (Steam)",
            PublisherType = PublisherTypeConstants.Steam,
            Version = "1.04",
            GameType = GameType.ZeroHour,
            ExecutablePath = Path.Combine(missingDir, "generals.exe"),
        };

        var profile = new GameProfile
        {
            Id = "test-profile-missing-dir",
            Name = "Missing Directory Profile",
            GameClient = client,
            WorkingDirectory = missingDir,
        };

        var mockCalculator = new Mock<IGameCrcCalculatorService>();
        mockCalculator
            .Setup(c => c.CalculateIniCrcAsync(
                It.IsAny<string>(),
                GameType.ZeroHour,
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(ReplayManagerConstants.RetailZeroHourIniCrcVanilla));

        Assert.False(await ReplayCrcMatchingHelper.IsRetailCompatibleAsync(profile, mockCalculator.Object));
        mockCalculator.Verify(
            c => c.CalculateIniCrcAsync(
                It.IsAny<string>(),
                GameType.ZeroHour,
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a matching live retail INI CRC decides async compatibility even when the
    /// official client carries no version metadata.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatibleAsync_OfficialClientEmptyVersionWithRetailIniCrc_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_AsyncEmptyVer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var exePath = Path.Combine(tempDir, "generals.exe");
        await File.WriteAllTextAsync(exePath, "dummy binary");

        try
        {
            var profile = new GameProfile
            {
                Id = "test-profile-empty-ver",
                Name = "Empty Version Profile",
                GameClient = new GameClient
                {
                    Id = "steam",
                    Name = "Command & Conquer Generals Zero Hour (Steam)",
                    PublisherType = PublisherTypeConstants.Steam,
                    Version = string.Empty,
                    GameType = GameType.ZeroHour,
                    ExecutablePath = exePath,
                },
            };

            var mockCalculator = new Mock<IGameCrcCalculatorService>();
            mockCalculator
                .Setup(c => c.CalculateIniCrcAsync(
                    tempDir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess(ReplayManagerConstants.RetailZeroHourIniCrcVanilla));

            Assert.True(await ReplayCrcMatchingHelper.IsRetailCompatibleAsync(profile, mockCalculator.Object));
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
    /// Verifies that custom executable validation applies to any <see cref="IGameProfile"/>
    /// implementation, not just the concrete <see cref="GameProfile"/> type.
    /// </summary>
    [Fact]
    public void IsRetailCompatible_WithNonGameProfileImplementation_ValidatesCustomExecutable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_FakeProfile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var customExePath = Path.Combine(tempDir, "custom.exe");
        File.WriteAllText(customExePath, "non retail binary data");

        try
        {
            var client = new GameClient
            {
                Id = "1.104.steam.gameclient.zerohour",
                Name = "Command & Conquer Generals Zero Hour (Steam)",
                PublisherType = PublisherTypeConstants.Steam,
                Version = "1.04",
                GameType = GameType.ZeroHour,
                ExecutablePath = Path.Combine(tempDir, "generals.exe"),
            };

            // A skipped custom executable check would report this official client as retail.
            IGameProfile profile = new FakeGameProfile(client, customExePath, tempDir);
            Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(profile));
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
    /// Verifies that profile file-set scoping is forwarded to the CRC calculator.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatibleAsync_WithFileSet_ForwardsScopingToCalculator()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_AsyncFileSet_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var exePath = Path.Combine(tempDir, "generals.exe");
        await File.WriteAllTextAsync(exePath, "dummy binary");

        try
        {
            var profile = new GameProfile
            {
                Id = "test-profile-fileset",
                Name = "File Set Profile",
                GameClient = new GameClient
                {
                    Id = "1.104.steam.gameclient.zerohour",
                    Name = "Command & Conquer Generals Zero Hour (Steam)",
                    PublisherType = PublisherTypeConstants.Steam,
                    Version = "1.04",
                    GameType = GameType.ZeroHour,
                    ExecutablePath = exePath,
                },
            };

            IReadOnlyCollection<string>? capturedAllowed = null;
            IReadOnlyList<string>? capturedOverlays = null;
            var mockCalculator = new Mock<IGameCrcCalculatorService>();
            mockCalculator
                .Setup(c => c.CalculateIniCrcAsync(
                    tempDir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, GameType, IReadOnlyList<string>?, string?, IReadOnlyCollection<string>?, IReadOnlyList<string>?, CancellationToken>(
                    (_, _, _, _, allowed, overlays, _) =>
                    {
                        capturedAllowed = allowed;
                        capturedOverlays = overlays;
                    })
                .ReturnsAsync(OperationResult<string>.CreateSuccess(ReplayManagerConstants.RetailZeroHourIniCrcVanilla));

            var allowed = new[] { "INIZH.big" };
            var overlays = new[] { Path.Combine(tempDir, "Mod.big") };
            Assert.True(await ReplayCrcMatchingHelper.IsRetailCompatibleAsync(
                profile,
                mockCalculator.Object,
                allowedBaseRelativePaths: allowed,
                overlayModPaths: overlays));

            Assert.Same(allowed, capturedAllowed);
            Assert.Same(overlays, capturedOverlays);
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
    /// Verifies that the metadata-only heuristic recognizes an official Steam client without touching the filesystem.
    /// </summary>
    [Fact]
    public void IsRetailCompatibleHeuristic_WithSteamClient_ReturnsTrue()
    {
        var profile = new GameProfile
        {
            Id = "test-profile",
            Name = "Steam Profile",
            GameClient = new GameClient
            {
                Id = "steam",
                Name = "Command & Conquer Generals Zero Hour (Steam)",
                PublisherType = PublisherTypeConstants.Steam,
                Version = "1.04",
                GameType = GameType.ZeroHour,
            },
        };

        Assert.True(ReplayCrcMatchingHelper.IsRetailCompatibleHeuristic(profile));
    }

    /// <summary>
    /// Verifies that the metadata-only heuristic rejects non-retail engine clients and missing profiles.
    /// </summary>
    [Fact]
    public void IsRetailCompatibleHeuristic_WithNonRetailClient_ReturnsFalse()
    {
        var onlineProfile = new GameProfile
        {
            Id = "test-profile",
            Name = "Online Profile",
            GameClient = new GameClient
            {
                Id = "1.000104.generalsonline.gameclient.zerohour",
                Name = "Generals Online",
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                GameType = GameType.ZeroHour,
            },
        };

        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatibleHeuristic(onlineProfile));
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatibleHeuristic(null));
    }

    private static async Task RunProfileIniCompatibilityScenarioAsync(
        string clientId,
        string clientName,
        string publisherType,
        string calculatedIniCrc,
        Action<bool> assertCompatibility,
        Action<Mock<IGameCrcCalculatorService>, string>? verifyCalculator = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_AsyncTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var exePath = Path.Combine(tempDir, "generals.exe");
        await File.WriteAllTextAsync(exePath, "dummy binary");

        try
        {
            var profile = new GameProfile
            {
                Id = "test-profile",
                Name = clientName,
                GameClient = new GameClient
                {
                    Id = clientId,
                    Name = clientName,
                    PublisherType = publisherType,
                    GameType = GameType.ZeroHour,
                    ExecutablePath = exePath,
                },
            };

            var mockCalculator = new Mock<IGameCrcCalculatorService>();
            mockCalculator
                .Setup(c => c.CalculateIniCrcAsync(
                    tempDir,
                    GameType.ZeroHour,
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<string?>(),
                    It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<IReadOnlyList<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess(calculatedIniCrc));

            var isRetail = await ReplayCrcMatchingHelper.IsRetailCompatibleAsync(profile, mockCalculator.Object);
            assertCompatibility(isRetail);
            verifyCalculator?.Invoke(mockCalculator, tempDir);
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
    /// Minimal <see cref="IGameProfile"/> implementation that is not the concrete
    /// <see cref="GameProfile"/> type, used to verify interface-based validation.
    /// </summary>
    private sealed class FakeGameProfile(GameClient gameClient, string? customExecutablePath, string? workingDirectory) : IGameProfile
    {
        public string Id => "fake-profile";

        public string Name => "Fake Profile";

        public GameClient? GameClient => gameClient;

        public string Version => "1.04";

        public string ExecutablePath => string.Empty;

        public string? CustomExecutablePath => customExecutablePath;

        public string? WorkingDirectory => workingDirectory;

        public List<string> EnabledContentIds => [];

        public WorkspaceStrategy? WorkspaceStrategy => null;

        public string BuildInfo { get; set; } = string.Empty;
    }
}
