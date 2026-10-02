using FluentAssertions;
using GenHub.Features.Tools.RmlEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.RmlEditor.Services;

/// <summary>
/// Unit tests for <see cref="RcssDocumentService"/>.
/// </summary>
public sealed class RcssDocumentServiceTests
{
    private const string SampleSheet =
        "/* Screen theme */\n" +
        ".screen {\n" +
        "  background-color: #101010;\n" +
        "  color: white;\n" +
        "}\n" +
        "\n" +
        "div .panel, #main-menu {\n" +
        "  padding: 8dp;\n" +
        "  margin: 4dp !important;\n" +
        "}\n" +
        "\n" +
        "@font-face {\n" +
        "  font-family: Bank;\n" +
        "  src: url(\"bank.ttf\");\n" +
        "}\n";

    private readonly RcssDocumentService _service = new(new Mock<ILogger<RcssDocumentService>>().Object);

    /// <summary>
    /// Tests that rules, selectors, and at-rules parse.
    /// </summary>
    [Fact]
    public void ParseText_RulesAndAtRules_Parsed()
    {
        var result = _service.ParseText(SampleSheet, "MainMenu.rcss");

        result.Success.Should().BeTrue();
        result.Data!.Rules.Should().HaveCount(2);
        result.Data.Rules[0].Selectors.Should().ContainSingle(s => s == ".screen");
        result.Data.Rules[0].Declarations.Should().HaveCount(2);
        result.Data.Rules[1].Selectors.Should().HaveCount(2);
        result.Data.Rules[1].Declarations.Should().Contain(d => d.Property == "margin" && d.Important);
        result.Data.AtRules.Should().ContainSingle(a => a.Name == "@font-face");
        result.Data.LeadingComments.Should().ContainSingle(c => c == "Screen theme");
    }

    /// <summary>
    /// Tests that an unclosed block fails with line information.
    /// </summary>
    [Fact]
    public void ParseText_UnclosedBlock_FailsWithLine()
    {
        var result = _service.ParseText(".screen {\n  color: white;\n");

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Line");
    }

    /// <summary>
    /// Tests that a declaration without a colon fails.
    /// </summary>
    [Fact]
    public void ParseText_DeclarationWithoutColon_Fails()
    {
        var result = _service.ParseText(".screen {\n  color white;\n}\n");

        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that statement at-rules round-trip opaquely.
    /// </summary>
    [Fact]
    public void ParseText_StatementAtRule_Preserved()
    {
        var result = _service.ParseText("@import \"common.rcss\";\n.screen { color: white; }\n");

        result.Success.Should().BeTrue();
        result.Data!.AtRules.Should().ContainSingle();
        var atRule = result.Data.AtRules.Single();
        atRule.Name.Should().Be("@import");
        atRule.HasBlock.Should().BeFalse();
        atRule.Prelude.Should().Be("\"common.rcss\"");
    }

    /// <summary>
    /// Tests that write output parses back to an equal structure.
    /// </summary>
    [Fact]
    public void WriteDocument_RoundTrips_Structure()
    {
        var parsed = _service.ParseText(SampleSheet).Data!;

        var written = _service.WriteDocument(parsed);
        var reparsed = _service.ParseText(written);

        reparsed.Success.Should().BeTrue();
        reparsed.Data!.Rules.Should().HaveCount(2);
        reparsed.Data.AtRules.Should().HaveCount(1);
        reparsed.Data.Rules.SelectMany(r => r.Declarations).Should().HaveCount(4);
    }

    /// <summary>
    /// Tests inline style parsing and serialization.
    /// </summary>
    [Fact]
    public void InlineStyle_RoundTrips()
    {
        var parsed = _service.ParseInlineStyle("color: white; margin: 4dp !important;");

        parsed.Success.Should().BeTrue();
        parsed.Data.Should().HaveCount(2);
        var written = _service.WriteInlineStyle(parsed.Data!);
        written.Should().Contain("margin: 4dp !important;");
    }

    /// <summary>
    /// Tests that an empty style sheet warns on validation.
    /// </summary>
    [Fact]
    public void ValidateDocument_Empty_Warns()
    {
        var parsed = _service.ParseText("/* nothing here */\n").Data!;

        var result = _service.ValidateDocument(parsed, "Empty.rcss");

        result.WarningIssueCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Tests that a clean style sheet validates.
    /// </summary>
    [Fact]
    public void ValidateDocument_Clean_IsValid()
    {
        var parsed = _service.ParseText(SampleSheet).Data!;

        var result = _service.ValidateDocument(parsed, "MainMenu.rcss");

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Tests that a quoted important marker is kept as content.
    /// </summary>
    [Fact]
    public void ParseText_QuotedImportant_NotStripped()
    {
        var result = _service.ParseText(".tip::after { content: \"!important\"; }\n");

        result.Success.Should().BeTrue();
        var declaration = result.Data!.Rules.Single().Declarations.Single();
        declaration.Value.Should().Be("\"!important\"");
        declaration.Important.Should().BeFalse();
    }

    /// <summary>
    /// Tests that comments inside declaration values are stripped.
    /// </summary>
    [Fact]
    public void ParseText_CommentInValue_Stripped()
    {
        var result = _service.ParseText(".screen { color: red /* bright */; }\n");

        result.Success.Should().BeTrue();
        result.Data!.Rules.Single().Declarations.Single().Value.Should().Be("red");
    }

    /// <summary>
    /// Tests that trailing comments stay on their own rule.
    /// </summary>
    [Fact]
    public void ParseText_TrailingComment_StaysOnRule()
    {
        var result = _service.ParseText(".first { color: red; /* trailing */ }\n.second { color: blue; }\n");

        result.Success.Should().BeTrue();
        result.Data!.Rules[0].TrailingComments.Should().ContainSingle(c => c == "trailing");
        result.Data.Rules[1].LeadingComments.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that at-rules with nested blocks round-trip opaquely.
    /// </summary>
    [Fact]
    public void ParseText_NestedAtRule_PreservedRaw()
    {
        var result = _service.ParseText("@media wide {\n  div { color: red; }\n}\n");

        result.Success.Should().BeTrue();
        var atRule = result.Data!.AtRules.Single();
        atRule.RawBody.Should().Contain("div { color: red; }");

        var rewritten = _service.WriteDocument(result.Data);
        var reparsed = _service.ParseText(rewritten);
        reparsed.Success.Should().BeTrue();
        reparsed.Data!.AtRules.Single().RawBody.Should().Contain("div { color: red; }");
    }

    /// <summary>
    /// Tests that a file round-trips through format.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task FormatFileAsync_RoundTripsAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "genhub-format-screen.rcss");
        await File.WriteAllTextAsync(path, SampleSheet);
        try
        {
            var result = await _service.FormatFileAsync(path);

            result.Success.Should().BeTrue();
            var reparsed = await _service.ParseFileAsync(path);
            reparsed.Success.Should().BeTrue();
            reparsed.Data!.Rules.Should().HaveCount(2);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
