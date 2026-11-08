namespace GenHub.Core.Models.Enums;

/// <summary>
/// Describes how a content manifest's files are sourced.
/// </summary>
public enum ContentSourceMode
{
    /// <summary>
    /// Files were captured into content-addressable storage at import time (the default).
    /// </summary>
    Snapshot = 0,

    /// <summary>
    /// Files stay in the registered local folder and are materialized as links to that
    /// folder. Used by developer mode for loose-file iteration; no CAS copy exists.
    /// </summary>
    Live = 1,
}
