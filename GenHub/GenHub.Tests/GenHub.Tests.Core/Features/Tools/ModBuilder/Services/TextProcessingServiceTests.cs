using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="TextProcessingService"/>.
/// </summary>
public sealed class TextProcessingServiceTests
{
    private readonly Mock<ILogger<TextProcessingService>> _mockLogger;
    private readonly TextProcessingService _service;

    public TextProcessingServiceTests()
    {
        _mockLogger = new Mock<ILogger<TextProcessingService>>();
        _service = new TextProcessingService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var service = new TextProcessingService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessTextAsync_WithNoOptions_ReturnsUnchanged()
    {
        // Arrange
        var content = "Line 1\nLine 2\nLine 3";
        var options = new TextProcessingOptions();

        // Act
        var result = await _service.ProcessTextAsync(content, options);

        // Assert
        result.Should().Be(content);
    }

    [Fact]
    public async Task ProcessTextAsync_WithDeleteComments_RemovesComments()
    {
        // Arrange
        var content = "; Comment\nLine 1\n; Another comment\nLine 2";
        var options = new TextProcessingOptions
        {
            DeleteComments = true,
            CommentStyle = CommentStyle.IniStyle
        };

        // Act
        var result = await _service.ProcessTextAsync(content, options);

        // Assert
        result.Should().NotContain("; Comment");
        result.Should().NotContain("; Another comment");
        result.Should().Contain("Line 1");
        result.Should().Contain("Line 2");
    }

    [Fact]
    public async Task ProcessTextAsync_WithForceEOL_NormalizesLineEndings()
    {
        // Arrange
        var content = "Line 1\r\nLine 2\rLine 3\nLine 4";
        var options = new TextProcessingOptions
        {
            ForceEOL = LineEndingType.LF
        };

        // Act
        var result = await _service.ProcessTextAsync(content, options);

        // Assert
        result.Should().NotContain("\r\n");
        result.Should().NotContain("\r");
        result.Split('\n').Should().HaveCount(4);
    }

    [Fact]
    public async Task ProcessTextAsync_WithDeleteWhitespace_RemovesWhitespace()
    {
        // Arrange
        var content = "  Line 1  \n  Line 2  \n  Line 3  ";
        var options = new TextProcessingOptions
        {
            DeleteWhitespace = true,
            WhitespaceMode = WhitespaceMode.All
        };

        // Act
        var result = await _service.ProcessTextAsync(content, options);

        // Assert
        result.Should().NotStartWith(" ");
        result.Should().NotEndWith(" ");
    }

    [Fact]
    public async Task NormalizeLineEndingsAsync_ToCRLF_ConvertsCorrectly()
    {
        // Arrange
        var content = "Line 1\nLine 2\rLine 3\r\nLine 4";

        // Act
        var result = await _service.NormalizeLineEndingsAsync(content, LineEndingType.CRLF);

        // Assert
        result.Should().Contain("\r\n");
        result.Should().NotContain("\n\n");
        result.Split("\r\n").Should().HaveCount(4);
    }

    [Fact]
    public async Task NormalizeLineEndingsAsync_ToLF_ConvertsCorrectly()
    {
        // Arrange
        var content = "Line 1\r\nLine 2\rLine 3\nLine 4";

        // Act
        var result = await _service.NormalizeLineEndingsAsync(content, LineEndingType.LF);

        // Assert
        result.Should().NotContain("\r\n");
        result.Should().NotContain("\r");
        result.Split('\n').Should().HaveCount(4);
    }

    [Fact]
    public async Task NormalizeLineEndingsAsync_ToCR_ConvertsCorrectly()
    {
        // Arrange
        var content = "Line 1\r\nLine 2\nLine 3\rLine 4";

        // Act
        var result = await _service.NormalizeLineEndingsAsync(content, LineEndingType.CR);

        // Assert
        result.Should().NotContain("\r\n");
        result.Should().NotContain("\n");
        result.Split('\r').Should().HaveCount(4);
    }

    [Fact]
    public async Task RemoveCommentsAsync_WithIniStyle_RemovesIniComments()
    {
        // Arrange
        var content = "; Comment line\nData=Value ; inline comment\nMoreData=Value";

        // Act
        var result = await _service.RemoveCommentsAsync(content, CommentStyle.IniStyle);

        // Assert
        result.Should().NotContain("; Comment line");
        result.Should().Contain("Data=Value");
        result.Should().NotContain("; inline comment");
    }

    [Fact]
    public async Task RemoveCommentsAsync_WithCStyle_RemovesCStyleComments()
    {
        // Arrange
        var content = "// Comment line\nint x = 5; // inline comment\nint y = 10;";

        // Act
        var result = await _service.RemoveCommentsAsync(content, CommentStyle.CStyle);

        // Assert
        result.Should().NotContain("// Comment line");
        result.Should().Contain("int x = 5;");
        result.Should().NotContain("// inline comment");
    }

    [Fact]
    public async Task RemoveCommentsAsync_WithScriptStyle_RemovesScriptComments()
    {
        // Arrange
        var content = "# Comment line\necho 'Hello' # inline comment\necho 'World'";

        // Act
        var result = await _service.RemoveCommentsAsync(content, CommentStyle.ScriptStyle);

        // Assert
        result.Should().NotContain("# Comment line");
        result.Should().Contain("echo 'Hello'");
        result.Should().NotContain("# inline comment");
    }

    [Fact]
    public async Task RemoveWhitespaceAsync_WithTrimMode_TrimsLines()
    {
        // Arrange
        var content = "  Line 1  \n  Line 2  \n  Line 3  ";

        // Act
        var result = await _service.RemoveWhitespaceAsync(content, WhitespaceMode.All);

        // Assert
        var lines = result.Split('\n');
        lines.Should().OnlyContain(line => !line.StartsWith(" "));
        lines.Should().OnlyContain(line => !line.EndsWith(" "));
    }

    [Fact]
    public async Task RemoveWhitespaceAsync_WithCollapseMode_CollapsesWhitespace()
    {
        // Arrange
        var content = "Line   with    multiple    spaces";

        // Act
        var result = await _service.RemoveWhitespaceAsync(content, WhitespaceMode.ExtraOnly);

        // Assert
        result.Should().NotContain("  ");
        result.Should().Contain("Line with multiple spaces");
    }

    [Fact]
    public async Task ProcessTextAsync_WithAllOptions_AppliesAllTransformations()
    {
        // Arrange
        var content = "; Comment\r\n  Line 1  \r\n; Another comment\r\n  Line 2  ";
        var options = new TextProcessingOptions
        {
            DeleteComments = true,
            CommentStyle = CommentStyle.IniStyle,
            ForceEOL = LineEndingType.LF,
            DeleteWhitespace = true,
            WhitespaceMode = WhitespaceMode.All
        };

        // Act
        var result = await _service.ProcessTextAsync(content, options);

        // Assert
        result.Should().NotContain(";");
        result.Should().NotContain("\r");
        result.Should().NotStartWith(" ");
        result.Should().NotEndWith(" ");
    }

    [Fact]
    public async Task ProcessTextAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var content = "Line 1\nLine 2";
        var options = new TextProcessingOptions();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.ProcessTextAsync(content, options, cts.Token));
    }

    [Fact]
    public async Task RemoveCommentsAsync_WithEmptyContent_ReturnsEmpty()
    {
        // Arrange
        var content = string.Empty;

        // Act
        var result = await _service.RemoveCommentsAsync(content, CommentStyle.IniStyle);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NormalizeLineEndingsAsync_WithEmptyContent_ReturnsEmpty()
    {
        // Arrange
        var content = string.Empty;

        // Act
        var result = await _service.NormalizeLineEndingsAsync(content, LineEndingType.LF);

        // Assert
        result.Should().BeEmpty();
    }
}
