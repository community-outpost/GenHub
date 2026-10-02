namespace GenHub.Core.Constants;

/// <summary>
/// Constants for GenHub build inspection, channels, categories, and identification.
/// </summary>
public static class GenHubBuildConstants
{
    /// <summary>
    /// The official company name for Community Outpost GenHub builds.
    /// </summary>
    public const string OfficialCompany = "Community Outpost";

    /// <summary>
    /// The official application product name.
    /// </summary>
    public const string OfficialProductName = "GenHub";

    /// <summary>
    /// Default content ID for official GenHub builds.
    /// </summary>
    public const string OfficialContentId = "genhub";

    /// <summary>
    /// Default content ID for development builds.
    /// </summary>
    public const string DevContentId = "genhub-dev";

    /// <summary>
    /// Default content ID for experimental / test builds.
    /// </summary>
    public const string TestContentId = "genhub-test";

    /// <summary>
    /// Default content ID for custom builds.
    /// </summary>
    public const string CustomContentId = "genhub-custom";

    /// <summary>
    /// Default name for development builds.
    /// </summary>
    public const string DevBuildName = "GenHub Development Build";

    /// <summary>
    /// Default name for test builds.
    /// </summary>
    public const string TestBuildName = "GenHub Test Build";

    /// <summary>
    /// Default name for custom third-party builds.
    /// </summary>
    public const string CustomBuildName = "GenHub (Custom Build)";

    /// <summary>
    /// Release channel name.
    /// </summary>
    public const string ChannelRelease = "Release";

    /// <summary>
    /// Pull request channel name.
    /// </summary>
    public const string ChannelPr = "PR";

    /// <summary>
    /// Development channel name.
    /// </summary>
    public const string ChannelDev = "Dev";

    /// <summary>
    /// Test channel name.
    /// </summary>
    public const string ChannelTest = "Test";

    /// <summary>
    /// Custom fork channel name.
    /// </summary>
    public const string ChannelCustomFork = "CustomFork";

    /// <summary>
    /// Category for standard release builds.
    /// </summary>
    public const string CategoryRelease = "Release";

    /// <summary>
    /// Category for test and PR builds.
    /// </summary>
    public const string CategoryTest = "Test";

    /// <summary>
    /// Category for development builds.
    /// </summary>
    public const string CategoryDev = "Dev";

    /// <summary>
    /// Category for custom community fork builds.
    /// </summary>
    public const string CategoryCustomFork = "CustomFork";

    /// <summary>
    /// Primary Windows binary executable file name.
    /// </summary>
    public const string WindowsExecutable = "GenHub.Windows.exe";

    /// <summary>
    /// Generic binary executable file name.
    /// </summary>
    public const string GenericExecutable = "GenHub.exe";

    /// <summary>
    /// Managed DLL assembly file name.
    /// </summary>
    public const string ManagedAssembly = "GenHub.dll";

    /// <summary>
    /// Setup executable file name.
    /// </summary>
    public const string SetupExecutable = "Setup.exe";

    /// <summary>
    /// Maximum safe file size in bytes (50 MB) when decompressing an entry from a build archive for inspection.
    /// </summary>
    public const long MaxArchiveEntrySizeBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Tag applied to all GenHub builds.
    /// </summary>
    public const string TagGenHub = "genhub";

    /// <summary>
    /// Tag applied to all build artifacts.
    /// </summary>
    public const string TagBuild = "build";

    /// <summary>
    /// Tag applied to fork builds.
    /// </summary>
    public const string TagFork = "fork";

    /// <summary>
    /// Tag applied to custom builds.
    /// </summary>
    public const string TagCustom = "custom";

    /// <summary>
    /// Tag applied to PR builds.
    /// </summary>
    public const string TagPr = "pr";

    /// <summary>
    /// Tag applied to development builds.
    /// </summary>
    public const string TagDev = "dev";

    /// <summary>
    /// Tag applied to test builds.
    /// </summary>
    public const string TagTest = "test";
}
