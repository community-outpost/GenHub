using System;
using System.Collections.Generic;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Event raised when content is about to be removed.
/// Allows listeners to prepare (e.g., close open files, save state).
/// </summary>
public record ContentRemovingEvent(
    string ManifestId,
    string? ManifestName,
    string Reason);
