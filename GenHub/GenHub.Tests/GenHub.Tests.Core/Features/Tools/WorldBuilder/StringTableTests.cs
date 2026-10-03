// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Services.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="StrFile"/> and <see cref="StringTableService"/>: STR text
/// parsing, CSF plus STR loading with override precedence, and DisplayName label
/// resolution.
/// </summary>
public sealed class StringTableTests : IDisposable
{
    private readonly CatalogTestHost _host = CatalogTestHost.Create();

    /// <summary>
    /// Cleans up the test host.
    /// </summary>
    public void Dispose()
    {
        _host.Dispose();
    }

    /// <summary>
    /// Tests that STR entries parse like the engine: a value may span physical
    /// lines with each embedded newline reading as one space, and backslashes
    /// are preserved verbatim with an escaped quote not closing the value.
    /// </summary>
    [Fact]
    public void StrFile_LoadText_ParsesEntries()
    {
        // Arrange
        var text = """
            TankName
              "Battle Tank"
            End
            MultiLine
              "First line
              Second \"quoted\" line"
            END
            """;

        // Act
        var file = StrFile.LoadText(text);

        // Assert
        file.Count.Should().Be(2);
        file.GetString("tankname").Should().Be("Battle Tank");
        file.GetString("MultiLine").Should().Be("First line Second \\\"quoted\\\" line");
        file.GetString("Ghost").Should().BeEmpty();
    }

    /// <summary>
    /// Tests that a second quoted value for the same label is ignored, matching
    /// release-engine behavior where only the first string is kept.
    /// </summary>
    [Fact]
    public void StrFile_LoadText_SecondValue_Ignored()
    {
        // Arrange
        var text = """
            Duplicate
              "First"
              "Second"
            End
            """;

        // Act
        var file = StrFile.LoadText(text);

        // Assert
        file.GetString("Duplicate").Should().Be("First");
    }

    /// <summary>
    /// Tests that malformed STR text throws with line context.
    /// </summary>
    /// <param name="text">The malformed text.</param>
    [Theory]
    [InlineData("\"Orphan\"\nEnd\n")]
    [InlineData("End\n")]
    [InlineData("Label\n\"Value\"\n")]
    [InlineData("Label\n\"Unterminated\nEnd\n")]
    [InlineData("First\n\"A\"\nSecond\n\"B\"\nEnd\n")]
    public void StrFile_LoadText_Malformed_Throws(string text)
    {
        // Act
        var act = () => StrFile.LoadText(text.Replace("\n", Environment.NewLine));

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    /// <summary>
    /// Tests that CSF loads and STR overrides same-named CSF labels.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadAsync_CsfAndStr_StrWinsOverlapAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Data\English\Generals.csf", BuildCsf(("Shared", "FromCsf"), ("CsfOnly", "CsfValue")));
        _host.WriteLoose(workspace, @"Data\Generals.str", "Shared\n  \"FromStr\"\nEnd\nStrOnly\n  \"StrValue\"\nEnd\n");
        var fileSystem = await _host.MountAsync(workspace);
        var sut = new StringTableService(NullLogger<StringTableService>.Instance);

        // Act
        var loaded = await sut.LoadAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeTrue();
        loaded.Data!.CsfLabelCount.Should().Be(2);
        loaded.Data.StrLabelCount.Should().Be(2);
        loaded.Data.Language.Should().Be("English");
        sut.GetString("Shared").Should().Be("FromStr");
        sut.GetString("CsfOnly").Should().Be("CsfValue");
        sut.GetString("StrOnly").Should().Be("StrValue");
        sut.GetLabels().Should().BeEquivalentTo("Shared", "CsfOnly", "StrOnly");
        sut.Count.Should().Be(3);
    }

    /// <summary>
    /// Tests that missing string sources succeed with empty tables.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadAsync_MissingBoth_SucceedsEmptyAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var fileSystem = await _host.MountAsync(workspace);
        var sut = new StringTableService(NullLogger<StringTableService>.Instance);

        // Act
        var loaded = await sut.LoadAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeTrue();
        loaded.Data!.TotalLabels.Should().Be(0);
        sut.Count.Should().Be(0);
    }

    /// <summary>
    /// Tests that a malformed CSF table fails the load.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadAsync_MalformedCsf_ReturnsFailureAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Data\English\Generals.csf", [0x00, 0x01, 0x02, 0x03]);
        var fileSystem = await _host.MountAsync(workspace);
        var sut = new StringTableService(NullLogger<StringTableService>.Instance);

        // Act
        var loaded = await sut.LoadAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a per-map map.str overlays game strings and tolerates absence.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadMapStringsAsync_Overlay_WinsAndToleratesMissingAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Data\Generals.str", "Shared\n  \"FromStr\"\nEnd\n");
        var fileSystem = await _host.MountAsync(workspace);
        var sut = new StringTableService(NullLogger<StringTableService>.Instance);
        (await sut.LoadAsync(fileSystem)).Success.Should().BeTrue();
        var mapDir = _host.NewDir("map");
        var mapStr = _host.WriteFile(mapDir, "map.str", "Shared\n  \"FromMap\"\nEnd\nMapOnly\n  \"MapValue\"\nEnd\n");

        // Act
        var loaded = await sut.LoadMapStringsAsync(mapStr);

        // Assert
        loaded.Success.Should().BeTrue();
        loaded.Data!.MapStrLabelCount.Should().Be(2);
        sut.GetString("Shared").Should().Be("FromMap");
        sut.GetString("MapOnly").Should().Be("MapValue");
        (await sut.LoadMapStringsAsync(Path.Combine(mapDir, "absent.str"))).Success.Should().BeTrue();
    }

    /// <summary>
    /// Tests DisplayName resolution: LABEL strip, direct lookup, and fallbacks.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ResolveLabel_PrefixedAndBare_ResolvesOrFallsBackAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Data\Generals.str", "NameTank\n  \"Battle Tank\"\nEnd\n");
        var fileSystem = await _host.MountAsync(workspace);
        var sut = new StringTableService(NullLogger<StringTableService>.Instance);
        (await sut.LoadAsync(fileSystem)).Success.Should().BeTrue();

        // Act and assert
        sut.ResolveLabel("LABEL:NameTank", "AmericaTank").Should().Be("Battle Tank");
        sut.ResolveLabel("label:nametank", "AmericaTank").Should().Be("Battle Tank");
        sut.ResolveLabel("NameTank", "AmericaTank").Should().Be("Battle Tank");
        sut.ResolveLabel("LABEL:Ghost", "AmericaTank").Should().Be("AmericaTank");
        sut.ResolveLabel(null, "AmericaTank").Should().Be("AmericaTank");
        sut.ResolveLabel("  ", "AmericaTank").Should().Be("AmericaTank");
    }

    private static byte[] BuildCsf(params (string Label, string Value)[] entries)
    {
        var csf = new CsfFile();
        foreach (var (label, value) in entries)
        {
            csf.SetString(label, value);
        }

        using var stream = new MemoryStream();
        csf.Save(stream);
        return stream.ToArray();
    }
}
