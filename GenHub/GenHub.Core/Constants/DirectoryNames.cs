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

    /// <summary>
    /// Directory for temporary files.
    /// </summary>
    public const string Temp = "Temp";

    /// <summary>
    /// Directory for log files.
    /// </summary>
    public const string Logs = "Logs";

    /// <summary>
    /// Directory for storing backup files.
    /// </summary>
    public const string Backups = "Backups";

    /// <summary>
    /// Directory for storing game profiles.
    /// </summary>
    public const string Profiles = "Profiles";

    /// <summary>
    /// Directory that releases up to v0.0.3 nested the manifests, tracked user data and workspace
    /// metadata under. Current releases keep those entries directly in the data root.
    /// </summary>
    public const string LegacyContent = "Content";

    /// <summary>
    /// Directory for storing tracked user data.
    /// </summary>
    public const string UserData = "UserData";

    /// <summary>
    /// Directory holding the manifests of tracked user data, nested inside <see cref="UserData"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately lower-case and separate from <see cref="FileTypes.ManifestsDirectory"/>: this is
    /// the exact name written to disk, and matching case matters on case-sensitive filesystems.
    /// </remarks>
    public const string UserDataManifests = "manifests";

    /// <summary>
    /// Directory holding backups of replaced user data files, nested inside <see cref="UserData"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately lower-case and separate from <see cref="Backups"/>: this is the exact name
    /// written to disk, and matching case matters on case-sensitive filesystems.
    /// </remarks>
    public const string UserDataBackups = "backups";

    /// <summary>
    /// Directory for storing workspaces.
    /// </summary>
    public const string Workspaces = "Workspaces";

    /// <summary>
    /// Directory for storing tool workspaces.
    /// </summary>
    public const string ToolWorkspaces = "ToolWorkspaces";
}
