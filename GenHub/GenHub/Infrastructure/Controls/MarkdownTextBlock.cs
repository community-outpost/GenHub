using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

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

    private static Control RenderCodeBlock(CodeBlock code)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
        };

        var textBlock = new TextBlock
        {
            Text = code is FencedCodeBlock fenced ? fenced.Lines.ToString() : code.Lines.ToString(),
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

                    // Make the link clickable
                    var linkText = new TextBlock
                    {
                        Cursor = new Cursor(StandardCursorType.Hand),
                    };

                    linkText.Inlines?.Add(linkRun);

                    linkText.PointerPressed += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(link.Url))
                        {
                            // Only allow http/https URLs for security
                            if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) &&
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
            CodeBlock code => RenderCodeBlock(code),
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

    private void UpdateContent()
    {
        if (string.IsNullOrWhiteSpace(Markdown))
        {
            Content = null;
            return;
        }

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var document = Markdig.Markdown.Parse(Markdown, pipeline);

        var stackPanel = new StackPanel { Spacing = 8, };

        foreach (var block in document)
        {
            stackPanel.Children.Add(RenderBlock(block));
        }

        Content = stackPanel;
    }
}
