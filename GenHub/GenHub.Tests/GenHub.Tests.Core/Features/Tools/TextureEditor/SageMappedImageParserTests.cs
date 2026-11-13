using GenHub.Core.Services.Tools.TextureEditor;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.TextureEditor;

/// <summary>
/// Unit tests for <see cref="SageMappedImageParser"/>.
/// </summary>
public sealed class SageMappedImageParserTests
{
    private readonly SageMappedImageParser _parser = new(NullLogger<SageMappedImageParser>.Instance);

    /// <summary>
    /// Verifies that valid INI text parses all blocks with exclusive SAGE edges.
    /// </summary>
    [Fact]
    public void ParseText_ValidIni_ParsesAllBlocks()
    {
        const string ini = """
            ; Retail mapped images
            MappedImage AttackButton
              Texture = CommandBar.tga
              TextureWidth = 512
              TextureHeight = 512
              Coords = Left:10 Top:10 Right:73 Bottom:73
              Status = NONE
            End

            MappedImage GuardButton
              Texture = CommandBar.tga
              TextureWidth = 512
              TextureHeight = 512
              Coords = Left:80 Top:10 Right:143 Bottom:73
              Status = NONE
            End
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count);
        Assert.Equal("AttackButton", result.Data[0].Name);
        Assert.Equal("CommandBar.tga", result.Data[0].TextureFileName);
        Assert.Equal(63, result.Data[0].Width);
        Assert.Equal(63, result.Data[0].Height);
        Assert.True(result.Data[0].IsWithinTexture);
        Assert.True(result.Data[0].HasGuardBorder);
    }

    /// <summary>
    /// Verifies that block and key matching is case-insensitive like the SAGE engine.
    /// </summary>
    [Fact]
    public void ParseText_MixedCase_ParsesSuccessfully()
    {
        const string ini = """
            mappedimage LowerButton
              texture = bar.tga
              texturewidth = 128
              textureheight = 128
              coords = left:0 top:0 right:31 bottom:31
              status = none
            end
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal("LowerButton", result.Data[0].Name);
        Assert.Equal(31, result.Data[0].Width);
    }

    /// <summary>
    /// Verifies that coordinate keys parse in any order with the last duplicate winning.
    /// </summary>
    [Fact]
    public void ParseText_UnorderedCoords_ParsesCoordinates()
    {
        const string ini = """
            MappedImage ShuffledButton
              Texture = bar.tga
              TextureWidth = 128
              TextureHeight = 128
              Coords = Bottom:63 Right:63 Top:0 Left:4 Left:0
              Status = NONE
            End
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal(0, result.Data[0].Left);
        Assert.Equal(0, result.Data[0].Top);
        Assert.Equal(63, result.Data[0].Right);
        Assert.Equal(63, result.Data[0].Bottom);
    }

    /// <summary>
    /// Verifies that malformed blocks produce failures while preserving parsed entries.
    /// </summary>
    [Fact]
    public void ParseText_MalformedBlock_ReturnsFailureWithPartialData()
    {
        const string ini = """
            MappedImage GoodButton
              Texture = bar.tga
              TextureWidth = 128
              TextureHeight = 128
              Coords = Left:0 Top:0 Right:31 Bottom:31
              Status = NONE
            End
            MappedImage BrokenButton
              Texture = bar.tga
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Failed);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal("GoodButton", result.Data[0].Name);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Verifies that a stray End terminator is reported as an error.
    /// </summary>
    [Fact]
    public void ParseText_StrayEnd_ReturnsFailure()
    {
        var result = _parser.ParseText("End\n");

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Verifies that null content throws an argument null exception.
    /// </summary>
    [Fact]
    public void ParseText_NullContent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _parser.ParseText(null!));
    }

    /// <summary>
    /// Verifies that a missing file returns a failure instead of throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseFileAsync_MissingFile_ReturnsFailureAsync()
    {
        var result = await _parser.ParseFileAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini"));

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Verifies that parsing a real file records the source path on each entry.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseFileAsync_ValidFile_RecordsSourcePathAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini");
        try
        {
            await File.WriteAllTextAsync(path, "MappedImage Solo\n  Texture = a.tga\n  TextureWidth = 64\n  TextureHeight = 64\n  Coords = Left:0 Top:0 Right:63 Bottom:63\n  Status = NONE\nEnd\n");
            var result = await _parser.ParseFileAsync(path);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Single(result.Data);
            Assert.Equal(path, result.Data[0].SourcePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that serialization reproduces the exact SAGE block format.
    /// </summary>
    [Fact]
    public void Serialize_Entries_WritesExactSageFormat()
    {
        var definition = new GenHub.Core.Models.Tools.TextureEditor.MappedImageDefinition(
            "AttackButton", "CommandBar.tga", 512, 512, 10, 10, 73, 73);

        string text = _parser.Serialize([definition]);

        string expected = string.Join(
            Environment.NewLine,
            "MappedImage AttackButton",
            "  Texture = CommandBar.tga",
            "  TextureWidth = 512",
            "  TextureHeight = 512",
            "  Coords = Left:10 Top:10 Right:73 Bottom:73",
            "  Status = NONE",
            "End",
            string.Empty,
            string.Empty);
        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Verifies that serialization writes each header line as a comment.
    /// </summary>
    [Fact]
    public void Serialize_WithHeader_WritesCommentLines()
    {
        var definition = new GenHub.Core.Models.Tools.TextureEditor.MappedImageDefinition(
            "Solo", "a.tga", 64, 64, 0, 0, 63, 63);

        string text = _parser.Serialize([definition], "Title\nSecond line");

        Assert.StartsWith("; Title" + Environment.NewLine + "; Second line" + Environment.NewLine, text);
        Assert.Contains("MappedImage Solo", text);
    }

    /// <summary>
    /// Verifies that serialization rejects names that would inject extra INI lines.
    /// </summary>
    [Fact]
    public void Serialize_NameWithLineBreak_ThrowsArgumentException()
    {
        var definition = new GenHub.Core.Models.Tools.TextureEditor.MappedImageDefinition(
            "Evil\nEnd", "a.tga", 64, 64, 0, 0, 63, 63);

        var exception = Assert.Throws<ArgumentException>(() => _parser.Serialize([definition]));
        Assert.Contains("Evil", exception.Message);
    }

    /// <summary>
    /// Verifies that serialization rejects texture names that would be truncated as comments.
    /// </summary>
    [Fact]
    public void Serialize_TextureWithSemicolon_ThrowsArgumentException()
    {
        var definition = new GenHub.Core.Models.Tools.TextureEditor.MappedImageDefinition(
            "Solo", "a.tga;injected", 64, 64, 0, 0, 63, 63);

        Assert.Throws<ArgumentException>(() => _parser.Serialize([definition]));
    }

    /// <summary>
    /// Verifies that serialization rejects status values that would inject extra INI lines.
    /// </summary>
    [Fact]
    public void Serialize_StatusWithLineBreak_ThrowsArgumentException()
    {
        var definition = new GenHub.Core.Models.Tools.TextureEditor.MappedImageDefinition(
            "Solo", "a.tga", 64, 64, 0, 0, 63, 63, "NONE\r\nEnd");

        Assert.Throws<ArgumentException>(() => _parser.Serialize([definition]));
    }

    /// <summary>
    /// Verifies that a Coords line without coordinate fields fails the block instead of zeroing coordinates.
    /// </summary>
    [Fact]
    public void ParseText_MalformedCoords_ReturnsFailure()
    {
        const string ini = """
            MappedImage Broken
              Texture = bar.tga
              TextureWidth = 64
              TextureHeight = 64
              Coords = malformed
              Status = NONE
            End
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Errors);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// Verifies that non-numeric coordinate values fail the block instead of converting to zero.
    /// </summary>
    [Fact]
    public void ParseText_NonNumericCoords_ReturnsFailure()
    {
        const string ini = """
            MappedImage Broken
              Texture = bar.tga
              TextureWidth = 64
              TextureHeight = 64
              Coords = Left:x Top:0 Right:31 Bottom:31
              Status = NONE
            End
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Errors);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// Verifies that a tab after the block keyword still starts a mapped image block.
    /// </summary>
    [Fact]
    public void ParseText_TabAfterBlockName_ParsesSuccessfully()
    {
        var result = _parser.ParseText("MappedImage\tTabButton\n  Texture = bar.tga\n  TextureWidth = 64\n  TextureHeight = 64\n  Coords = Left:0 Top:0 Right:32 Bottom:32\n  Status = NONE\nEnd\n");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal("TabButton", result.Data[0].Name);
    }

    /// <summary>
    /// Verifies that whitespace after the Coords colon still parses coordinates.
    /// </summary>
    [Fact]
    public void ParseText_SpacedCoords_ParsesSuccessfully()
    {
        const string ini = """
            MappedImage Spaced
              Texture = bar.tga
              TextureWidth = 64
              TextureHeight = 64
              Coords = Left: 0 Top: 0 Right: 32 Bottom: 32
              Status = NONE
            End
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal(32, result.Data[0].Width);
        Assert.Equal(32, result.Data[0].Height);
    }

    /// <summary>
    /// Verifies that equals and comma separators still parse coordinates.
    /// </summary>
    [Fact]
    public void ParseText_EqualsCommaCoords_ParsesSuccessfully()
    {
        const string ini = """
            MappedImage Equals
              Texture = bar.tga
              TextureWidth = 64
              TextureHeight = 64
              Coords = Left=0,Top=0,Right=32,Bottom=32
              Status = NONE
            End
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal(32, result.Data[0].Width);
    }

    /// <summary>
    /// Verifies that an empty texture value fails the block instead of registering an entry.
    /// </summary>
    [Fact]
    public void ParseText_EmptyTexture_ReturnsFailure()
    {
        const string ini = """
            MappedImage Broken
              Texture =
              TextureWidth = 64
              TextureHeight = 64
              Coords = Left:0 Top:0 Right:31 Bottom:31
              Status = NONE
            End
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Errors);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// Verifies that error line numbers count blank lines like the source file.
    /// </summary>
    [Fact]
    public void ParseText_BlankLinesBeforeError_ReportsSourceLineNumber()
    {
        const string ini = """
            MappedImage First
              Texture = bar.tga
              TextureWidth = 64
              TextureHeight = 64
              Coords = Left:0 Top:0 Right:31 Bottom:31
              Status = NONE
            End


            MappedImage Broken
              Texture = bar.tga
              Coords = malformed
            End
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, error => error.Contains("Line 10", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that parsed entries detect out-of-bounds coordinates.
    /// </summary>
    [Fact]
    public void ParseText_OutOfBoundsCoords_ReportsInvalid()
    {
        const string ini = """
            MappedImage Outside
              Texture = bar.tga
              TextureWidth = 64
              TextureHeight = 64
              Coords = Left:0 Top:0 Right:80 Bottom:80
              Status = NONE
            End
            """;

        var result = _parser.ParseText(ini);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.False(result.Data[0].IsWithinTexture);
        Assert.False(result.Data.Single().HasGuardBorder);
    }
}
