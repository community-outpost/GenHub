using System;
using System.Collections.Generic;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Results;

/// <summary>Represents a single result from a content search operation.</summary>
public class ContentSearchResult
{
    /// <summary>Gets or sets the unique identifier of the content.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the content.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a brief description of the content.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the rich data payload. For discoverers with SupportsManifestGeneration capability, this can contain the complete ContentManifest to avoid data loss.</summary>
    public object? Data { get; set; }

    /// <summary>Gets or sets the version of the content.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets the type of the content (e.g., Mod, Map).</summary>
    public ContentType ContentType { get; set; }

    /// <summary>Gets or sets a value indicating whether the ContentType (or other inferred fields) were produced by an automatic heuristic and should be considered a guess that the user can override.</summary>
    public bool IsInferred { get; set; } = false;

    /// <summary>Gets or sets the game this content is for (e.g., Generals, ZeroHour).</summary>
    public GameType TargetGame { get; set; }

    /// <summary>Gets or sets the name of the provider that supplied this result.</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the content author or publisher, if available.</summary>
    public string? AuthorName { get; set; }

    /// <summary>Gets or sets the URL for the content's icon (optional).</summary>
    public string? IconUrl { get; set; }

    /// <summary>Gets a list of screenshot URLs.</summary>
    public IList<string> ScreenshotUrls { get; } = new List<string>();

    /// <summary>Gets a list of tags associated with the content.</summary>
    public IList<string> Tags { get; } = new List<string>();

    /// <summary>Gets or sets the date the content was last updated.</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>Gets or sets the download size in bytes.</summary>
    public long DownloadSize { get; set; }

    /// <summary>Gets or sets the total number of downloads.</summary>
    public int DownloadCount { get; set; }

    /// <summary>Gets or sets the user rating (e.g., 1-5).</summary>
    public float Rating { get; set; }

    /// <summary>Gets a dictionary for provider-specific metadata.</summary>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

    /// <summary>Gets or sets a value indicating whether this content is currently installed.</summary>
    public bool IsInstalled { get; set; }

    /// <summary>Gets or sets a value indicating whether an update is available for this content.</summary>
    public bool HasUpdate { get; set; }

    /// <summary>Gets or sets a value indicating whether this is a partial result that needs resolution to get full details.</summary>
    public bool RequiresResolution { get; set; }

    /// <summary>Gets or sets the resolver ID needed to get full content details (if RequiresResolution is true).</summary>
    public string? ResolverId { get; set; }

    /// <summary>Gets or sets the source URL for resolution (e.g., specific mod page URL).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Gets additional metadata for resolvers.</summary>
    public IDictionary<string, string> ResolverMetadata { get; } = new Dictionary<string, string>();

    /// <summary>Returns the data payload cast to type T, or null if unavailable or of wrong type.</summary>
    /// <typeparam name="T">Expected type of the data payload.</typeparam>
    /// <returns>The typed data or null.</returns>
    public T? GetData<T>()
        where T : class
        => Data as T;

    /// <summary>Sets the data payload with type safety.</summary>
    /// <typeparam name="T">Type of the data payload.</typeparam>
    /// <param name="data">The data to store.</param>
    public void SetData<T>(T data)
        where T : class
        => Data = data;
}
