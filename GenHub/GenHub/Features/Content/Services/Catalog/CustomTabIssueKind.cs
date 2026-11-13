namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Identifies a rule broken by a publisher-defined custom tab.
/// </summary>
public enum CustomTabIssueKind
{
    /// <summary>The scope declares more tabs than allowed.</summary>
    TooManyTabs,

    /// <summary>A tab entry is null.</summary>
    NullTab,

    /// <summary>A tab has no ID.</summary>
    MissingTabId,

    /// <summary>A tab ID is longer than allowed.</summary>
    TabIdTooLong,

    /// <summary>A tab ID is used more than once in the scope.</summary>
    DuplicateTabId,

    /// <summary>A tab has no header.</summary>
    MissingHeader,

    /// <summary>A tab header is longer than allowed.</summary>
    HeaderTooLong,

    /// <summary>A tab intro is longer than allowed.</summary>
    IntroTooLong,

    /// <summary>A tab body is longer than allowed.</summary>
    BodyTooLong,

    /// <summary>A tab declares more cards than allowed.</summary>
    TooManyCards,

    /// <summary>A card entry is null.</summary>
    NullCard,

    /// <summary>A card has no title.</summary>
    CardMissingTitle,

    /// <summary>A card title or label is longer than allowed.</summary>
    CardTitleTooLong,

    /// <summary>A card description is longer than allowed.</summary>
    CardDescriptionTooLong,

    /// <summary>A card image URL does not use an allowed scheme.</summary>
    InvalidCardImageUrl,
}
