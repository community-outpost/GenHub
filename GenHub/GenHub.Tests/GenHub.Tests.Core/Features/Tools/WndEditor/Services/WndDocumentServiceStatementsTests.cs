using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Core.Models.Validation;
using GenHub.Features.Tools.WndEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for statement parsing and spec-aware validation in <see cref="WndDocumentService"/>.
/// </summary>
public sealed class WndDocumentServiceStatementsTests
{
    private readonly WndDocumentService _service = new(new Mock<ILogger<WndDocumentService>>().Object);

    /// <summary>
    /// Tests that raw statements parse into ordered properties.
    /// </summary>
    [Fact]
    public void ParseStatements_ValidText_ParsesProperties()
    {
        // Act
        var result = _service.ParseStatements("WINDOWTYPE = USER;\nNAME = \"MainMenu.wnd:Parent\";\nSTATUS = ENABLED+IMAGE;");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().Equal(
            new WndProperty("WINDOWTYPE", "USER"),
            new WndProperty("NAME", "\"MainMenu.wnd:Parent\""),
            new WndProperty("STATUS", "ENABLED+IMAGE"));
    }

    /// <summary>
    /// Tests that semicolons inside quotes do not split statements.
    /// </summary>
    [Fact]
    public void ParseStatements_QuotedSemicolon_KeepsStatement()
    {
        // Act
        var result = _service.ParseStatements("TEXT = \"a;b\";");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle().Which.Value.Should().Be("\"a;b\"");
    }

    /// <summary>
    /// Tests that invalid text fails with errors.
    /// </summary>
    /// <param name="content">The content under test.</param>
    [Theory]
    [InlineData("WINDOWTYPE = USER")]
    [InlineData("WINDOWTYPE USER;")]
    [InlineData("WINDOW")]
    public void ParseStatements_InvalidText_Fails(string content)
    {
        // Act
        var result = _service.ParseStatements(content);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// Tests that unknown flags are reported as warnings.
    /// </summary>
    [Fact]
    public void ValidateDocument_UnknownFlags_ReportsWarnings()
    {
        // Arrange
        var document = CreateWindowDocument("STATUS = ENABLED+FROBNICATE;\nSTYLE = USER+BOGUS;");

        // Act
        var result = _service.ValidateDocument(document, "target");

        // Assert
        result.Issues.Should().Contain(issue => issue.Message.Contains("FROBNICATE"));
        result.Issues.Should().Contain(issue => issue.Message.Contains("BOGUS"));
    }

    /// <summary>
    /// Tests that malformed typed values are reported as warnings.
    /// </summary>
    [Fact]
    public void ValidateDocument_MalformedTypedValues_ReportsWarnings()
    {
        // Arrange
        var document = CreateWindowDocument("FONT = nonsense;\nTEXTCOLOR = nonsense;\nTOOLTIPDELAY = soon;");

        // Act
        var result = _service.ValidateDocument(document, "target");

        // Assert
        result.Issues.Should().Contain(issue => issue.Message.Contains("FONT"));
        result.Issues.Should().Contain(issue => issue.Message.Contains("TEXTCOLOR"));
        result.Issues.Should().Contain(issue => issue.Message.Contains("TOOLTIPDELAY"));
    }

    /// <summary>
    /// Tests that mismatched control data is reported as a warning.
    /// </summary>
    [Fact]
    public void ValidateDocument_MismatchedControlData_ReportsWarning()
    {
        // Arrange
        var document = CreateWindowDocument("SLIDERDATA = MINVALUE: 0, MAXVALUE: 100;");

        // Act
        var result = _service.ValidateDocument(document, "target");

        // Assert
        result.Issues.Should().Contain(issue => issue.Message.Contains("SLIDERDATA"));
    }

    /// <summary>
    /// Tests that well-formed engine values validate cleanly.
    /// </summary>
    [Fact]
    public void ValidateDocument_EngineValues_HasNoIssues()
    {
        // Arrange
        var document = CreateWindowDocument(
            "STATUS = ENABLED+IMAGE;\n"
            + "STYLE = USER;\n"
            + "FONT = NAME: \"Times New Roman\", SIZE: 14, BOLD: 0;\n"
            + "TEXTCOLOR = ENABLED: 255 255 255 0, ENABLEDBORDER: 255 255 255 0, DISABLED: 255 255 255 0, DISABLEDBORDER: 255 255 255 0, HILITE: 255 255 255 0, HILITEBORDER: 255 255 255 0;");

        // Act
        var result = _service.ValidateDocument(document, "target");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    private WndDocument CreateWindowDocument(string extraStatements)
    {
        var parsed = _service.ParseStatements(
            "WINDOWTYPE = USER;\n"
            + "SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600;\n"
            + extraStatements);
        parsed.Success.Should().BeTrue();
        var window = new WndWindow { ControlTypeName = "USER" };
        foreach (var property in parsed.Data!)
        {
            window.Properties.Add(property);
        }

        var document = new WndDocument();
        document.Windows.Add(window);
        return document;
    }
}
