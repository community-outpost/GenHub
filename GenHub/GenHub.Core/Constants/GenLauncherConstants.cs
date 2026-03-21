namespace GenHub.Core.Constants;

/// <summary>
/// Constants for GenLauncher file normalization.
/// </summary>
public static class GenLauncherConstants
{
    /// <summary>
    /// GenLauncher Replace suffix - appended to original game files when temporarily disabled.
    /// </summary>
    public const string ReplaceSuffix = ".GLR";

    /// <summary>
    /// GenLauncher Original File suffix - backup suffix for original files before modification.
    /// </summary>
    public const string OriginalFileSuffix = ".GOF";

    /// <summary>
    /// GenLauncher Temp Copy suffix - temporary folder suffix for version copies.
    /// </summary>
    public const string TempCopySuffix = ".GLTC";

    /// <summary>
    /// GenLauncher scrambled .big file extension.
    /// </summary>
    public const string GibExtension = ".gib";

    /// <summary>
    /// Standard .big file extension.
    /// </summary>
    public const string BigExtension = ".big";

    /// <summary>
    /// All GenLauncher suffixes that should be removed during normalization.
    /// </summary>
    public static readonly string[] AllSuffixes =
    [
        ReplaceSuffix,
        OriginalFileSuffix,
        TempCopySuffix,
    ];

    /// <summary>
    /// Session key for "do not ask again" preference for normalization dialog.
    /// </summary>
    public const string NormalizationDialogSessionKey = "genlauncher.normalization.skip";
}
