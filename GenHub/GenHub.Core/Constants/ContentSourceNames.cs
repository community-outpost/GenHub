namespace GenHub.Core.Constants;

/// <summary>
/// Constants for content pipeline component identifiers.
/// </summary>
public static class ContentSourceNames
{
    // Discoverers

    /// <summary>
    /// Source name for CNC Labs map discoverer.
    /// </summary>
    public const string CNCLabsDiscoverer = "CNC Labs Maps";

    /// <summary>
    /// Source name for GitHub content discoverer.
    /// </summary>
    public const string GitHubDiscoverer = "GitHub";

    /// <summary>
    /// Source name for GitHub releases discoverer.
    /// </summary>
    public const string GitHubReleasesDiscoverer = "GitHub Releases";

    /// <summary>
    /// Source name for local file system discoverer.
    /// </summary>
    public const string FileSystemDiscoverer = "Local File System";

    /// <summary>
    /// Source name for ModDB content discoverer.
    /// </summary>
    public const string ModDBDiscoverer = "ModDB";

    // Resolvers

    /// <summary>
    /// Resolver ID for CNC Labs map resolver.
    /// </summary>
    public const string CNCLabsResolverId = "CNCLabsMap";

    /// <summary>
    /// Resolver ID for GitHub release resolver.
    /// </summary>
    public const string GitHubResolverId = "GitHubRelease";

    /// <summary>
    /// Resolver ID for local manifest resolver.
    /// </summary>
    public const string LocalResolverId = "LocalManifest";

    /// <summary>
    /// Resolver ID for ModDB resolver.
    /// </summary>
    public const string ModDBResolverId = "ModDB";

    // Deliverers

    /// <summary>
    /// Source name for GitHub content deliverer.
    /// </summary>
    public const string GitHubDeliverer = "GitHub Content Deliverer";

    /// <summary>
    /// Source name for HTTP content deliverer.
    /// </summary>
    public const string HttpDeliverer = "HTTP Content Deliverer";

    /// <summary>
    /// Description for HTTP content deliverer.
    /// </summary>
    public const string HttpDelivererDescription = "Delivers content from HTTP/HTTPS URLs";

    /// <summary>
    /// Source name for local file system deliverer.
    /// </summary>
    public const string FileSystemDeliverer = "Local File System Deliverer";
}