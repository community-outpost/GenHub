using GenHub.Core.Models.Enums;

namespace GenHub.Core.Helpers;

/// <summary>
/// Parsed target of a <c>genhub://map/import</c> or <c>genhub://replay/import</c> share URI.
/// </summary>
/// <param name="ToolCommand">The tool path segment (<c>map</c> or <c>replay</c>).</param>
/// <param name="Url">The decoded absolute download URL carried by the share URI.</param>
/// <param name="Game">The optional target game carried by the share URI.</param>
public sealed record ToolShareTarget(string ToolCommand, string Url, GameType? Game);
