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

            // 1. Check strict subdirectories first (standard structure)
            var generalsPath = Path.Combine(InstallationPath, "Command and Conquer Generals");
            if (Directory.Exists(generalsPath))
            {
                var generalsExe = Path.Combine(generalsPath, GameClientConstants.GeneralsExecutable);
                if (generalsExe.FileExistsCaseInsensitive())
                {
                    HasGenerals = true;
                    GeneralsPath = generalsPath;
                    foundGenerals = true;
                    _logger?.LogDebug("Found Generals installation at {GeneralsPath}", GeneralsPath);
                }
            }

            var zeroHourPath = Path.Combine(InstallationPath, GameClientConstants.ZeroHourDirectoryName);
            if (Directory.Exists(zeroHourPath))
            {
                var zeroHourExe = Path.Combine(zeroHourPath, GameClientConstants.ZeroHourExecutable);
                if (zeroHourExe.FileExistsCaseInsensitive())
                {
                    HasZeroHour = true;
                    ZeroHourPath = zeroHourPath;
                    foundZeroHour = true;
                    _logger?.LogDebug("Found Zero Hour installation at {ZeroHourPath}", ZeroHourPath);
                }
            }

            // 2. If not found in subdirectories, check the root path (common for manual installs/repacks)
            if (!foundGenerals)
            {
                var rootGeneralsExe = Path.Combine(InstallationPath, GameClientConstants.GeneralsExecutable);

                // Note: Zero Hour also has a generals.exe, so we need to be careful.
                // If checking for valid installation, presence of generals.exe usually implies Generals capability.
                if (rootGeneralsExe.FileExistsCaseInsensitive())
                {
                    HasGenerals = true;
                    GeneralsPath = InstallationPath;
                    foundGenerals = true;
                    _logger?.LogDebug("Found Generals installation at root {GeneralsPath}", GeneralsPath);
                }
            }

            if (!foundZeroHour)
            {
                // Zero Hour usually has generals.exe AND specific files like "generals.zh.exe" (sometimes) or just "generals.exe" with different hash/version.
                // Detection primarily relies on folder name or presence of expansion files.
                // Checking for generals.exe in root can map to both if the user selected a merged directory.
                var rootGeneralsExe = Path.Combine(InstallationPath, GameClientConstants.GeneralsExecutable);

                if (rootGeneralsExe.FileExistsCaseInsensitive())
                {
                    // If we are in root and found generals.exe, it could be ZH.
                    // Check for something specific to ZH if possible, or just assume if user pointed here it might be combined.
                    // For safety, let's treat root install as potentially containing both if we can't distinguish.

                    // Ideally we check for a ZH specific file, but standard detection often just looks for exe.
                    // Let's assume if the user pointed us here and it has the exe, it's valid.
                    // Standard Retail ZH has "generals.exe" but also usually lives in its own folder.
                    // If user pointed to "C:\Games\ZH", it has generals.exe.
                    HasZeroHour = true;
                    ZeroHourPath = InstallationPath;
                    foundZeroHour = true;
                    _logger?.LogDebug("Found Zero Hour installation at root {ZeroHourPath}", ZeroHourPath);
                }
            }

            // Logic improvement: If we found generals.exe in root, we might have set BOTH to true/root.
            // This is acceptable for some "All in One" repacks or if the user manually merged them.

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

    private static bool HasValidExecutable(string path)
    {
        var possibleExes = new[] { GameClientConstants.SteamGameDatExecutable, GameClientConstants.GeneralsExecutable, GameClientConstants.ZeroHourExecutable };
        return possibleExes.Any(exe => Path.Combine(path, exe).FileExistsCaseInsensitive());
    }
}