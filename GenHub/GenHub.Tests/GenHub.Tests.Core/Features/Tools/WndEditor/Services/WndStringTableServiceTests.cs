using FluentAssertions;
using GenHub.Core.Services.Tools.GenHotkeys;
using GenHub.Features.Tools.WndEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for <see cref="WndStringTableService"/>.
/// </summary>
public sealed class WndStringTableServiceTests : IDisposable
{
    private readonly string _gameRoot;
    private readonly WndStringTableService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndStringTableServiceTests"/> class.
    /// </summary>
    public WndStringTableServiceTests()
    {
        _gameRoot = Path.Combine(Path.GetTempPath(), "GenHub_WndStringTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Data", "english"));
        _service = new WndStringTableService(Mock.Of<ILogger<WndStringTableService>>());
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
    /// Tests that known labels resolve to their localized values.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetStringsAsync_KnownLabel_ReturnsValue()
    {
        // Arrange
        WriteStrings(("GUI:Accept", "Accept"), ("GUI:Cancel", "&Cancel"));

        // Act
        var result = await _service.GetStringsAsync(["GUI:Accept", "GUI:Cancel", "GUI:Missing"], _gameRoot, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Contain("GUI:Accept", "Accept");
        result.Data.Should().Contain("GUI:Cancel", "&Cancel");
        result.Data.Should().NotContainKey("GUI:Missing");
    }

    /// <summary>
    /// Tests that label lookup is case-insensitive like the engine.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetStringsAsync_LabelCaseInsensitive_Resolves()
    {
        // Arrange
        WriteStrings(("GUI:Accept", "Accept"));

        // Act
        var result = await _service.GetStringsAsync(["gui:accept"], _gameRoot, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainKey("gui:accept");
    }

    /// <summary>
    /// Tests that override roots win over base roots.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetStringsAsync_OverrideRoot_Wins()
    {
        // Arrange
        WriteStrings(("GUI:Accept", "Base"));
        var overrideRoot = Path.Combine(Path.GetTempPath(), "GenHub_WndStringOverride_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(overrideRoot, "Data", "english"));
            var table = new CsfFile();
            table.SetString("GUI:Accept", "Override");
            table.Save(Path.Combine(overrideRoot, "Data", "english", "Generals.csf"));

            // Act
            var result = await _service.GetStringsAsync(["GUI:Accept"], _gameRoot, overrideRoot, null);

            // Assert
            result.Success.Should().BeTrue();
            result.Data.Should().Contain("GUI:Accept", "Override");
        }
        finally
        {
            if (Directory.Exists(overrideRoot))
            {
                Directory.Delete(overrideRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Tests that a missing game root fails gracefully.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetStringsAsync_MissingRoot_Fails()
    {
        // Act
        var result = await _service.GetStringsAsync(["GUI:Accept"], Path.Combine(_gameRoot, "Nope"), null, null);

        // Assert
        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a missing string table resolves nothing but still succeeds.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetStringsAsync_NoTable_ReturnsEmpty()
    {
        // Act
        var result = await _service.GetStringsAsync(["GUI:Accept"], _gameRoot, null, null);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that ParseStrFile handles both single-line and multi-line .str syntax.
    /// </summary>
    [Fact]
    public void ParseStrFile_SingleAndMultiLine_ParsesCorrectly()
    {
        // Arrange
        var content =
            "// Comment line\n" +
            "GUI:Single \"Single Line Value\"\n" +
            "GUI:Multi\n" +
            "\"Line 1\n" +
            "Line 2\"\n" +
            "End\n";
        var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Act
        WndStringTableService.ParseStrFile(content, dict);

        // Assert
        dict.Should().ContainKey("GUI:Single").WhoseValue.Should().Be("Single Line Value");
        dict.Should().ContainKey("GUI:Multi").WhoseValue.Should().Be("Line 1\nLine 2");
    }

    /// <summary>
    /// Tests that a mod plain-text .str file overrides CSF string table entries.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetStringsAsync_ModStrFile_OverridesCsf()
    {
        // Arrange base CSF
        WriteStrings(("GUI:Accept", "Base Accept"));

        // Arrange mod .str
        var modDir = Path.Combine(Path.GetTempPath(), "GenHub_ModStrTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(modDir);
            File.WriteAllText(Path.Combine(modDir, "generals.str"), "GUI:Accept \"Mod Accept\"\n");

            // Act
            var result = await _service.GetStringsAsync(["GUI:Accept"], _gameRoot, null, modDir);

            // Assert
            result.Success.Should().BeTrue();
            result.Data.Should().Contain("GUI:Accept", "Mod Accept");
        }
        finally
        {
            if (Directory.Exists(modDir))
            {
                Directory.Delete(modDir, true);
            }
        }
    }

    private void WriteStrings(params (string Label, string Value)[] entries)
    {
        var table = new CsfFile();
        foreach (var (label, value) in entries)
        {
            table.SetString(label, value);
        }

        table.Save(Path.Combine(_gameRoot, "Data", "english", "Generals.csf"));
    }
}
