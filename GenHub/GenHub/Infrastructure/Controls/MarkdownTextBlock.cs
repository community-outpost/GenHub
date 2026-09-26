using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Infrastructure.Controls;

/// <summary>
/// A control that renders Markdown text with proper formatting.
/// </summary>
public class MarkdownTextBlock : UserControl
{
    /// <summary>
    /// Defines the <see cref="Markdown"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string?>(nameof(Markdown));

    private static readonly IValueConverter PositiveWidthConverter =
        new FuncValueConverter<double, double>(w => w > 1.0 ? w : double.PositiveInfinity);

    private static readonly Lazy<MarkdownPipeline> CachedPipeline = new(() =>
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions();
        builder.BlockParsers.RemoveAll(p => p is Markdig.Parsers.IndentedCodeBlockParser);
        return builder.Build();
    });

    /// <summary>
    /// Gets or sets the Markdown text to render.
    /// </summary>
    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    static MarkdownTextBlock()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.UpdateContent());
    }

    private static Control RenderCodeBlock(FencedCodeBlock fenced)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
        };

        var textBlock = new TextBlock
        {
            Text = fenced.Lines.ToString(),
            FontFamily = new FontFamily("Consolas,Courier New,monospace"),
            Foreground = new SolidColorBrush(Color.Parse("#ABB2BF")),
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap,
        };

        border.Child = textBlock;

        return new ScrollViewer
        {
            Content = border,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 8),
        };
    }

    private static Control RenderFallbackCodeBlock(CodeBlock code)
    {
        var text = code.Lines.ToString().TrimEnd();
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#DDDDDD")),
            FontSize = 14,
            LineHeight = 22,
            Margin = new Thickness(0, 0, 0, 8),
        };
    }

    private static string GetInlineText(ContainerInline? inline)
    {
        if (inline == null)
        {
            return string.Empty;
        }

        var text = string.Empty;
        foreach (var child in inline)
        {
            text += child switch
            {
                LiteralInline literal => literal.Content.ToString(),
                ContainerInline container => GetInlineText(container),
                CodeInline code => code.Content,
                _ => child.ToString(),
            };
        }

        return text;
    }

    private static TextBlock RenderHeading(HeadingBlock heading)
    {
        var textBlock = new TextBlock
        {
            Text = GetInlineText(heading.Inline),
            FontWeight = FontWeight.Bold,
            FontSize = heading.Level switch
            {
                1 => 24,
                2 => 20,
                3 => 18,
                _ => 16,
            },
            Foreground = Brushes.White,
            Margin = new Thickness(0, heading.Level == 1 ? 16 : 12, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        return textBlock;
    }

    private static void RenderInlines(ContainerInline? container, Avalonia.Controls.Documents.InlineCollection inlines)
    {
        if (container == null)
        {
            return;
        }

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    inlines.Add(new Avalonia.Controls.Documents.Run(literal.Content.ToString()));
                    break;
                case EmphasisInline emphasis:
                    var run = new Avalonia.Controls.Documents.Run(GetInlineText(emphasis));
                    if (emphasis.DelimiterCount == 2)
                    {
                        run.FontWeight = FontWeight.Bold;
                    }
                    else
                    {
                        run.FontStyle = FontStyle.Italic;
                    }

                    inlines.Add(run);
                    break;
                case LinkInline link:
                    var linkRun = new Avalonia.Controls.Documents.Run(GetInlineText(link))
                    {
                        Foreground = new SolidColorBrush(Color.Parse("#61AFEF")),
                        TextDecorations = TextDecorations.Underline,
                    };

                    // Make the link clickable and constrain width to prevent paragraph overflow on unbroken URLs
                    var linkText = new TextBlock
                    {
                        Cursor = new Cursor(StandardCursorType.Hand),
                        TextWrapping = TextWrapping.Wrap,
                    };

                    linkText.Inlines?.Add(linkRun);

                    if (!string.IsNullOrEmpty(link.Url))
                    {
                        ToolTip.SetTip(linkText, link.Url);
                    }

                    linkText.Bind(
                        TextBlock.MaxWidthProperty,
                        new Avalonia.Data.Binding
                        {
                            Path = "Bounds.Width",
                            Converter = PositiveWidthConverter,
                            FallbackValue = double.PositiveInfinity,
                            TargetNullValue = double.PositiveInfinity,
                            RelativeSource = new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor)
                            {
                                AncestorType = typeof(TextBlock),
                            },
                        });

                    linkText.PointerPressed += (s, e) =>
                    {
                        // Only allow http/https URLs for security
                        if (!string.IsNullOrEmpty(link.Url) &&
                            Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) &&
                            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = link.Url,
                                    UseShellExecute = true,
                                });
                            }
                            catch
                            {
                                // Silently fail if link can't be opened
                            }
                        }
                    };

                    // Add as inline container
                    inlines.Add(new Avalonia.Controls.Documents.InlineUIContainer { Child = linkText });
                    break;
                case CodeInline code:
                    inlines.Add(new Avalonia.Controls.Documents.Run(code.Content)
                    {
                        FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                        Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
                        Foreground = new SolidColorBrush(Color.Parse("#E06C75")),
                    });
                    break;
                case LineBreakInline:
                    inlines.Add(new Avalonia.Controls.Documents.LineBreak());
                    break;
                default:
                    if (inline is ContainerInline containerInline)
                    {
                        RenderInlines(containerInline, inlines);
                    }
                    else
                    {
                        inlines.Add(new Avalonia.Controls.Documents.Run(inline.ToString()));
                    }

                    break;
            }
        }
    }

    private static Control RenderBlock(Block block)
    {
        if (block is LinkReferenceDefinitionGroup)
        {
            return new Control { IsVisible = false };
        }

        return block switch
        {
            HeadingBlock heading => RenderHeading(heading),
            ParagraphBlock paragraph => RenderParagraph(paragraph),
            ListBlock list => RenderList(list),
            FencedCodeBlock fenced => RenderCodeBlock(fenced),
            CodeBlock code => RenderFallbackCodeBlock(code),
            _ => new TextBlock { Text = block.ToString(), TextWrapping = TextWrapping.Wrap, },
        };
    }

    private static TextBlock RenderParagraph(ParagraphBlock paragraph)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#DDDDDD")),
            FontSize = 14,
            LineHeight = 22,
            Margin = new Thickness(0, 0, 0, 8),
        };

        if (textBlock.Inlines != null)
        {
            RenderInlines(paragraph.Inline, textBlock.Inlines);
        }

        return textBlock;
    }

    private static StackPanel RenderList(ListBlock list)
    {
        var stackPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 4), };

        var index = 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var itemGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto, *"),
                Margin = new Thickness(0, 0, 0, 4),
            };

            var bullet = new TextBlock
            {
                Text = list.IsOrdered ? $"{index++}." : "•",
                Foreground = new SolidColorBrush(Color.Parse("#888888")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(16, 0, 8, 0),
            };

            var contentPanel = new StackPanel { Spacing = 4, };
            foreach (var block in item)
            {
                contentPanel.Children.Add(RenderBlock(block));
            }

            Grid.SetColumn(bullet, 0);
            Grid.SetColumn(contentPanel, 1);

            itemGrid.Children.Add(bullet);
            itemGrid.Children.Add(contentPanel);
            stackPanel.Children.Add(itemGrid);
        }

        return stackPanel;
    }

    private static int CalculateCommonIndent(string[] lines)
    {
        var minIndent = int.MaxValue;
        var inFencedCode = false;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFencedCode = !inFencedCode;
                continue;
            }

            if (!inFencedCode && !string.IsNullOrWhiteSpace(rawLine))
            {
                var indent = rawLine.Length - trimmed.Length;
                if (indent < minIndent)
                {
                    minIndent = indent;
                }
            }
        }

        return minIndent;
    }

    private static string NormalizeMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var minIndent = CalculateCommonIndent(lines);

        if (minIndent > 0 && minIndent < int.MaxValue)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var leadingSpaces = lines[i].Length - lines[i].TrimStart().Length;
                var spacesToStrip = Math.Min(leadingSpaces, minIndent);
                if (spacesToStrip > 0)
                {
                    lines[i] = lines[i][spacesToStrip..];
                }
                else if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    lines[i] = string.Empty;
                }
            }
        }

        return string.Join("\n", lines);
    }

    private void UpdateContent()
    {
        if (string.IsNullOrWhiteSpace(Markdown))
        {
            Content = null;
            return;
        }

        var normalized = NormalizeMarkdown(Markdown);
        var document = Markdig.Markdown.Parse(normalized, CachedPipeline.Value);

        var stackPanel = new StackPanel { Spacing = 8, };

        foreach (var block in document)
        {
            stackPanel.Children.Add(RenderBlock(block));
        }

        Content = stackPanel;
    }
}
