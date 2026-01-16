namespace GenHub.Core.Constants;

/// <summary>
/// Constants for Action Sets and Fixes.
/// </summary>
public static class ActionSetConstants
{
    /// <summary>
    /// The directory name for sub-markers.
    /// </summary>
    public const string SubMarkersDirectory = "sub_markers";

    /// <summary>
    /// The minimum file size for VCRedist 2008 installer (bytes).
    /// </summary>
    public const long MinimumVCRedist2008FileSize = 1000 * 1024;

    /// <summary>
    /// File names used in action sets.
    /// </summary>
    public static class FileNames
    {
        /// <summary>
        /// The filename for Generals.exe.
        /// </summary>
        public const string GeneralsExe = "generals.exe";

        /// <summary>
        /// The filename for GeneralsZh.exe.
        /// </summary>
        public const string GeneralsZhExe = "generalszh.exe";
    }
}
