using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameSettings;

/// <summary>
/// Service for managing the LAN player nickname (<c>UserName</c> in
/// <c>Network.ini</c>) for Generals and Zero Hour. The game loads this value
/// when its LAN lobby opens, so writing it before launch sets the in-game name.
/// </summary>
public class LanNicknameService(ILogger<LanNicknameService> logger, IGamePathProvider pathProvider) : ILanNicknameService
{
    /// <summary>
    /// Static semaphore serializing Network.ini reads and writes. Two Online
    /// launches racing on the same file must not interleave a read with the
    /// other's replacement.
    /// </summary>
    private static readonly SemaphoreSlim _networkIniLock = new(1, 1);

    private readonly ILogger<LanNicknameService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly IGamePathProvider _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));

    /// <inheritdoc/>
    public virtual string GetNetworkFilePath(GameType gameType)
    {
        var optionsDirectory = _pathProvider.GetOptionsDirectory(gameType);
        return Path.Combine(optionsDirectory, GameSettingsConstants.Network.FileName);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> LoadNicknameAsync(GameType gameType, CancellationToken cancellationToken = default)
    {
        await _networkIniLock.WaitAsync(cancellationToken);
        try
        {
            var filePath = GetNetworkFilePath(gameType);
            if (!File.Exists(filePath))
            {
                return OperationResult<string>.CreateSuccess(string.Empty);
            }

            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            foreach (var rawLine in lines)
            {
                if (!TrySplitKeyValue(rawLine, out var key, out var value))
                {
                    continue;
                }

                if (string.Equals(key, GameSettingsConstants.Network.UserNameKey, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<string>.CreateSuccess(LanNicknameCodec.Decode(value));
                }
            }

            return OperationResult<string>.CreateSuccess(string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to load LAN nickname for {GameType}", gameType);
            return OperationResult<string>.CreateFailure($"Failed to load LAN nickname: {ex.Message}");
        }
        finally
        {
            _networkIniLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SaveNicknameAsync(GameType gameType, string nickname, CancellationToken cancellationToken = default)
    {
        var normalized = LanNicknameCodec.Normalize(nickname);
        if (string.IsNullOrEmpty(normalized))
        {
            return OperationResult<bool>.CreateFailure(OnlineConstants.ErrorNicknameEmpty);
        }

        await _networkIniLock.WaitAsync(cancellationToken);
        try
        {
            var filePath = GetNetworkFilePath(gameType);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var lines = File.Exists(filePath)
                ? (await File.ReadAllLinesAsync(filePath, cancellationToken)).ToList()
                : new List<string>();

            var encoded = LanNicknameCodec.Encode(normalized);
            var replaced = false;
            for (var index = 0; index < lines.Count; index++)
            {
                if (replaced || !TrySplitKeyValue(lines[index], out var key, out _))
                {
                    continue;
                }

                if (string.Equals(key, GameSettingsConstants.Network.UserNameKey, StringComparison.OrdinalIgnoreCase))
                {
                    lines[index] = FormatEntry(encoded);
                    replaced = true;
                }
            }

            if (!replaced)
            {
                lines.Add(FormatEntry(encoded));
            }

            // Atomic replace: a crash mid-write must never leave a truncated
            // Network.ini behind. The temp file lives beside the target so the
            // move stays on one volume.
            var temporaryPath = filePath + ".tmp";
            await File.WriteAllLinesAsync(temporaryPath, lines, Encoding.UTF8, cancellationToken);
            File.Move(temporaryPath, filePath, overwrite: true);

            _logger.LogInformation("Saved LAN nickname for {GameType}", gameType);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to save LAN nickname for {GameType}", gameType);
            return OperationResult<bool>.CreateFailure($"Failed to save LAN nickname: {ex.Message}");
        }
        finally
        {
            _networkIniLock.Release();
        }
    }

    private static string FormatEntry(string encoded) =>
        $"{GameSettingsConstants.Network.UserNameKey} = {encoded}";

    private static bool TrySplitKeyValue(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return false;
        }

        key = line[..separatorIndex].Trim();
        value = line[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrEmpty(key);
    }
}
