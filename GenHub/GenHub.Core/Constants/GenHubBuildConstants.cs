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
    /// Default fallback version string for builds where version cannot be determined.
    /// </summary>
    public const string DefaultVersion = "1.0.0";

    /// <summary>
    /// Default content ID for official GenHub builds.
    /// </summary>
    public const string OfficialContentId = "genhub";

    /// <summary>
    /// Tag identifier for genhub-build tag.
    /// </summary>
    public const string GenHubBuildTag = "genhub-build";

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

    /// <summary>
    /// Sentinel version when no version could be determined during inspection.
    /// </summary>
    public const string UnknownVersion = "Unknown";

    /// <summary>
    /// Full company name identifying Electronic Arts game binaries (never a GenHub fork).
    /// </summary>
    public const string EaCompanyFullName = "Electronic Arts";

    /// <summary>
    /// Short company name identifying Electronic Arts game binaries (never a GenHub fork).
    /// </summary>
    public const string EaCompanyShortName = "EA";

    /// <summary>
    /// Fallback company name for detected forks without an identified author.
    /// </summary>
    public const string FallbackForkCompanyName = "Community";

    /// <summary>
    /// Fallback company name for detected custom builds without an identified author.
    /// </summary>
    public const string FallbackCustomCompanyName = "Custom";

    /// <summary>
    /// Default file description for filename-only build detections.
    /// </summary>
    public const string DefaultFileDescription = "GenHub Application Build";

    /// <summary>
    /// Explicit channel alias mapping to the development channel.
    /// </summary>
    public const string ChannelDevAlias = "Development";

    /// <summary>
    /// Explicit channel alias mapping to the test channel.
    /// </summary>
    public const string ChannelTestAlias = "Beta";

    /// <summary>
    /// Assembly metadata key carrying the build channel.
    /// </summary>
    public const string MetadataKeyBuildChannel = "BuildChannel";

    /// <summary>
    /// Assembly metadata key carrying the pull request number.
    /// </summary>
    public const string MetadataKeyPullRequestNumber = "PullRequestNumber";

    /// <summary>
    /// Assembly metadata key carrying the git commit hash.
    /// </summary>
    public const string MetadataKeyGitHash = "GitHash";

    /// <summary>
    /// Assembly metadata key carrying the commit hash (alternate spelling).
    /// </summary>
    public const string MetadataKeyCommitHash = "CommitHash";

    /// <summary>
    /// Tag prefix for fork slug tags (e.g. "fork:user").
    /// </summary>
    public const string TagPrefixFork = "fork:";

    /// <summary>
    /// Tag prefix for pull request tags (e.g. "pr:123").
    /// </summary>
    public const string TagPrefixPr = "pr:";

    /// <summary>
    /// Channel tag for development builds.
    /// </summary>
    public const string TagChannelDev = "channel:dev";

    /// <summary>
    /// Channel tag for test builds.
    /// </summary>
    public const string TagChannelTest = "channel:test";

    /// <summary>
    /// Executable file extension inspected for build metadata.
    /// </summary>
    public const string ExecutableExtension = ".exe";

    /// <summary>
    /// Managed library extension inspected for build metadata.
    /// </summary>
    public const string LibraryExtension = ".dll";

    /// <summary>
    /// Archive extension inspected for embedded builds.
    /// </summary>
    public const string ArchiveExtension = ".zip";

    /// <summary>
    /// Package extension inspected for embedded builds.
    /// </summary>
    public const string PackageExtension = ".nupkg";

    /// <summary>
    /// Nuspec manifest extension identifying package metadata entries.
    /// </summary>
    public const string NuspecExtension = ".nuspec";

    /// <summary>
    /// Checks whether a build channel represents a prerelease (PR, dev, or test) build.
    /// </summary>
    /// <param name="channel">The build channel name.</param>
    /// <returns><c>true</c> for prerelease channels; otherwise, <c>false</c>.</returns>
    public static bool IsPrereleaseChannel(string? channel) =>
        channel is ChannelPr or ChannelDev or ChannelTest;
}
