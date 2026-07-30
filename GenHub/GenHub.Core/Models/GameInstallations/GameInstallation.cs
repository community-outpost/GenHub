using GenHub.Core.Constants;
using GenHub.Core.Helpers;
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
    /// <remarks>
    /// Each game's flag turns on when that game's retail archives are present in its
    /// directory, not when an executable with a known name is. The executable name was
    /// only ever a proxy for "the archives are here" — it rejects the canonical native
    /// deploy, whose binary is extensionless — while the archives are the direct signal
    /// and the thing a retail root actually has to supply. A combined directory carrying
    /// both games' archives may legitimately be passed as both paths and sets both flags.
    /// </remarks>
    public void SetPaths(string? generalsPath, string? zeroHourPath)
    {
        if (!string.IsNullOrEmpty(generalsPath))
        {
            HasGenerals = ClassifyArchivesSafely(generalsPath).HasGeneralsArchives;
            GeneralsPath = generalsPath;
        }

        if (!string.IsNullOrEmpty(zeroHourPath))
        {
            HasZeroHour = ClassifyArchivesSafely(zeroHourPath).HasZeroHourArchives;
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
    /// Initializes the installation by scanning for each game's retail archives.
    /// Standard subdirectories are checked first, then the installation root itself,
    /// which covers flat manual installs and combined directories holding both games.
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

            // 1. Check strict subdirectories first (standard structure)
            var generalsPath = Path.Combine(InstallationPath, GameClientConstants.GeneralsDirectoryName);
            if (RetailArchiveClassifier.ClassifyArchives(generalsPath).HasGeneralsArchives)
            {
                HasGenerals = true;
                GeneralsPath = generalsPath;
                foundGenerals = true;
                _logger?.LogDebug("Found Generals installation at {GeneralsPath}", GeneralsPath);
            }

            var zeroHourPath = Path.Combine(InstallationPath, GameClientConstants.ZeroHourDirectoryName);
            if (RetailArchiveClassifier.ClassifyArchives(zeroHourPath).HasZeroHourArchives)
            {
                HasZeroHour = true;
                ZeroHourPath = zeroHourPath;
                foundZeroHour = true;
                _logger?.LogDebug("Found Zero Hour installation at {ZeroHourPath}", ZeroHourPath);
            }

            // 2. If not found in subdirectories, check the root path itself. Archive
            // classification tells the games apart even in one flat directory, so a
            // combined root legitimately sets both flags to the same path — the earlier
            // executable-based scan had to guess here, because both games ship the same
            // executable name.
            var rootClassification = RetailArchiveClassifier.ClassifyArchives(InstallationPath);

            if (!foundGenerals && rootClassification.HasGeneralsArchives)
            {
                HasGenerals = true;
                GeneralsPath = InstallationPath;
                foundGenerals = true;
                _logger?.LogDebug("Found Generals installation at root {GeneralsPath}", GeneralsPath);
            }

            if (!foundZeroHour && rootClassification.HasZeroHourArchives)
            {
                HasZeroHour = true;
                ZeroHourPath = InstallationPath;
                foundZeroHour = true;
                _logger?.LogDebug("Found Zero Hour installation at root {ZeroHourPath}", ZeroHourPath);
            }

            // Log warnings only if absolutely nothing found
            if (!foundGenerals && !foundZeroHour)
            {
                _logger?.LogWarning("No retail game archives found in {InstallationPath} or standard subdirectories", InstallationPath);
            }

            _logger?.LogInformation(
                "Installation fetch completed for {InstallationPath}: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                InstallationPath,
                HasGenerals,
                HasZeroHour);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to fetch installations for {InstallationPath}", InstallationPath);
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

    /// <summary>
    /// Classifies a directory's archives without letting a filesystem error escape.
    /// </summary>
    /// <param name="path">The directory to classify.</param>
    /// <returns>The classification, or neither game when the directory cannot be read.</returns>
    /// <remarks>
    /// <see cref="SetPaths"/> is called from every platform detector, so it must not
    /// throw. An unreadable directory is logged rather than silently reading as "no
    /// archives" — the flag still ends up false, but the log names the real cause.
    /// </remarks>
    private RetailArchiveClassification ClassifyArchivesSafely(string path)
    {
        try
        {
            return RetailArchiveClassifier.ClassifyArchives(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger?.LogWarning(ex, "Could not read {Path} while classifying retail archives; treating it as holding none", path);
            return default;
        }
    }
}