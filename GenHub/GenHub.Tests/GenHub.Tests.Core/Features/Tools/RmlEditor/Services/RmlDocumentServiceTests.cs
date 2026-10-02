using FluentAssertions;
using GenHub.Core.Models.Tools.RmlEditor;
using GenHub.Features.Tools.RmlEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.RmlEditor.Services;

/// <summary>
/// Unit tests for <see cref="RmlDocumentService"/>.
/// </summary>
public sealed class RmlDocumentServiceTests
{
    private const string SampleDocument =
        "<rml>\n" +
        "  <head>\n" +
        "    <title>Main Menu</title>\n" +
        "    <link type=\"text/rcss\" href=\"MainMenu.rcss\" />\n" +
        "    <style>\n" +
        "      .screen { background-color: #101010; }\n" +
        "    </style>\n" +
        "  </head>\n" +
        "  <body class=\"screen\">\n" +
        "    <div id=\"main-menu\" class=\"panel\">\n" +
        "      <h1>Generals</h1>\n" +
        "      <button id=\"start\" data-event-click=\"start_game()\">Start</button>\n" +
        "      <img src=\"logo.tga\" />\n" +
        "      <!-- a comment -->\n" +
        "    </div>\n" +
        "  </body>\n" +
        "</rml>\n";

    private readonly RmlDocumentService _service = new(new Mock<ILogger<RmlDocumentService>>().Object);

    /// <summary>
    /// Tests that a valid document parses into head and body elements.
    /// </summary>
    [Fact]
    public void ParseText_ValidDocument_ReturnsHeadAndBody()
    {
        var result = _service.ParseText(SampleDocument, "MainMenu.rml");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Head.Tag.Should().Be("head");
        result.Data.Body.Tag.Should().Be("body");
        result.Data.Body.Elements.Should().ContainSingle(e => e.ElementId == "main-menu");
    }

    /// <summary>
    /// Tests that text runs and comments survive parsing.
    /// </summary>
    [Fact]
    public void ParseText_PreservesTextAndComments()
    {
        var result = _service.ParseText(SampleDocument);

        result.Success.Should().BeTrue();
        var panel = result.Data!.FindById("main-menu")!;
        panel.Elements.Should().Contain(e => e.Tag == "h1" && e.InnerText == "Generals");
        panel.Children.OfType<RmlComment>().Should().ContainSingle(c => c.Text.Contains("a comment"));
    }

    /// <summary>
    /// Tests that a document without a body fails parsing.
    /// </summary>
    [Fact]
    public void ParseText_MissingBody_Fails()
    {
        var result = _service.ParseText("<rml><head><title>T</title></head></rml>");

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("body");
    }

    /// <summary>
    /// Tests that malformed markup fails with line information.
    /// </summary>
    [Fact]
    public void ParseText_MalformedXml_FailsWithLine()
    {
        var result = _service.ParseText("<rml>\n  <head>\n    <title>oops</head>\n  </head>\n</rml>");

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Line 3");
    }

    /// <summary>
    /// Tests that an empty document fails parsing.
    /// </summary>
    [Fact]
    public void ParseText_Empty_Fails()
    {
        _service.ParseText("  ").Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that write output parses back to an equal structure.
    /// </summary>
    [Fact]
    public void WriteDocument_RoundTrips_Structure()
    {
        var parsed = _service.ParseText(SampleDocument);

        var written = _service.WriteDocument(parsed.Data!);
        var reparsed = _service.ParseText(written);

        reparsed.Success.Should().BeTrue();
        reparsed.Data!.AllElements().Count().Should().Be(parsed.Data!.AllElements().Count());
        reparsed.Data.FindById("start")!.GetAttribute("data-event-click").Should().Be("start_game()");
    }

    /// <summary>
    /// Tests that duplicate ids raise warnings.
    /// </summary>
    [Fact]
    public void ValidateDocument_DuplicateIds_Warns()
    {
        var parsed = _service.ParseText(SampleDocument).Data!;
        var first = parsed.FindById("start")!;
        var clone = first.Clone();
        parsed.Body.Children.Add(clone);

        var result = _service.ValidateDocument(parsed, "MainMenu.rml");

        result.WarningIssueCount.Should().BeGreaterThan(0);
        result.CriticalIssueCount.Should().Be(0);
    }

    /// <summary>
    /// Tests that an image without a source raises a warning.
    /// </summary>
    [Fact]
    public void ValidateDocument_ImageWithoutSource_Warns()
    {
        var parsed = _service.ParseText(SampleDocument).Data!;
        parsed.FindById("main-menu")!.Children.Add(new RmlElement { Tag = "img" });

        var result = _service.ValidateDocument(parsed, "MainMenu.rml");

        result.WarningIssueCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Tests that validation reports the document as valid when clean.
    /// </summary>
    [Fact]
    public void ValidateDocument_CleanDocument_IsValid()
    {
        var parsed = _service.ParseText("<rml><head><title>T</title></head><body><div><p>Hi</p></div></body></rml>").Data!;

        var result = _service.ValidateDocument(parsed, "Clean.rml");

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Tests that preformatted text keeps its whitespace verbatim.
    /// </summary>
    [Fact]
    public void ParseText_Pre_PreservesWhitespace()
    {
        var result = _service.ParseText("<rml><head><title>T</title></head><body><pre>  line1\n    line2</pre></body></rml>");

        result.Success.Should().BeTrue();
        var pre = result.Data!.Body.Elements.Single(e => e.Tag == "pre");
        pre.InnerText.Should().Be("  line1\n    line2");

        var rewritten = _service.WriteDocument(result.Data);
        var reparsed = _service.ParseText(rewritten);
        reparsed.Success.Should().BeTrue();
        reparsed.Data!.Body.Elements.Single(e => e.Tag == "pre").InnerText.Should().Be("  line1\n    line2");
    }

    /// <summary>
    /// Tests that double hyphens in comments are sanitized so output stays valid.
    /// </summary>
    [Fact]
    public void WriteDocument_CommentWithDashes_Sanitized()
    {
        var parsed = _service.ParseText(SampleDocument).Data!;
        parsed.FindById("main-menu")!.Children.Add(new RmlComment { Text = "a -- b" });

        var written = _service.WriteDocument(parsed);

        written.Should().Contain("a - - b");
        written.Should().NotContain("a -- b");
        _service.ParseText(written).Success.Should().BeTrue();
    }

    /// <summary>
    /// Tests that comments with double hyphens raise warnings.
    /// </summary>
    [Fact]
    public void ValidateDocument_CommentWithDashes_Warns()
    {
        var parsed = _service.ParseText(SampleDocument).Data!;
        parsed.FindById("main-menu")!.Children.Add(new RmlComment { Text = "a -- b" });

        var result = _service.ValidateDocument(parsed, "MainMenu.rml");

        result.WarningIssueCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Tests that parsing a missing file fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ParseFileAsync_MissingFile_FailsAsync()
    {
        var missing = Path.Combine(Path.GetTempPath(), "genhub-missing-screen.rml");

        var result = await _service.ParseFileAsync(missing);

        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a file round-trips through parse and format.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task FormatFileAsync_RoundTripsAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "genhub-format-screen.rml");
        await File.WriteAllTextAsync(path, SampleDocument);
        try
        {
            var result = await _service.FormatFileAsync(path);

            result.Success.Should().BeTrue();
            var reparsed = await _service.ParseFileAsync(path);
            reparsed.Success.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
