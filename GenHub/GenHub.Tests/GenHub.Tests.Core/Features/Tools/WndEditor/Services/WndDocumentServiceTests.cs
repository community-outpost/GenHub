using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Core.Models.Validation;
using GenHub.Features.Tools.WndEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for <see cref="WndDocumentService"/>.
/// </summary>
public sealed class WndDocumentServiceTests : IDisposable
{
    private const string SampleDocument =
        "FILE_VERSION = 2;\n" +
        "STARTLAYOUTBLOCK\n" +
        "  LAYOUTINIT = MainMenuInit;\n" +
        "ENDLAYOUTBLOCK\n" +
        "WINDOW\n" +
        "  WINDOWTYPE = USER;\n" +
        "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600;\n" +
        "  NAME = \"MainMenu.wnd:ParentMenu\";\n" +
        "  STATUS = ENABLED;\n" +
        "  CHILD\n" +
        "  WINDOW\n" +
        "    WINDOWTYPE = PUSHBUTTON;\n" +
        "    SCREENRECT = UPPERLEFT: 10 20,\n" +
        "                 BOTTOMRIGHT: 110 60,\n" +
        "                 CREATIONRESOLUTION: 800 600;\n" +
        "    NAME = \"MainMenu.wnd:StartButton\";\n" +
        "  END\n" +
        "  ENDALLCHILDREN\n" +
        "END\n";

    private readonly Mock<ILogger<WndDocumentService>> _mockLogger;
    private readonly WndDocumentService _service;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndDocumentServiceTests"/> class.
    /// </summary>
    public WndDocumentServiceTests()
    {
        _mockLogger = new Mock<ILogger<WndDocumentService>>();
        _service = new WndDocumentService(_mockLogger.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Tests that the constructor accepts valid dependencies.
    /// </summary>
    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var service = new WndDocumentService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that valid document text parses into metadata, layout, and windows.
    /// </summary>
    [Fact]
    public void ParseText_ValidDocument_ParsesMetadataLayoutAndWindows()
    {
        // Act
        var result = _service.ParseText(SampleDocument, "MainMenu.wnd");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        var document = result.Data!;
        document.FileVersion.Should().Be("2");
        document.SourcePath.Should().Be("MainMenu.wnd");
        document.LayoutBlock.Should().ContainSingle().Which.Should().Be(new WndProperty("LAYOUTINIT", "MainMenuInit"));
        var window = document.Windows.Should().ContainSingle().Which;
        window.ControlTypeName.Should().Be("USER");
        window.ControlType.Should().Be(WndControlType.User);
        window.Name.Should().Be("\"MainMenu.wnd:ParentMenu\"");
        window.TryGetScreenRect(out var rect).Should().BeTrue();
        rect.Should().Be(new WndScreenRect(0, 0, 800, 600, 800, 600));
        var child = window.Children.Should().ContainSingle().Which;
        child.ControlType.Should().Be(WndControlType.PushButton);
        child.Name.Should().Be("\"MainMenu.wnd:StartButton\"");
    }

    /// <summary>
    /// Tests that multi-line values are joined across continuation lines.
    /// </summary>
    [Fact]
    public void ParseText_MultiLineValue_JoinsContinuationLines()
    {
        // Act
        var result = _service.ParseText(SampleDocument);

        // Assert
        result.Success.Should().BeTrue();
        var child = result.Data!.Windows[0].Children[0];
        child.TryGetScreenRect(out var rect).Should().BeTrue();
        rect.Should().Be(new WndScreenRect(10, 20, 110, 60, 800, 600));
    }

    /// <summary>
    /// Tests that unknown control types are preserved and map to unknown.
    /// </summary>
    [Fact]
    public void ParseText_UnknownControlType_PreservesNameAndMapsToUnknown()
    {
        // Arrange
        var content = "WINDOW\n  WINDOWTYPE = FUTUREWIDGET;\n  NAME = \"X\";\nEND\n";

        // Act
        var result = _service.ParseText(content);

        // Assert
        result.Success.Should().BeTrue();
        var window = result.Data!.Windows.Should().ContainSingle().Which;
        window.ControlTypeName.Should().Be("FUTUREWIDGET");
        window.ControlType.Should().Be(WndControlType.Unknown);
        window.GetProperty("WINDOWTYPE").Should().Be("FUTUREWIDGET");
    }

    /// <summary>
    /// Tests that a missing statement terminator returns a failure.
    /// </summary>
    [Fact]
    public void ParseText_MissingTerminator_ReturnsFailure()
    {
        // Arrange
        var content = "WINDOW\n  WINDOWTYPE = USER\nEND\n";

        // Act
        var result = _service.ParseText(content);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("expected ';'");
    }

    /// <summary>
    /// Tests that an unterminated window returns a failure.
    /// </summary>
    [Fact]
    public void ParseText_UnterminatedWindow_ReturnsFailure()
    {
        // Arrange
        var content = "WINDOW\n  WINDOWTYPE = USER;\n";

        // Act
        var result = _service.ParseText(content);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Unterminated window");
    }

    /// <summary>
    /// Tests that an unexpected top-level property returns a failure.
    /// </summary>
    [Fact]
    public void ParseText_UnexpectedTopLevelProperty_ReturnsFailure()
    {
        // Arrange
        var content = "STATUS = ENABLED;\n";

        // Act
        var result = _service.ParseText(content);

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Unexpected top-level property");
    }

    /// <summary>
    /// Tests that parsing a missing file returns a failure.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileAsync_MissingFile_ReturnsFailure()
    {
        // Act
        var result = await _service.ParseFileAsync(Path.Combine(_tempDirectory, "missing.wnd"));

        // Assert
        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("not found");
    }

    /// <summary>
    /// Tests that parsing a valid file returns the document.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseFileAsync_ValidFile_ParsesDocument()
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, "MainMenu.wnd");
        await File.WriteAllTextAsync(path, SampleDocument);

        // Act
        var result = await _service.ParseFileAsync(path);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.SourcePath.Should().Be(path);
        result.Data.Windows.Should().ContainSingle();
    }

    /// <summary>
    /// Tests that writing and re-parsing preserves document semantics.
    /// </summary>
    [Fact]
    public void WriteDocument_RoundTrip_PreservesSemantics()
    {
        // Arrange
        var first = _service.ParseText(SampleDocument).Data!;

        // Act
        var canonical = _service.WriteDocument(first);
        var second = _service.ParseText(canonical).Data!;

        // Assert
        _service.ParseText(canonical).Success.Should().BeTrue();
        DocumentsEqual(first, second).Should().BeTrue();
        _service.WriteDocument(second).Should().Be(canonical);
    }

    /// <summary>
    /// Tests that an empty document writes only the version statement.
    /// </summary>
    [Fact]
    public void WriteDocument_EmptyDocument_WritesVersionOnly()
    {
        // Arrange
        var document = new WndDocument();

        // Act
        var text = _service.WriteDocument(document);

        // Assert
        text.Should().Be("FILE_VERSION = 2;\n");
    }

    /// <summary>
    /// Tests that formatting rewrites the file in canonical form.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_ValidFile_RewritesCanonicalFile()
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, "Messy.wnd");
        await File.WriteAllTextAsync(path, "FILE_VERSION=2;\nWINDOW\nWINDOWTYPE=USER;\nEND\n");

        // Act
        var result = await _service.FormatFileAsync(path);

        // Assert
        result.Success.Should().BeTrue();
        var formatted = await File.ReadAllTextAsync(path);
        formatted.Should().Be("FILE_VERSION = 2;\nWINDOW\n  WINDOWTYPE = USER;\nEND\n");
        _service.ParseText(formatted).Success.Should().BeTrue();
    }

    /// <summary>
    /// Tests that formatting a missing file fails without creating it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_MissingFile_ReturnsFailureAndCreatesNothing()
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, "missing.wnd");

        // Act
        var result = await _service.FormatFileAsync(path);

        // Assert
        result.Success.Should().BeFalse();
        File.Exists(path).Should().BeFalse();
    }

    /// <summary>
    /// Tests that a valid document passes validation.
    /// </summary>
    [Fact]
    public void ValidateDocument_ValidDocument_IsValid()
    {
        // Arrange
        var document = _service.ParseText(SampleDocument).Data!;

        // Act
        var result = _service.ValidateDocument(document, "MainMenu.wnd");

        // Assert
        result.Success.Should().BeTrue();
        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that a missing window type reports an error.
    /// </summary>
    [Fact]
    public void ValidateDocument_MissingWindowType_ReportsError()
    {
        // Arrange
        var document = _service.ParseText("WINDOW\n  NAME = \"X\";\nEND\n").Data!;

        // Act
        var result = _service.ValidateDocument(document, "Broken.wnd");

        // Assert
        result.Success.Should().BeFalse();
        result.IsValid.Should().BeFalse();
        result.Issues.Should().ContainSingle().Which.Severity.Should().Be(ValidationSeverity.Error);
    }

    /// <summary>
    /// Tests that an unknown control type reports a warning but stays valid.
    /// </summary>
    [Fact]
    public void ValidateDocument_UnknownControlType_ReportsWarningButStaysValid()
    {
        // Arrange
        var document = _service.ParseText("WINDOW\n  WINDOWTYPE = FUTUREWIDGET;\nEND\n").Data!;

        // Act
        var result = _service.ValidateDocument(document, "Future.wnd");

        // Assert
        result.Success.Should().BeTrue();
        result.IsValid.Should().BeTrue();
        result.Issues.Should().ContainSingle().Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    /// <summary>
    /// Tests that an invalid screen rectangle reports a warning but stays valid.
    /// </summary>
    [Fact]
    public void ValidateDocument_BadScreenRect_ReportsWarningButStaysValid()
    {
        // Arrange
        var document = _service.ParseText("WINDOW\n  WINDOWTYPE = USER;\n  SCREENRECT = nonsense;\nEND\n").Data!;

        // Act
        var result = _service.ValidateDocument(document, "BadRect.wnd");

        // Assert
        result.Success.Should().BeTrue();
        result.Issues.Should().ContainSingle().Which.Message.Should().Contain("SCREENRECT");
    }

    /// <summary>
    /// Tests that validating a missing file reports a critical issue.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateFileAsync_MissingFile_ReportsCritical()
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, "missing.wnd");

        // Act
        var result = await _service.ValidateFileAsync(path);

        // Assert
        result.Success.Should().BeFalse();
        result.Issues.Should().ContainSingle().Which.Severity.Should().Be(ValidationSeverity.Critical);
    }

    /// <summary>
    /// Tests that a bare window token inside a window body parses as a nested child.
    /// </summary>
    [Fact]
    public void ParseText_BareWindowInBody_ParsesAsNestedChild()
    {
        // Arrange
        var content = "WINDOW\n  WINDOWTYPE = USER;\n  WINDOW\n    WINDOWTYPE = PUSHBUTTON;\n  END\nEND\n";

        // Act
        var result = _service.ParseText(content);

        // Assert
        result.Success.Should().BeTrue();
        var child = result.Data!.Windows.Should().ContainSingle().Which.Children.Should().ContainSingle().Which;
        child.ControlType.Should().Be(WndControlType.PushButton);
    }

    /// <summary>
    /// Tests that formatting preserves whitespace inside quoted values.
    /// </summary>
    [Fact]
    public void WriteDocument_PreservesWhitespaceInsideQuotedValues()
    {
        // Arrange
        var document = _service.ParseText("WINDOW\n  WINDOWTYPE = STATICTEXT;\n  TEXT = \"Start  Screen\";\nEND\n").Data!;

        // Act
        var text = _service.WriteDocument(document);

        // Assert
        text.Should().Contain("TEXT = \"Start  Screen\";");
        _service.ParseText(text).Success.Should().BeTrue();
    }

    /// <summary>
    /// Tests that extended control types map to their enum values.
    /// </summary>
    /// <param name="typeName">The declared control type name.</param>
    /// <param name="expected">The expected control type.</param>
    [Theory]
    [InlineData("COMMANDBUTTON", WndControlType.CommandButton)]
    [InlineData("TABCONTROL", WndControlType.TabControl)]
    [InlineData("TABPANE", WndControlType.TabPane)]
    public void ParseText_ExtendedControlTypes_MapToEnumValues(string typeName, WndControlType expected)
    {
        // Act
        var result = _service.ParseText($"WINDOW\n  WINDOWTYPE = {typeName};\nEND\n");

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Windows.Should().ContainSingle().Which.ControlType.Should().Be(expected);
    }

    /// <summary>
    /// Tests that a cancelled format propagates cancellation without leaving files behind.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FormatFileAsync_WhenCancelled_ThrowsAndLeavesNoTempFiles()
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, "Cancel.wnd");
        await File.WriteAllTextAsync(path, "WINDOW\n  WINDOWTYPE = USER;\nEND\n");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await _service.FormatFileAsync(path, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(_tempDirectory).Should().ContainSingle();
    }

    private static bool DocumentsEqual(WndDocument first, WndDocument second)
    {
        return string.Equals(first.FileVersion, second.FileVersion, StringComparison.Ordinal)
            && PropertiesEqual(first.LayoutBlock, second.LayoutBlock)
            && first.Windows.Count == second.Windows.Count
            && first.Windows.Zip(second.Windows, WindowsEqual).All(match => match);
    }

    private static bool WindowsEqual(WndWindow first, WndWindow second)
    {
        return string.Equals(first.ControlTypeName, second.ControlTypeName, StringComparison.Ordinal)
            && PropertiesEqual(first.Properties, second.Properties)
            && first.Children.Count == second.Children.Count
            && first.Children.Zip(second.Children, WindowsEqual).All(match => match);
    }

    private static bool PropertiesEqual(
        IReadOnlyList<WndProperty> first,
        IReadOnlyList<WndProperty> second)
    {
        return first.Count == second.Count
            && first.Zip(second, (a, b) => string.Equals(a.Key, b.Key, StringComparison.Ordinal)
                && string.Equals(Normalize(a.Value), Normalize(b.Value), StringComparison.Ordinal)).All(match => match);
    }

    private static string Normalize(string value)
    {
        return string.Join(" ", value.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }
}
