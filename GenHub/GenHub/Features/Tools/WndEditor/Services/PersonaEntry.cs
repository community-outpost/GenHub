namespace GenHub.Features.Tools.WndEditor.Services;

/// <summary>
/// A parsed challenge persona entry.
/// </summary>
/// <param name="PlayerTemplate">The player template name, if any.</param>
/// <param name="StartsEnabled">Whether the persona starts enabled.</param>
internal sealed record PersonaEntry(string? PlayerTemplate, bool StartsEnabled);
