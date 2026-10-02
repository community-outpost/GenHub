using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Tools.RmlEditor;
using GenHub.Features.Tools.RmlEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Tests.Core.Features.Tools.RmlEditor.Services;

/// <summary>
/// Headless tests for <see cref="RmlPreviewBuilder"/>.
/// </summary>
public sealed class RmlPreviewBuilderTests
{
    private const string SampleDocument =
        "<rml>\n" +
        "  <head><title>T</title></head>\n" +
        "  <body>\n" +
        "    <div id=\"panel\" class=\"box\">\n" +
        "      <h1 id=\"title\">Hello</h1>\n" +
        "      <button id=\"ok\">Start</button>\n" +
        "      <img id=\"logo\" src=\"logo.tga\" />\n" +
        "      <div style=\"display: none;\" id=\"secret\">Hidden</div>\n" +
        "    </div>\n" +
        "  </body>\n" +
        "</rml>\n";

    private readonly RmlDocumentService _documents = new(new Mock<ILogger<RmlDocumentService>>().Object);
    private readonly RcssDocumentService _styles = new(new Mock<ILogger<RcssDocumentService>>().Object);
    private readonly Mock<ILocalizationService> _localization = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RmlPreviewBuilderTests"/> class.
    /// </summary>
    public RmlPreviewBuilderTests()
    {
        _localization.Setup(s => s.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => args is { Length: > 0 } ? $"{key} {string.Join(' ', args)}" : key);
    }

    /// <summary>
    /// Tests that elements map to preview controls keyed by identity.
    /// </summary>
    [AvaloniaFact]
    public void Build_MapsElementsToControls()
    {
        var document = ParseSample();
        var panel = document.FindById("panel")!;

        var result = CreateBuilder().Build(document, new RmlPreviewOptions());

        result.Root.Should().NotBeNull();
        result.ElementControls.Should().ContainKey(panel.Id);
        result.ElementControls.Should().ContainKey(document.FindById("title")!.Id);
        result.ElementControls.Should().ContainKey(document.FindById("ok")!.Id);
        result.ComputedStyles.Should().ContainKey(panel.Id);
    }

    /// <summary>
    /// Tests that style sheets flow into computed styles.
    /// </summary>
    [AvaloniaFact]
    public void Build_AppliesStyleSheets()
    {
        var document = ParseSample();
        var sheet = _styles.ParseText(".box { background-color: #ff0000; }").Data!;
        var panel = document.FindById("panel")!;
        var options = new RmlPreviewOptions { StyleSheets = new List<RcssDocument> { sheet } };

        var result = CreateBuilder().Build(document, options);

        result.ComputedStyles[panel.Id].GetProperty("background-color").Should().Be("#ff0000");
    }

    /// <summary>
    /// Tests that hidden elements are skipped unless requested.
    /// </summary>
    [AvaloniaFact]
    public void Build_HiddenElement_SkippedByDefault()
    {
        var document = ParseSample();
        var secret = document.FindById("secret")!;

        var hidden = CreateBuilder().Build(document, new RmlPreviewOptions());
        var shown = CreateBuilder().Build(document, new RmlPreviewOptions { ShowHidden = true });

        hidden.ElementControls.Should().NotContainKey(secret.Id);
        shown.ElementControls.Should().ContainKey(secret.Id);
    }

    /// <summary>
    /// Tests that missing images are reported.
    /// </summary>
    [AvaloniaFact]
    public void Build_MissingImage_Reported()
    {
        var document = ParseSample();

        var result = CreateBuilder().Build(document, new RmlPreviewOptions());

        result.MissingImages.Should().ContainSingle(i => i == "logo.tga");
        result.ElementControls.Should().ContainKey(document.FindById("logo")!.Id);
    }

    /// <summary>
    /// Tests that the selected element is highlighted in the preview tree.
    /// </summary>
    [AvaloniaFact]
    public void Build_SelectedElement_Highlighted()
    {
        var document = ParseSample();
        var panel = document.FindById("panel")!;
        var options = new RmlPreviewOptions { SelectedId = panel.Id };

        var result = CreateBuilder().Build(document, options);

        var frame = result.ElementControls[panel.Id].Should().BeOfType<Border>().Subject;
        frame.BorderBrush.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that absurd style lengths are clamped to sane maxima.
    /// </summary>
    [AvaloniaFact]
    public void Build_AbsurdLengths_Clamped()
    {
        var document = _documents.ParseText(
            "<rml><head><title>T</title></head><body>" +
            "<div id=\"wide\" style=\"width: 99999px;\"><p>Hi</p></div>" +
            "<p id=\"big\" style=\"font-size: 99999px;\">Hi</p>" +
            "</body></rml>").Data!;

        var result = CreateBuilder().Build(document, new RmlPreviewOptions());

        var wideFrame = result.ElementControls[document.FindById("wide")!.Id].Should().BeOfType<Border>().Subject;
        var wideInner = wideFrame.Child.Should().BeOfType<Border>().Subject;
        wideInner.Width.Should().Be(4096);
        var bigFrame = result.ElementControls[document.FindById("big")!.Id].Should().BeOfType<Border>().Subject;
        var bigInner = bigFrame.Child.Should().BeOfType<Border>().Subject;
        var panel = bigInner.Child.Should().BeOfType<StackPanel>().Subject;
        panel.Children.OfType<TextBlock>().First().FontSize.Should().Be(256);
    }

    /// <summary>
    /// Tests that table spans never exceed the built grid.
    /// </summary>
    [AvaloniaFact]
    public void Build_RowspanBeyondGrid_Clamped()
    {
        var document = _documents.ParseText(
            "<rml><head><title>T</title></head><body><table id=\"t\">" +
            "<tr><td rowspan=\"99\">Hi</td></tr>" +
            "<tr><td>Lo</td></tr>" +
            "</table></body></rml>").Data!;

        var result = CreateBuilder().Build(document, new RmlPreviewOptions());

        var frame = result.ElementControls[document.FindById("t")!.Id].Should().BeOfType<Border>().Subject;
        var inner = frame.Child.Should().BeOfType<Border>().Subject;
        var grid = inner.Child.Should().BeOfType<Grid>().Subject;
        grid.Children.Select(c => Grid.GetRowSpan(c)).Should().OnlyContain(span => span <= 2);
        grid.Children.Select(c => Grid.GetRowSpan(c)).Should().Contain(2);
    }

    private RmlPreviewBuilder CreateBuilder()
    {
        return new RmlPreviewBuilder(_styles, _localization.Object);
    }

    private RmlDocument ParseSample()
    {
        return _documents.ParseText(SampleDocument, "Sample.rml").Data!;
    }
}
