using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;

namespace GenHub.Features.GameClients;

/// <summary>
/// Injectable implementation of game client hash registry.
/// Replaces the static GameClientHashes class for better testability and dependency injection.
/// </summary>
public class GameClientHashRegistry : IGameClientHashRegistry
{
    // Core Hash Constants - These are the foundational hashes for official EA/Steam releases
    private const string Generals108Hash = "1c96366ff6a99f40863f6bbcfa8bf7622e8df1f80a474201e0e95e37c6416255";
    private const string SteamZeroHour104Hash = "7B075B9F0BAA9DF81651C0C9DD7D8C445454AE1B2452B928F4A1D9332E9CCECE";
    private const string EaAppZeroHour104Hash = "253FEBA0A5503CB4D49FD07463B17D3CC84731E583F9625CB90FCD8B5CAC0221";
    private const string EaAppGenerals108Hash = "69A39881179112A566CEF69573B20065CC868516C49AF0761F809EC57DA0BDBC";

    private const string ZeroHour104Hash = "f37a4929f8d697104e99c2bcf46f8d833122c943afcd87fd077df641d344495b";
    private const string ZeroHour105Hash = "420fba1dbdc4c14e2418c2b0d3010b9fac6f314eafa1f3a101805b8d98883ea1";

    // Launcher Stub Hashes (Steam/EA App)
    private const string ModernLauncherStubHash = "FF6F78211A014100D8EF6B08BC2F8EDD3D55E99E872DFDB5371776FC5A5D02CE";

    // Public static access to hashes for testing

    /// <summary>Gets the hash for Generals 1.08.</summary>
    public static string Generals108HashPublic => Generals108Hash;

    /// <summary>Gets the hash for Zero Hour 1.04.</summary>
    public static string ZeroHour104HashPublic => ZeroHour104Hash;

    /// <summary>Gets the hash for Zero Hour 1.05.</summary>
    public static string ZeroHour105HashPublic => ZeroHour105Hash;

    private readonly ConcurrentDictionary<string, GameClientInfo> _knownHashes;
    private readonly List<string> _possibleExecutableNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientHashRegistry"/> class.
    /// </summary>
    public GameClientHashRegistry()
    {
        _knownHashes = new ConcurrentDictionary<string, GameClientInfo>(StringComparer.OrdinalIgnoreCase);
        _possibleExecutableNames =
        [

            // Engine files (Prefer these as they are more reliable for version detection)
            GameClientConstants.GameExecutable,        // game.exe
            GameClientConstants.SteamGameDatExecutable, // game.dat

            // Launcher stubs
            GameClientConstants.GeneralsExecutable,
            GameClientConstants.ZeroHourExecutable,

            // Publisher clients
            GameClientConstants.SuperHackersGeneralsExecutable,
            GameClientConstants.SuperHackersZeroHourExecutable,
            GameClientConstants.GeneralsOnline30HzExecutable,
            GameClientConstants.GeneralsOnline60HzExecutable,
        ];

        InitializeCoreHashes();
    }

    /// <inheritdoc/>
    public bool TryGetInfo(string hash, out GameClientInfo? info)
    {
        if (string.IsNullOrEmpty(hash))
        {
            info = null;
            return false;
        }

        if (_knownHashes.TryGetValue(hash, out GameClientInfo tempInfo))
        {
            info = tempInfo;
            return true;
        }

        info = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryAddHash(string hash, GameClientInfo info)
    {
        if (string.IsNullOrEmpty(hash))
            return false;

        return _knownHashes.TryAdd(hash, info);
    }

    /// <inheritdoc/>
    public string GetVersionFromHash(string hash, GameType gameType)
    {
        if (TryGetInfo(hash, out var info) && info?.GameType == gameType)
        {
            return info.Value.Version;
        }

        return GameClientConstants.UnknownVersion;
    }

    /// <inheritdoc/>
    public (GameType GameType, string Version) GetGameInfoFromHash(string hash)
    {
        if (TryGetInfo(hash, out var info) && info.HasValue)
        {
            return (info.Value.GameType, info.Value.Version);
        }

        return (GameType.Unknown, GameClientConstants.UnknownVersion);
    }

    /// <inheritdoc/>
    public bool IsKnownHash(string hash)
    {
        return TryGetInfo(hash, out _);
    }

    /// <inheritdoc/>
    public Dictionary<string, string> GetHashesForGameType(GameType gameType)
    {
        return _knownHashes
            .Where(kvp => kvp.Value.GameType == gameType)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Version, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public Dictionary<string, GameClientInfo> GetExecutableInfoForGameType(GameType gameType)
    {
        return _knownHashes
            .Where(kvp => kvp.Value.GameType == gameType)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> PossibleExecutableNames => _possibleExecutableNames.AsReadOnly();

    /// <inheritdoc/>
    public bool AddPossibleExecutableName(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
            return false;

        lock (_possibleExecutableNames)
        {
            if (_possibleExecutableNames.Contains(executableName, StringComparer.OrdinalIgnoreCase))
                return false;

            _possibleExecutableNames.Add(executableName);
            return true;
        }
    }

    /// <summary>
    /// Initializes the core hash database with known official game executable hashes.
    /// </summary>
    private void InitializeCoreHashes()
    {
        _knownHashes.TryAdd(Generals108Hash, new GameClientInfo(GameType.Generals, "1.08", "EA/Steam", "Official Generals 1.08 executable", true));
        _knownHashes.TryAdd(EaAppGenerals108Hash, new GameClientInfo(GameType.Generals, "1.08", "EA App", "Official EA App Generals 1.08 executable", true));
        _knownHashes.TryAdd(SteamZeroHour104Hash, new GameClientInfo(GameType.ZeroHour, "1.04", "Steam", "Official Steam Zero Hour 1.04 executable", true));
        _knownHashes.TryAdd(EaAppZeroHour104Hash, new GameClientInfo(GameType.ZeroHour, "1.04", "EA App", "Official EA App Zero Hour 1.04 executable", true));
        _knownHashes.TryAdd(ZeroHour104Hash, new GameClientInfo(GameType.ZeroHour, "1.04", "EA/Steam", "Official Zero Hour 1.04 executable", true));
        _knownHashes.TryAdd(ZeroHour105Hash, new GameClientInfo(GameType.ZeroHour, "1.05", "EA/Steam", "Official Zero Hour 1.05 executable", true));

        // Registry for common launcher stubs (Informational, prioritized lower by PossibleExecutableNames)
        _knownHashes.TryAdd(ModernLauncherStubHash, new GameClientInfo(GameType.ZeroHour, "1.04", "Launcher", "Modern Steam/EA launcher stub", false));
    }
}