using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.MacOS.GameInstallations;

/// <summary>
/// Detects retail Generals and Zero Hour data on macOS.
/// <para>
/// There is no native macOS distribution of either game, so there is nothing to
/// detect in the sense Windows and Linux mean it: no Steam library, no EA App, no
/// registry. What a macOS user has is a copied retail directory tree, either placed
/// somewhere obvious by hand or sitting inside a Wine or CrossOver bottle. This
/// detector looks in those places and quietly finds nothing when they are absent.
/// </para>
/// <para>
/// Finding nothing is the expected outcome, not a failure. It returns an empty
/// success so the orchestrator reports "no installations" rather than an error, and
/// the user is directed to the manual browse flow. The detector exists so that macOS
/// <em>has</em> a detector at all: the composition-root test asserts every host
/// registers one, which is what would otherwise let a missing registration ship as a
/// silently empty installation list.
/// </para>
/// </summary>
/// <param name="logger">Logger for detection progress.</param>
public class MacOSInstallationDetector(ILogger<MacOSInstallationDetector> logger) : IGameInstallationDetector
{
    /// <summary>
    /// Directory names a retail Zero Hour tree is known to use, across disc, EA, and
    /// Steam layouts.
    /// </summary>
    private static readonly string[] ZeroHourDirectoryNames =
    [
        GameClientConstants.ZeroHourDirectoryName,
        GameClientConstants.ZeroHourDirectoryNameAmpersandHyphen,
        GameClientConstants.ZeroHourDirectoryNameColonVariant,
        GameClientConstants.ZeroHourDirectoryNameAbbreviated,
        GameClientConstants.ZeroHourRetailDirectoryName,
    ];

    /// <summary>
    /// Directory names a retail Generals tree is known to use.
    /// </summary>
    private static readonly string[] GeneralsDirectoryNames =
    [
        GameClientConstants.GeneralsDirectoryName,
        GameClientConstants.GeneralsRetailDirectoryName,
    ];

    /// <inheritdoc/>
    public string DetectorName => "macOS Installation Detector";

    /// <inheritdoc/>
    public bool CanDetectOnCurrentPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <inheritdoc/>
    public Task<DetectionResult<GameInstallation>> DetectInstallationsAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var installs = new List<GameInstallation>();
        var deniedRoots = new List<string>();

        logger.LogInformation("Starting macOS game installation detection");

        try
        {
            foreach (var root in GetSearchRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (generalsPath, zeroHourPath, accessDenied) = FindGameDirectories(root);

                if (accessDenied)
                {
                    deniedRoots.Add(root);
                }

                if (generalsPath is null && zeroHourPath is null)
                {
                    continue;
                }

                var installation = new GameInstallation(root, GameInstallationType.Retail, null);
                installation.SetPaths(generalsPath, zeroHourPath);

                // SetPaths only sets Has* when a valid executable is present, so a
                // directory that merely has the right name is discarded here.
                if (!installation.HasGenerals && !installation.HasZeroHour)
                {
                    logger.LogDebug("Directory under {Root} matched by name but has no game executable", root);
                    continue;
                }

                installs.Add(installation);
                logger.LogInformation(
                    "Detected retail installation under {Root}: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                    root,
                    installation.HasGenerals,
                    installation.HasZeroHour);
            }

            if (deniedRoots.Count > 0)
            {
                logger.LogWarning(
                    "Could not read {DeniedCount} location(s) during detection: {DeniedRoots}. " +
                    "macOS blocks these until access is granted in " +
                    "System Settings > Privacy & Security > Files and Folders.",
                    deniedRoots.Count,
                    string.Join(", ", deniedRoots));
            }

            logger.LogInformation(
                "macOS installation detection completed with {ResultCount} installations found",
                installs.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "macOS installation detection failed");
            sw.Stop();
            return Task.FromResult(DetectionResult<GameInstallation>.CreateFailure(ex.Message));
        }

        sw.Stop();
        return Task.FromResult(CreateDetectionResult(installs, deniedRoots, sw.Elapsed));
    }

    /// <summary>
    /// Creates the final detection result while preserving retry semantics for incomplete scans.
    /// </summary>
    /// <param name="installs">The installations found in readable locations.</param>
    /// <param name="deniedRoots">Locations that could not be searched.</param>
    /// <param name="elapsed">The elapsed detection time.</param>
    /// <returns>A successful result only when every candidate location was searchable.</returns>
    internal static DetectionResult<GameInstallation> CreateDetectionResult(
        IReadOnlyCollection<GameInstallation> installs,
        IReadOnlyCollection<string> deniedRoots,
        TimeSpan elapsed)
    {
        // Any denied root makes the result incomplete. Returning success when another
        // root happened to contain a game would cache that partial result and suppress
        // the retry needed after the user grants access.
        if (deniedRoots.Count > 0)
        {
            return DetectionResult<GameInstallation>.CreateFailure(
                $"Could not search {string.Join(", ", deniedRoots)} because macOS denied access, " +
                "so installation detection is incomplete. Grant access in " +
                "System Settings > Privacy & Security > Files and Folders, then detect again.");
        }

        return DetectionResult<GameInstallation>.CreateSuccess(installs, elapsed);
    }

    /// <summary>
    /// Builds the list of directories worth scanning for a copied retail tree.
    /// </summary>
    /// <returns>Candidate root directories, in rough order of likelihood.</returns>
    private static IEnumerable<string> GetSearchRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            yield break;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents))
        {
            yield return documents;
        }

        // .NET has no SpecialFolder value for Downloads.
        yield return Path.Combine(home, "Downloads");

        var applicationSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(applicationSupport))
        {
            yield return applicationSupport;
        }

        yield return "/Applications";

        // Wine and CrossOver bottles keep a Windows-shaped tree under drive_c.
        foreach (var prefix in GetBottleDriveCPaths(home, applicationSupport))
        {
            yield return prefix;
            yield return Path.Combine(prefix, "Program Files", GameClientConstants.EaGamesParentDirectoryName);
            yield return Path.Combine(prefix, "Program Files (x86)", GameClientConstants.EaGamesParentDirectoryName);
        }
    }

    /// <summary>
    /// Enumerates the <c>drive_c</c> directory of every Wine prefix and CrossOver bottle.
    /// </summary>
    /// <param name="home">The current user's home directory.</param>
    /// <param name="applicationSupport">The platform-resolved application support directory.</param>
    /// <returns>Existing <c>drive_c</c> paths.</returns>
    private static IEnumerable<string> GetBottleDriveCPaths(string home, string applicationSupport)
    {
        var bottleContainers = new List<string>();
        if (!string.IsNullOrEmpty(applicationSupport))
        {
            bottleContainers.Add(Path.Combine(applicationSupport, "CrossOver", "Bottles"));
        }

        bottleContainers.Add(Path.Combine(home, "Wine Prefixes"));

        foreach (var container in bottleContainers)
        {
            string[] bottles = [];
            try
            {
                bottles = Directory.Exists(container) ? Directory.GetDirectories(container) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable bottle container is not a detection failure.
                continue;
            }

            foreach (var bottle in bottles)
            {
                var driveC = Path.Combine(bottle, "drive_c");
                if (Directory.Exists(driveC))
                {
                    yield return driveC;
                }
            }
        }

        // The default Wine prefix is a directory, not a container of directories.
        var defaultPrefix = Path.Combine(home, ".wine", "drive_c");
        if (Directory.Exists(defaultPrefix))
        {
            yield return defaultPrefix;
        }
    }

    /// <summary>
    /// Finds immediate children of <paramref name="root"/> whose names match the known
    /// Generals and Zero Hour directory names.
    /// </summary>
    /// <param name="root">Directory to search within.</param>
    /// <returns>
    /// The matching paths and whether access was denied. Matching is case-insensitive
    /// because macOS volumes can be case-sensitive while retail trees are Windows-cased.
    /// </returns>
    private static (string? GeneralsPath, string? ZeroHourPath, bool AccessDenied)
        FindGameDirectories(string root)
    {
        try
        {
            string? generalsPath = null;
            string? zeroHourPath = null;

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var directoryName = Path.GetFileName(directory);
                if (generalsPath is null &&
                    GeneralsDirectoryNames.Contains(directoryName, StringComparer.OrdinalIgnoreCase))
                {
                    generalsPath = directory;
                }

                if (zeroHourPath is null &&
                    ZeroHourDirectoryNames.Contains(directoryName, StringComparer.OrdinalIgnoreCase))
                {
                    zeroHourPath = directory;
                }

                if (generalsPath is not null && zeroHourPath is not null)
                {
                    break;
                }
            }

            return (generalsPath, zeroHourPath, false);
        }
        catch (UnauthorizedAccessException)
        {
            // Reported separately: on macOS this is how a declined TCC prompt surfaces for
            // a protected location such as ~/Documents. Treating it as "nothing here"
            // would tell the user they own no games when we were simply not allowed to look.
            return (null, null, true);
        }
        catch (Exception)
        {
            // A vanished directory is not a detection failure.
            return (null, null, false);
        }
    }
}
