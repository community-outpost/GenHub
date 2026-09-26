using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.MapManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.MapManager;

/// <summary>
/// Unit tests for <see cref="MapNameParser"/>, player count extraction, and viewmodel model bindings.
/// </summary>
public sealed class MapNameParserTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly MapNameParser _parser;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapNameParserTests"/> class.
    /// </summary>
    public MapNameParserTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_MapNameParserTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _parser = new MapNameParser(NullLogger<MapNameParser>.Instance);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="MapNameParser.ExtractPlayerCountFromString"/> parses known player-count patterns.
    /// </summary>
    /// <param name="input">The map name to parse.</param>
    /// <param name="expected">The expected player count.</param>
    [Theory]
    [InlineData("(2) Tournament Desert", 2)]
    [InlineData("Tournament Desert [4]", 4)]
    [InlineData("Defcon 6 (6)", 6)]
    [InlineData("Twilight Flame (8)", 8)]
    [InlineData("8 Players Free For All", 8)]
    [InlineData("Tournament Arena 4 Players", 4)]
    [InlineData("Hostile Dawn 1 Player", 1)]
    [InlineData("2-player Showdown", 2)]
    [InlineData("4-players Arena", 4)]
    [InlineData("Winter_Wolf_4p", 4)]
    [InlineData("Desert-2p", 2)]
    [InlineData("Classic_Map 6p", 6)]
    public void ExtractPlayerCountFromString_WithKnownPatterns_ReturnsExpectedCount(string input, int expected)
    {
        var result = MapNameParser.ExtractPlayerCountFromString(input);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that <see cref="MapNameParser.ExtractPlayerCountFromString"/> returns null when no player pattern is present.
    /// </summary>
    /// <param name="input">The map name to parse.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Tournament Desert")]
    [InlineData("Defcon 6")]
    [InlineData("Area 51")]
    public void ExtractPlayerCountFromString_WithNoPlayerPattern_ReturnsNull(string? input)
    {
        var result = MapNameParser.ExtractPlayerCountFromString(input);
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that <see cref="MapNameParser.ParsePlayerCount"/> reads the player count from map file content.
    /// </summary>
    [Fact]
    public void ParsePlayerCount_FromFileWithNumPlayers_ReturnsParsedValue()
    {
        var mapFile = Path.Combine(_tempDirectory, "test_map.map");
        var content = """
            Map
              displayName = "Custom Tournament"
              numPlayers = 4
            End
            """;
        File.WriteAllText(mapFile, content);

        var result = _parser.ParsePlayerCount(mapFile);
        Assert.Equal(4, result);
    }

    /// <summary>
    /// Verifies that <see cref="MapNameParser.ParsePlayerCount"/> derives the player count from waypoint slots.
    /// </summary>
    [Fact]
    public void ParsePlayerCount_FromFileWithWaypoints_ReturnsMaxSlot()
    {
        var mapFile = Path.Combine(_tempDirectory, "waypoint_map.map");
        var content = """
            Objects
              Waypoint
                name = Player_1_Start
              End
              Waypoint
                name = Player_2_Start
              End
              Waypoint
                name = Player_3_Start
              End
            End
            """;
        File.WriteAllText(mapFile, content);

        var result = _parser.ParsePlayerCount(mapFile);
        Assert.Equal(3, result);
    }

    /// <summary>
    /// Verifies that <see cref="MapNameParser.ParsePlayerCount"/> falls back to the directory name.
    /// </summary>
    [Fact]
    public void ParsePlayerCount_FallbackToDirectoryName_ReturnsCount()
    {
        var subDir = Path.Combine(_tempDirectory, "Mountain_Pass_4p");
        Directory.CreateDirectory(subDir);
        var mapFile = Path.Combine(subDir, "Mountain_Pass.map");
        File.WriteAllText(mapFile, "empty");

        var result = _parser.ParsePlayerCount(mapFile);
        Assert.Equal(4, result);
    }

    /// <summary>
    /// Verifies that <see cref="MapFile"/> exposes player count formatting and map type display.
    /// </summary>
    [Fact]
    public void MapFile_PlayerCountAndFormatting_WorksCorrectly()
    {
        var map = new MapFile
        {
            FileName = "TestMap.map",
            FullPath = "/test/TestMap.map",
            SizeBytes = 1024,
            GameType = GameType.ZeroHour,
            LastModified = DateTime.UtcNow,
            PlayerCount = 4,
            IsDirectory = false,
        };

        Assert.Equal(4, map.PlayerCount);
        Assert.Equal("4", map.FormattedPlayerCount);
        Assert.Equal("Map", map.MapTypeDisplay);

        map.PlayerCount = null;
        Assert.Equal("-", map.FormattedPlayerCount);

        map.IsDirectory = true;
        Assert.Equal("Directory", map.MapTypeDisplay);

        var zipMap = new MapFile
        {
            FileName = "TestPack.zip",
            FullPath = "/test/TestPack.zip",
            SizeBytes = 2048,
            GameType = GameType.ZeroHour,
            LastModified = DateTime.UtcNow,
            IsDirectory = false,
        };
        Assert.Equal("Archive", zipMap.MapTypeDisplay);
    }

    /// <summary>
    /// Verifies that <see cref="ReplayFile"/> exposes player count, names, and checksums from metadata.
    /// </summary>
    [Fact]
    public void ReplayFile_PlayerCountAndDisplay_WorksCorrectly()
    {
        var replay = new ReplayFile
        {
            FileName = "test_replay.rep",
            FullPath = "/test/test_replay.rep",
            SizeInBytes = 5000,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = new ReplayMetadata
            {
                MapName = "Tournament Desert",
                Players = new List<string> { "Player1", "Player2", "AI" },
                ExeCrc = 0x12345678,
                IniCrc = 0x87654321,
            },
        };

        Assert.Equal("Tournament Desert", replay.MapName);
        Assert.Equal(3, replay.PlayerCount);
        Assert.Equal("3", replay.FormattedPlayerCount);
        Assert.Equal("Player1, Player2, AI", replay.PlayerNamesDisplay);
        Assert.Equal(0x12345678u, replay.ExeCrc);
        Assert.Equal(0x87654321u, replay.IniCrc);
    }

    /// <summary>
    /// Verifies that <see cref="ReplayFile"/> returns defaults when metadata has no players.
    /// </summary>
    [Fact]
    public void ReplayFile_WithNoPlayers_ReturnsDefaults()
    {
        var replay = new ReplayFile
        {
            FileName = "test_replay_empty.rep",
            FullPath = "/test/test_replay_empty.rep",
            SizeInBytes = 5000,
            LastModified = DateTime.UtcNow,
            GameVersion = GameType.ZeroHour,
            Metadata = null,
        };

        Assert.Empty(replay.MapName);
        Assert.Equal(0, replay.PlayerCount);
        Assert.Equal("-", replay.FormattedPlayerCount);
        Assert.Empty(replay.PlayerNamesDisplay);
        Assert.Null(replay.ExeCrc);
        Assert.Null(replay.IniCrc);
    }
}
