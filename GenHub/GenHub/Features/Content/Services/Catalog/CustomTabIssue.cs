namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// A rule broken by a publisher-defined custom tab.
/// </summary>
/// <param name="Kind">The rule that was broken.</param>
/// <param name="TabIndex">The index of the tab in its scope, or -1 for a scope-level issue.</param>
/// <param name="TabId">The tab ID, when the tab has one.</param>
/// <param name="CardIndex">The index of the card, for card issues.</param>
/// <param name="Count">The number of tabs or cards, for count issues.</param>
/// <param name="ImageUrl">The rejected image URL, for image issues.</param>
public sealed record CustomTabIssue(
    CustomTabIssueKind Kind,
    int TabIndex,
    string? TabId = null,
    int? CardIndex = null,
    int? Count = null,
    string? ImageUrl = null);
