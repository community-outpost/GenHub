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
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(superHackersClient));
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(superHackersClient));
        Assert.True(ReplayCrcMatchingHelper.IsLegacySuperHackersClient(superHackersClient));
        Assert.True(ReplayCrcMatchingHelper.IsNonRetailEngineClient(superHackersClient));

        var nameOnlySuperHackersClient = new GameClient
        {
            Id = "custom.legacy.client",
            Name = "SuperHackers Build",
            PublisherType = null,
            ExecutablePath = "generalszh.exe",
            GameType = GameType.ZeroHour,
        };
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(nameOnlySuperHackersClient));
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(nameOnlySuperHackersClient));
        Assert.True(ReplayCrcMatchingHelper.IsLegacySuperHackersClient(nameOnlySuperHackersClient));
        Assert.True(ReplayCrcMatchingHelper.IsNonRetailEngineClient(nameOnlySuperHackersClient));

        var theSuperHackersNameClient = new GameClient
        {
            Id = "custom.legacy.client.2",
            Name = "TheSuperHackers Zero Hour",
            PublisherType = null,
            ExecutablePath = "generalszh.exe",
            GameType = GameType.ZeroHour,
        };
        Assert.False(ReplayCrcMatchingHelper.IsZeroHourRetailCompatible(theSuperHackersNameClient));
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(theSuperHackersNameClient));
        Assert.True(ReplayCrcMatchingHelper.IsLegacySuperHackersClient(theSuperHackersNameClient));
        Assert.True(ReplayCrcMatchingHelper.IsNonRetailEngineClient(theSuperHackersNameClient));

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
        Assert.False(ReplayCrcMatchingHelper.IsRetailCompatible(tshZeroHour));
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
        Assert.True(ReplayCrcMatchingHelper.IsRetailExeSha256("8DDE6C990280AC44B4629A664B24BBAF226E629E9C7700234010F198783B6674"));
        Assert.True(ReplayCrcMatchingHelper.IsRetailExeSha256("FF6F78211A014100D8EF6B08BC2F8EDD3D55E99E872DFDB5371776FC5A5D02CE"));
    }

    /// <summary>
    /// Verifies that IsRetailCompatibleAsync preserves retail compatibility even when
    /// game root contains loose files that alter the root INI CRC.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatibleAsync_PreservesRetailCompatibility_WhenRootIniDirty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_DirtyRootTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var exePath = Path.Combine(tempDir, "generals.exe");
        await File.WriteAllTextAsync(exePath, "dummy binary");

        try
        {
            var profile = new GameProfile
            {
                Id = "test-profile",
                Name = "Zero Hour Retail",
                GameClient = new GameClient
                {
                    Id = "1.104.steam.gameclient.zerohour",
                    Name = "Command & Conquer Generals Zero Hour (Steam)",
                    PublisherType = "Steam",
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
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0xAC76387F"));

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
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_ModRootTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var exePath = Path.Combine(tempDir, "mod.exe");
        await File.WriteAllTextAsync(exePath, "dummy binary");

        try
        {
            var profile = new GameProfile
            {
                Id = "test-profile",
                Name = "Custom Mod Profile",
                GameClient = new GameClient
                {
                    Id = "custom.mod.client",
                    Name = "Custom Mod",
                    PublisherType = "Custom",
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
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.CreateSuccess("0xAC76387F"));

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
    /// Verifies that IsRetailCompatibleAsync returns true when the INI CRC matches retail.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task IsRetailCompatibleAsync_WhenIniCrcRetail_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHub_CleanRootTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var exePath = Path.Combine(tempDir, "generals.exe");
        await File.WriteAllTextAsync(exePath, "dummy binary");

        try
        {
            var profile = new GameProfile
            {
                Id = "test-profile",
                Name = "Zero Hour Retail",
                GameClient = new GameClient
                {
                    Id = "1.104.steam.gameclient.zerohour",
                    Name = "Command & Conquer Generals Zero Hour (Steam)",
                    PublisherType = "Steam",
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
}
