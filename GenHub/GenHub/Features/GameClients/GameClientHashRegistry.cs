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
/// Manages a registry of known game client executable hashes, providing methods to query and add hash information for different game types.
/// </summary>
public class GameClientHashRegistry : IGameClientHashRegistry
{
    // Core Hash Constants - These are the foundational hashes for official EA/Steam releases
    private const string Generals108Hash = "1c96366ff6a99f40863f6bbcfa8bf7622e8df1f80a474201e0e95e37c6416255";
    private const string ZeroHour104Hash = "f37a4929f8d697104e99c2bcf46f8d833122c943afcd87fd077df641d344495b";
    private const string ZeroHour105Hash = "420fba1dbdc4c14e2418c2b0d3010b9fac6f314eafa1f3a101805b8d98883ea1";

    private readonly ConcurrentDictionary<string, GameClientInfo> _knownHashes;
    private readonly List<string> _possibleExecutableNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientHashRegistry"/> class.
    /// </summary>
    public GameClientHashRegistry()
    {
        _knownHashes = new ConcurrentDictionary<string, GameClientInfo>(StringComparer.OrdinalIgnoreCase);
        _possibleExecutableNames = new List<string>
        {
            GameClientConstants.GeneralsExecutable,
            GameClientConstants.ZeroHourExecutable,
            GameClientConstants.SuperHackersGeneralsExecutable,
            GameClientConstants.SuperHackersZeroHourExecutable,
            GameClientConstants.GeneralsOnline30HzExecutable,
            GameClientConstants.GeneralsOnline60HzExecutable,
        };

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

        return "Unknown";
    }

    /// <inheritdoc/>
    public (GameType GameType, string Version) GetGameInfoFromHash(string hash)
    {
        if (TryGetInfo(hash, out var info) && info.HasValue)
        {
            return (info.Value.GameType, info.Value.Version);
        }

        return (GameType.Unknown, "Unknown");
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
        _knownHashes.TryAdd(ZeroHour104Hash, new GameClientInfo(GameType.ZeroHour, "1.04", "EA/Steam", "Official Zero Hour 1.04 executable", true));
        _knownHashes.TryAdd(ZeroHour105Hash, new GameClientInfo(GameType.ZeroHour, "1.05", "EA/Steam", "Official Zero Hour 1.05 executable", true));
    }
}