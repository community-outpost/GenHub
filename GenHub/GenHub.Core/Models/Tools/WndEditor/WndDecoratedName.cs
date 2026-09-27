using GenHub.Core.Constants;
using System;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A decorated window name of the form file.wnd:WindowName.
/// </summary>
public sealed record WndDecoratedName
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndDecoratedName"/> class.
    /// </summary>
    /// <param name="fileName">The file portion, or null when absent.</param>
    /// <param name="shortName">The window portion.</param>
    public WndDecoratedName(string? fileName, string shortName)
    {
        FileName = fileName;
        ShortName = shortName;
    }

    /// <summary>
    /// Gets the file portion, or null when absent.
    /// </summary>
    public string? FileName { get; }

    /// <summary>
    /// Gets the window portion.
    /// </summary>
    public string ShortName { get; }

    /// <summary>
    /// Splits a NAME property value into file and window portions.
    /// </summary>
    /// <param name="value">The raw NAME property value.</param>
    /// <returns>The split name, never null.</returns>
    public static WndDecoratedName Parse(string? value)
    {
        var unquoted = WndValueTokenizer.Unquote(value ?? string.Empty);
        var separatorIndex = unquoted.IndexOf(WndConstants.Syntax.DecoratedNameSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return new WndDecoratedName(null, unquoted);
        }

        var fileName = unquoted.Substring(0, separatorIndex);
        var shortName = unquoted.Substring(separatorIndex + 1);
        return new WndDecoratedName(fileName, shortName);
    }

    /// <summary>
    /// Returns the quoted decorated representation.
    /// </summary>
    /// <returns>The quoted decorated representation.</returns>
    public override string ToString()
    {
        var decorated = FileName == null
            ? ShortName
            : string.Concat(FileName, WndConstants.Syntax.DecoratedNameSeparator, ShortName);
        return WndValueTokenizer.Quote(decorated);
    }
}
