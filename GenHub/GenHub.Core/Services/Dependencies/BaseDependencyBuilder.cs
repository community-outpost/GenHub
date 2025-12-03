using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Services.Dependencies;

/// <summary>
/// Base class for building content dependencies across all publishers.
/// Provides common factory methods for standard dependencies like game installations
/// and a consistent interface for publisher-specific dependency builders.
/// </summary>
/// <remarks>
/// This class serves as the foundation for the dependency system described in PR-Dependency.md.
/// Each publisher (GeneralsOnline, TheSuperhackers, CommunityOutpost) can inherit from this
/// to define their specific dependencies while reusing common dependency patterns.
/// </remarks>
public abstract class BaseDependencyBuilder
{
    /// <summary>
    /// Schema version used in all dependency IDs.
    /// </summary>
    protected const int SchemaVersion = 1;

    /// <summary>
    /// Publisher wildcard for dependencies that can be satisfied by any publisher's installation.
    /// Game installations from Steam, EA, Origin, etc. all satisfy the same dependency.
    /// </summary>
    protected const string AnyPublisher = "any";

    /// <summary>
    /// Creates a dependency on Zero Hour 1.04 game installation.
    /// This is the most common dependency - most content requires the patched Zero Hour game.
    /// </summary>
    /// <param name="strictPublisher">If true, requires a specific publisher (Steam/EA). If false, any ZH installation works.</param>
    /// <param name="requiredPublisherTypes">Optional list of required publisher types (e.g., "steam", "ea").</param>
    /// <returns>A content dependency for Zero Hour 1.04 installation.</returns>
    public static ContentDependency CreateZeroHour104Dependency(
        bool strictPublisher = false,
        List<string>? requiredPublisherTypes = null)
    {
        return new ContentDependency
        {
            // Use semantic ID - matching is done via DependencyType and CompatibleGameTypes
            // Use 'any' publisher since any platform's ZH installation satisfies this
            Id = ManifestId.Create($"{SchemaVersion}.104.{AnyPublisher}.gameinstallation.zerohour"),
            Name = GameClientConstants.ZeroHourInstallationDependencyName,
            DependencyType = ContentType.GameInstallation,
            MinVersion = ManifestConstants.ZeroHourManifestVersion, // "1.04"
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = strictPublisher,
            RequiredPublisherTypes = requiredPublisherTypes ?? new List<string>(),
            CompatibleGameTypes = new List<GameType> { GameType.ZeroHour },
        };
    }

    /// <summary>
    /// Creates a dependency on Generals 1.08 game installation.
    /// Used by content that targets the original Generals game.
    /// </summary>
    /// <param name="strictPublisher">If true, requires a specific publisher (Steam/EA). If false, any Generals installation works.</param>
    /// <param name="requiredPublisherTypes">Optional list of required publisher types.</param>
    /// <returns>A content dependency for Generals 1.08 installation.</returns>
    public static ContentDependency CreateGenerals108Dependency(
        bool strictPublisher = false,
        List<string>? requiredPublisherTypes = null)
    {
        return new ContentDependency
        {
            // Use 'any' publisher since any platform's Generals installation satisfies this
            Id = ManifestId.Create($"{SchemaVersion}.108.{AnyPublisher}.gameinstallation.generals"),
            Name = "Generals 1.08 (Required)",
            DependencyType = ContentType.GameInstallation,
            MinVersion = ManifestConstants.GeneralsManifestVersion, // "1.08"
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = strictPublisher,
            RequiredPublisherTypes = requiredPublisherTypes ?? new List<string>(),
            CompatibleGameTypes = new List<GameType> { GameType.Generals },
        };
    }

    /// <summary>
    /// Creates a dependency on any Zero Hour game installation (any version).
    /// Used for content that works with unpatched Zero Hour.
    /// </summary>
    /// <returns>A content dependency for any Zero Hour installation.</returns>
    public static ContentDependency CreateZeroHourAnyVersionDependency()
    {
        return new ContentDependency
        {
            Id = ManifestId.Create($"{SchemaVersion}.0.{AnyPublisher}.gameinstallation.zerohour"),
            Name = "Zero Hour (Any Version)",
            DependencyType = ContentType.GameInstallation,
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = false,
            CompatibleGameTypes = new List<GameType> { GameType.ZeroHour },
        };
    }

    /// <summary>
    /// Creates a dependency on any Generals game installation (any version).
    /// </summary>
    /// <returns>A content dependency for any Generals installation.</returns>
    public static ContentDependency CreateGeneralsAnyVersionDependency()
    {
        return new ContentDependency
        {
            Id = ManifestId.Create($"{SchemaVersion}.0.{AnyPublisher}.gameinstallation.generals"),
            Name = "Generals (Any Version)",
            DependencyType = ContentType.GameInstallation,
            InstallBehavior = DependencyInstallBehavior.RequireExisting,
            IsOptional = false,
            StrictPublisher = false,
            CompatibleGameTypes = new List<GameType> { GameType.Generals },
        };
    }

    /// <summary>
    /// Creates a game installation dependency based on game type.
    /// Automatically selects the appropriate patched version dependency.
    /// </summary>
    /// <param name="gameType">The target game type.</param>
    /// <param name="requirePatchedVersion">Whether to require the patched version (1.04 for ZH, 1.08 for Generals).</param>
    /// <returns>The appropriate game installation dependency.</returns>
    public static ContentDependency CreateGameInstallationDependency(
        GameType gameType,
        bool requirePatchedVersion = true)
    {
        return gameType switch
        {
            GameType.ZeroHour when requirePatchedVersion => CreateZeroHour104Dependency(),
            GameType.ZeroHour => CreateZeroHourAnyVersionDependency(),
            GameType.Generals when requirePatchedVersion => CreateGenerals108Dependency(),
            GameType.Generals => CreateGeneralsAnyVersionDependency(),
            _ => throw new ArgumentException($"Unsupported game type: {gameType}", nameof(gameType)),
        };
    }

    /// <summary>
    /// Creates a custom content dependency with full control over all properties.
    /// </summary>
    /// <param name="publisherId">The publisher ID for the dependency.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="contentName">The content name (used in manifest ID).</param>
    /// <param name="displayName">The display name shown to users.</param>
    /// <param name="installBehavior">How to handle this dependency.</param>
    /// <param name="version">Optional version number (defaults to 0).</param>
    /// <param name="minVersion">Optional minimum version string.</param>
    /// <param name="maxVersion">Optional maximum version string.</param>
    /// <returns>A custom content dependency.</returns>
    public static ContentDependency CreateCustomDependency(
        string publisherId,
        ContentType contentType,
        string contentName,
        string displayName,
        DependencyInstallBehavior installBehavior,
        int version = 0,
        string? minVersion = null,
        string? maxVersion = null)
    {
        var contentTypeStr = contentType.ToManifestIdString();
        var isOptional = installBehavior == DependencyInstallBehavior.Optional ||
                         installBehavior == DependencyInstallBehavior.Suggest;

        return new ContentDependency
        {
            Id = ManifestId.Create($"{SchemaVersion}.{version}.{publisherId}.{contentTypeStr}.{contentName}"),
            Name = displayName,
            DependencyType = contentType,
            InstallBehavior = installBehavior,
            IsOptional = isOptional,
            MinVersion = minVersion,
            MaxVersion = maxVersion,
        };
    }

    /// <summary>
    /// Gets the dependencies for a content item from this publisher.
    /// Override this method in publisher-specific builders.
    /// </summary>
    /// <param name="manifest">The content manifest to get dependencies for.</param>
    /// <returns>List of dependencies for this content.</returns>
    public virtual List<ContentDependency> GetDependencies(ContentManifest manifest)
    {
        // Default implementation: Game clients require game installation
        if (manifest.ContentType == ContentType.GameClient)
        {
            return new List<ContentDependency>
            {
                CreateGameInstallationDependency(manifest.TargetGame, requirePatchedVersion: true),
            };
        }

        return new List<ContentDependency>();
    }

    /// <summary>
    /// Gets conflicting content codes for a given content code.
    /// Override in publisher-specific builders for exclusive categories.
    /// </summary>
    /// <param name="contentCode">The content code to check.</param>
    /// <returns>List of conflicting content codes.</returns>
    public virtual List<string> GetConflictingCodes(string contentCode)
    {
        return new List<string>();
    }

    /// <summary>
    /// Determines if content in a category is exclusive (only one can be active).
    /// Override in publisher-specific builders.
    /// </summary>
    /// <param name="category">The content category.</param>
    /// <returns>True if only one item of this category can be active.</returns>
    public virtual bool IsCategoryExclusive(string category)
    {
        return false;
    }

    /// <summary>
    /// Creates a list with a single dependency for convenience.
    /// </summary>
    /// <param name="dependency">The dependency to wrap in a list.</param>
    /// <returns>A list containing the single dependency.</returns>
    protected static List<ContentDependency> SingleDependency(ContentDependency dependency)
    {
        return new List<ContentDependency> { dependency };
    }

    /// <summary>
    /// Combines multiple dependency lists into one.
    /// </summary>
    /// <param name="dependencyLists">The dependency lists to combine.</param>
    /// <returns>A combined list of all dependencies.</returns>
    protected static List<ContentDependency> CombineDependencies(params List<ContentDependency>[] dependencyLists)
    {
        var result = new List<ContentDependency>();
        foreach (var list in dependencyLists)
        {
            result.AddRange(list);
        }

        return result;
    }
}
