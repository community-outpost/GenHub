namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// One color segment of a parsed message of the day.
/// </summary>
/// <param name="Text">The display text of the segment.</param>
/// <param name="ColorHex">The segment color as #RRGGBB, or null for the default text color.</param>
public sealed record GeneralsOnlineMotdRun(string Text, string? ColorHex);
