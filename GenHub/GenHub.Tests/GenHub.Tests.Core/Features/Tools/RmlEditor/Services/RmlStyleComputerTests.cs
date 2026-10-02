using FluentAssertions;
using GenHub.Core.Models.Tools.RmlEditor;
using GenHub.Core.Services.Tools.RmlEditor;
using System.Collections.Generic;

namespace GenHub.Tests.Core.Features.Tools.RmlEditor.Services;

/// <summary>
/// Unit tests for <see cref="RmlStyleComputer"/>.
/// </summary>
public sealed class RmlStyleComputerTests
{
    private static readonly RcssDocument EmptySheet = new();

    /// <summary>
    /// Tests type, class, and id selector matching.
    /// </summary>
    [Fact]
    public void ComputeStyle_BasicSelectors_Match()
    {
        var element = Element("div", "main", "panel active");
        var sheets = new[] { Sheet(("div", "color", "red"), (".panel", "color", "green"), ("#main", "color", "blue")) };

        var computed = Compute(element, [], sheets);

        computed.GetProperty("color").Should().Be("blue");
    }

    /// <summary>
    /// Tests descendant and child combinators.
    /// </summary>
    [Fact]
    public void ComputeStyle_Combinators_MatchAncestors()
    {
        var root = Element("body");
        var parent = Element("div", classes: "panel");
        var child = Element("span");
        var ancestors = new List<RmlElement> { parent, root };
        var sheets = new[] { Sheet(("div span", "color", "red"), ("body > div", "margin", "1dp"), ("body > span", "padding", "2dp")) };

        var childComputed = Compute(child, ancestors, sheets);
        var parentComputed = Compute(parent, [root], sheets);

        childComputed.GetProperty("color").Should().Be("red");
        childComputed.GetProperty("padding").Should().BeNull();
        parentComputed.GetProperty("margin").Should().Be("1dp");
    }

    /// <summary>
    /// Tests that later rules win at equal specificity.
    /// </summary>
    [Fact]
    public void ComputeStyle_EqualSpecificity_LastWins()
    {
        var element = Element("p", classes: "a b");
        var sheets = new[] { Sheet((".a", "color", "red"), (".b", "color", "green")) };

        Compute(element, [], sheets).GetProperty("color").Should().Be("green");
    }

    /// <summary>
    /// Tests that important declarations beat specificity.
    /// </summary>
    [Fact]
    public void ComputeStyle_Important_BeatsSpecificity()
    {
        var document = new RcssDocument();
        var plain = new RcssRule();
        plain.Selectors.Add("#main");
        plain.Declarations.Add(new RcssDeclaration("color", "blue"));
        var important = new RcssRule();
        important.Selectors.Add("div");
        important.Declarations.Add(new RcssDeclaration("color", "red", true));
        document.Rules.Add(plain);
        document.Rules.Add(important);

        Compute(Element("div", "main"), [], document).GetProperty("color").Should().Be("red");
    }

    /// <summary>
    /// Tests that inline declarations beat style sheets.
    /// </summary>
    [Fact]
    public void ComputeStyle_Inline_BeatsSheets()
    {
        var element = Element("div", "main");
        var sheets = new[] { Sheet(("#main", "color", "blue")) };
        var inline = new List<RcssDeclaration> { new("color", "yellow") };

        RmlStyleComputer.ComputeStyle(element, [], sheets, inline).GetProperty("color").Should().Be("yellow");
    }

    /// <summary>
    /// Tests that pseudo-class selectors are skipped in static matching.
    /// </summary>
    [Fact]
    public void ComputeStyle_PseudoClass_Skipped()
    {
        var element = Element("button");
        var sheets = new[] { Sheet(("button:hover", "color", "red")) };

        Compute(element, [], sheets).GetProperty("color").Should().BeNull();
    }

    /// <summary>
    /// Tests attribute selector matching.
    /// </summary>
    [Fact]
    public void ComputeStyle_AttributeSelector_Matches()
    {
        var element = Element("input");
        element.SetAttribute("type", "text");
        var sheets = new[] { Sheet(("input[type=\"text\"]", "color", "red"), ("input[disabled]", "color", "gray")) };

        Compute(element, [], sheets).GetProperty("color").Should().Be("red");
    }

    /// <summary>
    /// Tests that matched rules are recorded for the inspector.
    /// </summary>
    [Fact]
    public void ComputeStyle_RecordsMatchedRules()
    {
        var element = Element("div", "main", "panel");
        var sheets = new[] { Sheet(("#main", "color", "blue"), (".panel", "margin", "1dp"), ("span", "color", "red")) };

        var computed = Compute(element, [], sheets);

        computed.MatchedRules.Should().HaveCount(2);
    }

    private static RmlElement Element(string tag, string? id = null, string? classes = null)
    {
        var element = new RmlElement { Tag = tag };
        if (id is not null)
        {
            element.SetAttribute("id", id);
        }

        if (classes is not null)
        {
            element.SetAttribute("class", classes);
        }

        return element;
    }

    private static RcssDocument Sheet(params (string Selector, string Property, string Value)[] entries)
    {
        var document = new RcssDocument();
        foreach (var (selector, property, value) in entries)
        {
            var rule = new RcssRule();
            rule.Selectors.Add(selector);
            rule.Declarations.Add(new RcssDeclaration(property, value));
            document.Rules.Add(rule);
        }

        return document;
    }

    private static RmlComputedStyle Compute(RmlElement element, IReadOnlyList<RmlElement> ancestors, params RcssDocument[] sheets)
    {
        return RmlStyleComputer.ComputeStyle(element, ancestors, sheets, []);
    }
}
