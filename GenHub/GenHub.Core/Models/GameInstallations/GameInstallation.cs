using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Models.GameInstallations;

/// <summary>
/// Represents a detected or user-registered game installation (Steam, EA App, etc).
/// </summary>
public class GameInstallation : IGameInstallation
{
    private readonly ILogger<GameInstallation>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameInstallation"/> class.
    /// </summary>
    /// <param name="installationPath">The installation path.</param>
    /// <param name="installationType">The installation type.</param>
    /// <param name="logger">Optional logger instance.</param>
    public GameInstallation(
        string installationPath,
        GameInstallationType installationType,
        ILogger<GameInstallation>? logger = null)
    {
        InstallationPath = installationPath;
        InstallationType = installationType;
        DetectedAt = DateTime.UtcNow;
        AvailableClientsInternal = [];
        _logger = logger;

        _logger?.LogDebug(
            "Created GameInstallation: Path={InstallationPath}, Type={InstallationType}",
            InstallationPath,
            InstallationType);
    }

    /// <summary>
    /// Gets or sets the unique identifier for this installation.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the installation type.</summary>
    public GameInstallationType InstallationType { get; set; }

    /// <summary>Gets or sets the available game clients for this installation.</summary>
    public List<GameClient> AvailableGameClients { get; set; } = [];

    /// <summary>Gets the base installation directory path.</summary>
    public string InstallationPath { get; private set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the vanilla game is installed.</summary>
    public bool HasGenerals { get; set; }

    /// <summary>Gets or sets the path of the vanilla game installation.</summary>
    public string GeneralsPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether Zero Hour is installed.</summary>
    public bool HasZeroHour { get; set; }

    /// <summary>Gets or sets the path of the Zero Hour installation.</summary>
    public string ZeroHourPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date and time when this installation was detected/registered.
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets a value indicating whether this installation is currently valid/accessible.
    /// An installation is considered valid if:
    /// - If GeneralsPath is set, the directory must exist.
    /// - If ZeroHourPath is set, the directory must exist.
    /// Unset paths are allowed to support partial installations (e.g., only Generals or only Zero Hour).
    /// </summary>
    public bool IsValid =>
        (string.IsNullOrEmpty(GeneralsPath) || Directory.Exists(GeneralsPath)) &&
        (string.IsNullOrEmpty(ZeroHourPath) || Directory.Exists(ZeroHourPath));

    /// <summary>
    /// Gets the GameClient for the Generals game type if available in the <see cref="AvailableGameClients"/> collection.
    /// </summary>
    /// <value>
    /// The first <see cref="GameClient"/> where <see cref="GameClient.GameType"/> is <see cref="GameType.Generals"/>,
    /// or <c>null</c> if no matching client exists.
    /// </value>
    public GameClient? GeneralsClient => AvailableGameClients.FirstOrDefault(c => c.GameType == GameType.Generals);

    /// <summary>
    /// Gets the GameClient for the Zero Hour game type if available in the <see cref="AvailableGameClients"/> collection.
    /// </summary>
    /// <value>
    /// The first <see cref="GameClient"/> where <see cref="GameClient.GameType"/> is <see cref="GameType.ZeroHour"/>,
    /// or <c>null</c> if no matching client exists.
    /// </value>
    public GameClient? ZeroHourClient => AvailableGameClients.FirstOrDefault(c => c.GameType == GameType.ZeroHour);

    /// <summary>Gets the internal list of available game clients for population.</summary>
    internal List<GameClient> AvailableClientsInternal { get; }

    /// <summary>
    /// Sets the paths for Generals and Zero Hour.
    /// </summary>
    /// <param name="generalsPath">The path to Generals, or null if not present.</param>
    /// <param name="zeroHourPath">The path to Zero Hour, or null if not present.</param>
    public void SetPaths(string? generalsPath, string? zeroHourPath)
    {
        if (!string.IsNullOrEmpty(generalsPath))
        {
            HasGenerals = Directory.Exists(generalsPath) && HasValidExecutable(generalsPath);
            GeneralsPath = generalsPath;
        }

        if (!string.IsNullOrEmpty(zeroHourPath))
        {
            HasZeroHour = Directory.Exists(zeroHourPath) && HasValidExecutable(zeroHourPath);
            ZeroHourPath = zeroHourPath;
        }

        _logger?.LogDebug("Set paths for {InstallationType}: Generals={HasGenerals}, ZeroHour={HasZeroHour}", InstallationType, HasGenerals, HasZeroHour);
    }

    /// <summary>
    /// Populates the available game clients for this installation.
    /// </summary>
    /// <param name="clients">The clients to add.</param>
    public void PopulateGameClients(IEnumerable<GameClient> clients)
    {
        AvailableClientsInternal.Clear();
        AvailableClientsInternal.AddRange(clients.Where(c => c.InstallationId == Id));

        // Sync to public property
        AvailableGameClients.Clear();
        AvailableGameClients.AddRange(AvailableClientsInternal);

        _logger?.LogInformation("Populated {Count} clients for {Id}", AvailableClientsInternal.Count, Id);
    }

    /// <summary>
    /// Initializes the installation by scanning for game directories and executables.
    /// This method performs automatic detection of Generals and Zero Hour installations
    /// within the installation path using standard directory naming conventions.
    /// </summary>
    /// <remarks>
    /// This method is primarily used for testing and initialization purposes.
    /// For production code, prefer using <see cref="SetPaths(string?, string?)"/> with explicit paths.
    /// </remarks>
    public void Fetch()
    {
        try
        {
            _logger?.LogDebug("Initializing installation scan - Current state: HasGenerals={HasGenerals}, HasZeroHour={HasZeroHour}", HasGenerals, HasZeroHour);
            _logger?.LogDebug("Fetching game installations for {InstallationPath}", InstallationPath);

            bool foundGenerals = false;
            bool foundZeroHour = false;

            // Preserve explicitly configured and valid paths (e.g. from platform detectors or manifests)
            if (!string.IsNullOrEmpty(GeneralsPath) && Directory.Exists(GeneralsPath) && HasValidExecutable(GeneralsPath))
            {
                HasGenerals = true;
                foundGenerals = true;
            }

            if (!string.IsNullOrEmpty(ZeroHourPath) && Directory.Exists(ZeroHourPath) && HasValidExecutable(ZeroHourPath))
            {
                HasZeroHour = true;
                foundZeroHour = true;
            }

            FetchSubdirectoryInstallations(ref foundGenerals, ref foundZeroHour);
            FetchRootInstallation(ref foundGenerals, ref foundZeroHour);

            // Log warnings only if absolutely nothing found
            if (!foundGenerals && !foundZeroHour)
            {
                _logger?.LogWarning("No game executables found in {InstallationPath} or standard subdirectories", InstallationPath);
            }

            _logger?.LogInformation(
                "Installation fetch completed for {InstallationPath}: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                InstallationPath,
                HasGenerals,
                HasZeroHour);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to fetch installation at {InstallationPath}", InstallationPath);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{InstallationType}: {InstallationPath}";

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is GameInstallation other)
        {
            return string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return Id?.GetHashCode() ?? 0;
    }

    private static bool HasValidExecutable(string path)
    {
        var possibleExes = new[] { GameClientConstants.SteamGameDatExecutable, GameClientConstants.GeneralsExecutable, GameClientConstants.ZeroHourExecutable };
        return possibleExes.Any(exe => Path.Combine(path, exe).FileExistsCaseInsensitive());
    }

    private static bool HasRootExecutable(string path)
    {
        var possibleExes = new[]
        {
            GameClientConstants.GeneralsExecutable,
            GameClientConstants.SuperHackersZeroHourExecutable,
            GameClientConstants.SuperHackersGeneralsExecutable,
            GameClientConstants.GeneralsOnlineDefaultExecutable,
            GameClientConstants.GeneralsOnline60HzExecutable,
            GameClientConstants.GeneralsOnlineEacLauncherExecutable,
            GameClientConstants.ContraExecutable,
            GameClientConstants.SteamGameDatExecutable,
            GameClientConstants.GameExecutable,
        };

        return possibleExes.Any(exe => Path.Combine(path, exe).FileExistsCaseInsensitive());
    }

    private static bool HasZeroHourArchiveOrExecutableSignature(string path)
    {
        if (Path.Combine(path, GameClientConstants.ZeroHourIniBig).FileExistsCaseInsensitive() ||
            Path.Combine(path, GameClientConstants.ZeroHourPatchBig).FileExistsCaseInsensitive())
        {
            return true;
        }

        if (Path.Combine(path, GameClientConstants.SuperHackersZeroHourExecutable).FileExistsCaseInsensitive() ||
            Path.Combine(path, GameClientConstants.GeneralsOnlineDefaultExecutable).FileExistsCaseInsensitive() ||
            Path.Combine(path, GameClientConstants.GeneralsOnline60HzExecutable).FileExistsCaseInsensitive() ||
            Path.Combine(path, GameClientConstants.GeneralsOnlineEacLauncherExecutable).FileExistsCaseInsensitive() ||
            Path.Combine(path, GameClientConstants.ContraExecutable).FileExistsCaseInsensitive())
        {
            return true;
        }

        // Check for any localized or mod big archive ending with ZH.big (e.g. SpeechEnglishZH.big, RussianZH.big, GermanZH.big, MapsZH.big)
        try
        {
            if (Directory.Exists(path))
            {
                var directoryInfo = new DirectoryInfo(path);
                if (directoryInfo.EnumerateFiles().Any(f => f.Name.EndsWith("ZH.big", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            // Directory probe failure fallback
        }
        catch (UnauthorizedAccessException)
        {
            // Directory access denied fallback
        }

        return false;
    }

    private static bool HasGeneralsArchiveSignature(string path)
    {
        return Path.Combine(path, "gensec.big").FileExistsCaseInsensitive() ||
               Path.Combine(path, GameClientConstants.GeneralsIniBig).FileExistsCaseInsensitive() ||
               Path.Combine(path, GameClientConstants.GeneralsPatchBig).FileExistsCaseInsensitive() ||
               Path.Combine(path, GameClientConstants.SuperHackersGeneralsExecutable).FileExistsCaseInsensitive();
    }

    private static bool IsZeroHourNamedDirectory(string path)
    {
        var folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return folderName.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase) ||
               folderName.Contains("ZeroHour", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(folderName, "ZH", StringComparison.OrdinalIgnoreCase) ||
               folderName.StartsWith("ZH_", StringComparison.OrdinalIgnoreCase) ||
               folderName.EndsWith("_ZH", StringComparison.OrdinalIgnoreCase) ||
               folderName.StartsWith("ZH-", StringComparison.OrdinalIgnoreCase) ||
               folderName.EndsWith("-ZH", StringComparison.OrdinalIgnoreCase);
    }

    private void FetchSubdirectoryInstallations(ref bool foundGenerals, ref bool foundZeroHour)
    {
        if (!foundGenerals)
        {
            ReadOnlySpan<string> generalsSubdirs =
            [
                GameClientConstants.GeneralsDirectoryName,
                GameClientConstants.GeneralsRetailDirectoryName,
            ];

            if (TryFindSubdirectoryInstallation(generalsSubdirs, GameClientConstants.GeneralsExecutable, out var generalsPath))
            {
                HasGenerals = true;
                GeneralsPath = generalsPath;
                foundGenerals = true;
                _logger?.LogDebug("Found Generals installation at {GeneralsPath}", GeneralsPath);
            }
        }

        if (!foundZeroHour)
        {
            ReadOnlySpan<string> zhSubdirs =
            [
                GameClientConstants.ZeroHourDirectoryName,
                GameClientConstants.ZeroHourDirectoryNameAmpersandHyphen,
                GameClientConstants.ZeroHourRetailDirectoryName,
                GameClientConstants.ZeroHourDirectoryNameAbbreviated,
                GameClientConstants.ZeroHourDirectoryNameColonVariant,
            ];

            if (TryFindSubdirectoryInstallation(zhSubdirs, GameClientConstants.ZeroHourExecutable, out var zeroHourPath))
            {
                HasZeroHour = true;
                ZeroHourPath = zeroHourPath;
                foundZeroHour = true;
                _logger?.LogDebug("Found Zero Hour installation at {ZeroHourPath}", ZeroHourPath);
            }
        }
    }

    private bool TryFindSubdirectoryInstallation(
        ReadOnlySpan<string> candidateSubdirectories,
        string executableName,
        [NotNullWhen(true)] out string? foundPath)
    {
        foreach (var subDir in candidateSubdirectories)
        {
            if (InstallationPath.TryGetDirectoryCaseInsensitive(subDir, out var subDirPath))
            {
                var exePath = Path.Combine(subDirPath, executableName);
                if (exePath.FileExistsCaseInsensitive())
                {
                    foundPath = subDirPath;
                    return true;
                }
            }
        }

        foundPath = null;
        return false;
    }

    private void FetchRootInstallation(ref bool foundGenerals, ref bool foundZeroHour)
    {
        if ((foundGenerals && foundZeroHour) || !HasRootExecutable(InstallationPath))
        {
            return;
        }

        var isZhNamed = IsZeroHourNamedDirectory(InstallationPath);
        var hasZhSignature = HasZeroHourArchiveOrExecutableSignature(InstallationPath);
        var hasGenSignature = HasGeneralsArchiveSignature(InstallationPath);

        if (!foundZeroHour && (isZhNamed || hasZhSignature))
        {
            HasZeroHour = true;
            ZeroHourPath = InstallationPath;
            foundZeroHour = true;
            _logger?.LogDebug("Found Zero Hour installation at root {ZeroHourPath}", ZeroHourPath);
        }

        if (!foundGenerals && hasGenSignature)
        {
            var isStrictGeneralsOnlySignature =
                Path.Combine(InstallationPath, "gensec.big").FileExistsCaseInsensitive() ||
                Path.Combine(InstallationPath, GameClientConstants.SuperHackersGeneralsExecutable).FileExistsCaseInsensitive();

            var isZeroHour = isZhNamed || hasZhSignature;
            if (!isZeroHour || isStrictGeneralsOnlySignature)
            {
                HasGenerals = true;
                GeneralsPath = InstallationPath;
                foundGenerals = true;
                _logger?.LogDebug("Found Generals installation at root {GeneralsPath}", GeneralsPath);
            }
        }

        if (foundGenerals || foundZeroHour)
        {
            return;
        }

        AssignRootFallback(ref foundGenerals, ref foundZeroHour);
    }

    private void AssignRootFallback(ref bool foundGenerals, ref bool foundZeroHour)
    {
        if (IsZeroHourNamedDirectory(InstallationPath))
        {
            HasZeroHour = true;
            ZeroHourPath = InstallationPath;
            foundZeroHour = true;
            _logger?.LogDebug("Found Zero Hour installation at root based on directory name {ZeroHourPath}", ZeroHourPath);
        }
        else
        {
            HasGenerals = true;
            GeneralsPath = InstallationPath;
            foundGenerals = true;
            _logger?.LogDebug("Found Generals installation at root {GeneralsPath}", GeneralsPath);
        }
    }
}
