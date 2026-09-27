using FluentAssertions;
using GenHub.Features.Tools.WndEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for <see cref="ChallengeMedalService"/>.
/// </summary>
public sealed class ChallengeMedalServiceTests : IDisposable
{
    private const string ChallengeModeContent =
        "ChallengeGenerals\n" +
        "  GeneralPersona0\n" +
        "    PlayerTemplate = FactionAmericaAirForceGeneral\n" +
        "    StartsEnabled = YES\n" +
        "  End\n" +
        "  GeneralPersona1\n" +
        "    PlayerTemplate = FactionGLAToxinGeneral\n" +
        "    StartsEnabled = NO\n" +
        "  End\n" +
        "  GeneralPersona2\n" +
        "    PlayerTemplate = FactionUnknownGeneral\n" +
        "    StartsEnabled = YES\n" +
        "  End\n" +
        "End\n";

    private const string PlayerTemplateContent =
        "PlayerTemplate FactionAmericaAirForceGeneral\n" +
        "  MedallionRegular = AirGeneral_slvr\n" +
        "End\n" +
        "PlayerTemplate FactionGLAToxinGeneral\n" +
        "  MedallionRegular = ToxinGeneral_slvr\n" +
        "End\n";

    private readonly string _gameRoot;
    private readonly ChallengeMedalService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChallengeMedalServiceTests"/> class.
    /// </summary>
    public ChallengeMedalServiceTests()
    {
        _gameRoot = Path.Combine(Path.GetTempPath(), "GenHub_MedalTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Data", "INI"));
        _service = new ChallengeMedalService(Mock.Of<ILogger<ChallengeMedalService>>());
    }

    /// <summary>
    /// Cleans up the temporary game root.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_gameRoot))
        {
            Directory.Delete(_gameRoot, recursive: true);
        }
    }

    /// <summary>
    /// Tests that personas join medallions with disabled positions hidden.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetMedalsAsync_ZhRoot_ResolvesMedalsAndHidden()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_gameRoot, "Data", "INI", "ChallengeMode.ini"), ChallengeModeContent);
        File.WriteAllText(Path.Combine(_gameRoot, "Data", "INI", "PlayerTemplate.ini"), PlayerTemplateContent);

        // Act
        var result = await _service.GetMedalsAsync(_gameRoot, null, null, null, true);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.MedalsByPosition.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<int, string>(0, "AirGeneral_slvr"));
        result.Data.HiddenPositions.Should().ContainSingle().Which.Should().Be(1);
    }

    /// <summary>
    /// Tests that non-Zero Hour targets resolve no medals.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetMedalsAsync_GeneralsTarget_ReturnsEmpty()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_gameRoot, "Data", "INI", "ChallengeMode.ini"), ChallengeModeContent);
        File.WriteAllText(Path.Combine(_gameRoot, "Data", "INI", "PlayerTemplate.ini"), PlayerTemplateContent);

        // Act
        var result = await _service.GetMedalsAsync(_gameRoot, null, null, null, false);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.MedalsByPosition.Should().BeEmpty();
        result.Data.HiddenPositions.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that missing INI files resolve empty instead of failing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetMedalsAsync_MissingIni_ReturnsEmpty()
    {
        // Act
        var result = await _service.GetMedalsAsync(_gameRoot, null, null, null, true);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.MedalsByPosition.Should().BeEmpty();
        result.Data.HiddenPositions.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that nested persona blocks parse with template and enabled flag.
    /// </summary>
    [Fact]
    public void ParsePersonas_NestedBlocks_Parses()
    {
        // Act
        var personas = ChallengeMedalService.ParsePersonas(ChallengeModeContent);

        // Assert
        personas.Should().HaveCount(3);
        personas[0].PlayerTemplate.Should().Be("FactionAmericaAirForceGeneral");
        personas[0].StartsEnabled.Should().BeTrue();
        personas[1].StartsEnabled.Should().BeFalse();
    }

    /// <summary>
    /// Tests that medallions parse keyed by template name.
    /// </summary>
    [Fact]
    public void ParseMedallions_TemplateBlocks_Parses()
    {
        // Act
        var medallions = ChallengeMedalService.ParseMedallions(PlayerTemplateContent);

        // Assert
        medallions.Should().HaveCount(2);
        medallions["FactionAmericaAirForceGeneral"].Should().Be("AirGeneral_slvr");
    }
}
