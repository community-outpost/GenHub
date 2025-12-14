namespace GenHub.Core.Constants;

/// <summary>
/// Directory names used for organizing content storage.
/// </summary>
public static class DirectoryNames
{
    /// <summary>
    /// Directory for storing content data.
    /// </summary>
    public const string Data = "Data";

    /// <summary>
    /// Directory for storing cache files.
    /// </summary>
    public const string Cache = "Cache";

    /// <summary>
    /// Directory for Content-Addressable Storage (CAS) pool.
    /// </summary>
    public const string CasPool = "cas-pool";

    /// <summary>
    /// Directory name for GenHub CAS pool adjacent to game installations.
    /// </summary>
    public const string GenHubCasPool = ".genhub-cas";

    /// <summary>
    /// Directory name for GenHub workspace adjacent to game installations.
    /// </summary>
    public const string GenHubWorkspace = ".genhub-workspace";
}
