using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A mapped image definition: a named source rectangle on a GUI texture page.
/// Mirrors the engine MappedImage INI blocks under Data\INI\MappedImages.
/// </summary>
public sealed partial record WndMappedImage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndMappedImage"/> class.
    /// </summary>
    /// <param name="name">The mapped image name referenced by DrawData entries.</param>
    /// <param name="texture">The texture page file name.</param>
    /// <param name="left">The source rectangle left edge in pixels.</param>
    /// <param name="top">The source rectangle top edge in pixels.</param>
    /// <param name="right">The source rectangle right edge in pixels.</param>
    /// <param name="bottom">The source rectangle bottom edge in pixels.</param>
    /// <param name="isRotated">Whether the packed content is rotated 90 degrees clockwise.</param>
    /// <param name="textureWidth">The texture page width the coordinates are defined against (0 when absent).</param>
    /// <param name="textureHeight">The texture page height the coordinates are defined against (0 when absent).</param>
    public WndMappedImage(string name, string texture, int left, int top, int right, int bottom, bool isRotated, int textureWidth = 0, int textureHeight = 0)
    {
        Name = name;
        Texture = texture;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        IsRotated = isRotated;
        TextureWidth = textureWidth;
        TextureHeight = textureHeight;
    }

    /// <summary>
    /// Gets the mapped image name referenced by DrawData entries.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the texture page file name.
    /// </summary>
    public string Texture { get; }

    /// <summary>
    /// Gets the source rectangle left edge in pixels.
    /// </summary>
    public int Left { get; }

    /// <summary>
    /// Gets the source rectangle top edge in pixels.
    /// </summary>
    public int Top { get; }

    /// <summary>
    /// Gets the source rectangle right edge in pixels.
    /// </summary>
    public int Right { get; }

    /// <summary>
    /// Gets the source rectangle bottom edge in pixels.
    /// </summary>
    public int Bottom { get; }

    /// <summary>
    /// Gets a value indicating whether the packed content is rotated 90 degrees clockwise.
    /// </summary>
    public bool IsRotated { get; }

    /// <summary>
    /// Gets the texture page width the coordinates are defined against (0 when absent).
    /// </summary>
    public int TextureWidth { get; }

    /// <summary>
    /// Gets the texture page height the coordinates are defined against (0 when absent).
    /// </summary>
    public int TextureHeight { get; }

    /// <summary>
    /// Gets the source rectangle width in pixels.
    /// </summary>
    public int Width => Right - Left;

    /// <summary>
    /// Gets the source rectangle height in pixels.
    /// </summary>
    public int Height => Bottom - Top;

    /// <summary>
    /// Parses mapped image definitions from INI text, skipping malformed blocks.
    /// </summary>
    /// <param name="content">The raw INI content.</param>
    /// <returns>The parsed definitions.</returns>
    public static IReadOnlyList<WndMappedImage> ParseDefinitions(string content)
    {
        var images = new List<WndMappedImage>();
        var builder = new BlockBuilder();
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseBlockStart(line, out var name))
            {
                builder.Start(name);
                continue;
            }

            if (!builder.InsideBlock)
            {
                continue;
            }

            if (string.Equals(line, WndConstants.MappedImages.EndTag, StringComparison.OrdinalIgnoreCase))
            {
                var image = builder.Build();
                if (image != null)
                {
                    images.Add(image);
                }

                builder.Reset();
                continue;
            }

            builder.ApplyField(line);
        }

        return images;
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf(WndConstants.MappedImages.CommentPrefix, StringComparison.Ordinal);
        return index < 0 ? line : line[..index];
    }

    private static bool TryParseBlockStart(string line, out string name)
    {
        name = string.Empty;
        var tag = WndConstants.MappedImages.BlockTag;
        if (!line.StartsWith(tag, StringComparison.OrdinalIgnoreCase) || line.Length <= tag.Length)
        {
            return false;
        }

        if (!char.IsWhiteSpace(line[tag.Length]))
        {
            return false;
        }

        name = line[tag.Length..].Trim();
        return name.Length > 0;
    }

    [GeneratedRegex(@"\b(?<attr>Left|Top|Right|Bottom)\b\s*[:=]\s*(?<val>-?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CoordsRegex();

    private sealed class BlockBuilder
    {
        private string? _name;
        private string? _texture;
        private int _left;
        private int _top;
        private int _right;
        private int _bottom;
        private bool _hasCoords;
        private bool _isRotated;
        private int _textureWidth;
        private int _textureHeight;

        public bool InsideBlock => _name != null;

        public void Start(string name)
        {
            Reset();
            _name = name;
        }

        public void Reset()
        {
            _name = null;
            _texture = null;
            _left = 0;
            _top = 0;
            _right = 0;
            _bottom = 0;
            _hasCoords = false;
            _isRotated = false;
            _textureWidth = 0;
            _textureHeight = 0;
        }

        public void ApplyField(string line)
        {
            var separator = line.IndexOf(WndConstants.MappedImages.KeySeparator, StringComparison.Ordinal);
            if (separator <= 0)
            {
                return;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            if (string.Equals(key, WndConstants.MappedImages.TextureField, StringComparison.OrdinalIgnoreCase))
            {
                _texture = value;
            }
            else if (string.Equals(key, WndConstants.MappedImages.CoordsField, StringComparison.OrdinalIgnoreCase))
            {
                ApplyCoords(value);
            }
            else if (string.Equals(key, WndConstants.MappedImages.StatusField, StringComparison.OrdinalIgnoreCase))
            {
                _isRotated = value.Contains(WndConstants.MappedImages.RotatedStatus, StringComparison.OrdinalIgnoreCase);
            }
            else if (string.Equals(key, WndConstants.MappedImages.TextureWidthField, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var textureWidth))
            {
                _textureWidth = textureWidth;
            }
            else if (string.Equals(key, WndConstants.MappedImages.TextureHeightField, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var textureHeight))
            {
                _textureHeight = textureHeight;
            }
        }

        public WndMappedImage? Build()
        {
            if (_name == null || string.IsNullOrWhiteSpace(_texture))
            {
                return null;
            }

            if (!_hasCoords || _right <= _left || _bottom <= _top)
            {
                return null;
            }

            return new WndMappedImage(_name, _texture, _left, _top, _right, _bottom, _isRotated, _textureWidth, _textureHeight);
        }

        private void ApplyCoords(string value)
        {
            var left = 0;
            var top = 0;
            var right = 0;
            var bottom = 0;
            var found = 0;

            foreach (var groups in CoordsRegex().Matches(value).Select(static m => m.Groups))
            {
                var attr = groups["attr"].Value;
                if (!int.TryParse(groups["val"].Value, out var number))
                {
                    continue;
                }

                if (string.Equals(attr, WndConstants.MappedImages.LeftAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    left = number;
                    found++;
                }
                else if (string.Equals(attr, WndConstants.MappedImages.TopAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    top = number;
                    found++;
                }
                else if (string.Equals(attr, WndConstants.MappedImages.RightAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    right = number;
                    found++;
                }
                else if (string.Equals(attr, WndConstants.MappedImages.BottomAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    bottom = number;
                    found++;
                }
            }

            if (found >= 4)
            {
                _left = left;
                _top = top;
                _right = right;
                _bottom = bottom;
                _hasCoords = true;
            }
        }
    }
}
