using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Utilities;
using System;
using System.IO;
using System.Linq;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Identifies generic community game client executables on Unix: Flatpak bundles, macOS
/// application bundles, and extensionless native binaries verified by content. Windows
/// executables are deliberately excluded: retail, GeneralsOnline, and SuperHackers
/// identifiers own those names, and claiming every .exe would flood installations with
/// phantom clients.
/// </summary>
public class CommunityGameClientIdentifier : IGameClientIdentifier
{
    private static readonly char[] BoundedTokenSeparators = ['.', '-', '_', ' ', '+', '/', '\\', '(', ')', '[', ']'];

    /// <inheritdoc/>
    public string PublisherId => PublisherTypeConstants.Community;

    /// <inheritdoc/>
    public bool CanIdentify(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(executablePath);
        if (fileName.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsMacBundlePath(executablePath))
        {
            return IsSupportedMacBundle(executablePath);
        }

        return IsSupportedNativeBinary(executablePath, fileName);
    }

    /// <inheritdoc/>
    public GameClientIdentification? Identify(string executablePath)
    {
        if (!CanIdentify(executablePath))
        {
            return null;
        }

        var targetBinary = ResolveTargetBinary(executablePath) ?? executablePath;
        var (isExcluded, gameType) = InspectGameBinary(targetBinary, executablePath);
        if (isExcluded)
        {
            return null;
        }

        if (gameType == GameType.Unknown)
        {
            gameType = InferGameTypeFromTokens(executablePath);
        }

        var platform = ExecutableFileClassifier.DetectPlatform(executablePath);
        var platformName = ResolvePlatformDisplayName(platform);
        var displayName = FormatDisplayName(gameType, platformName);
        var variant = platformName.ToLowerInvariant();

        return new GameClientIdentification(
            publisherId: PublisherTypeConstants.Community,
            variant: variant,
            displayName: displayName,
            gameType: gameType,
            localVersion: null);
    }

    private static bool IsMacBundlePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.EndsWith(ContentFormatConstants.MacAppBundleExtension, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(ContentFormatConstants.MacAppBundleExtension + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedMacBundle(string executablePath)
    {
        var targetBinary = ResolveTargetBinary(executablePath);
        if (targetBinary is null || !File.Exists(targetBinary))
        {
            return true;
        }

        return !IsExcludedBinaryRole(targetBinary);
    }

    private static bool IsSupportedNativeBinary(string executablePath, string fileName)
    {
        // Extensionless is the shape of a native binary but also of a README, so content
        // verification is required: only ELF/Mach-O magic counts, never the name alone.
        if (!string.IsNullOrEmpty(Path.GetExtension(fileName)))
        {
            return false;
        }

        if (ExecutableFileClassifier.IsLibraryFile(executablePath)
            || fileName.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(executablePath) || !ExecutableFileClassifier.HasNativeExecutableMagicBytes(executablePath))
        {
            return false;
        }

        return !IsExcludedBinaryRole(executablePath);
    }

    private static bool IsExcludedBinaryRole(string binaryPath)
    {
        var inspect = GameBinaryInspector.Inspect(binaryPath);
        return inspect.Success && inspect.Data.Role is GameBinaryRole.Tool or GameBinaryRole.Installer or GameBinaryRole.DotNetLauncher;
    }

    private static (bool IsExcluded, GameType GameType) InspectGameBinary(string targetBinary, string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        if (!File.Exists(targetBinary) || fileName.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase))
        {
            return (false, GameType.Unknown);
        }

        var inspect = GameBinaryInspector.Inspect(targetBinary);
        if (!inspect.Success)
        {
            return (false, GameType.Unknown);
        }

        if (inspect.Data.Role is GameBinaryRole.Tool or GameBinaryRole.Installer or GameBinaryRole.DotNetLauncher)
        {
            return (true, GameType.Unknown);
        }

        if (inspect.Data.GameType is GameType.Generals or GameType.ZeroHour)
        {
            return (false, inspect.Data.GameType);
        }

        return (false, GameType.Unknown);
    }

    private static GameType InferGameTypeFromTokens(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        if (ContainsZeroHourToken(fileName) || ContainsZeroHourToken(executablePath))
        {
            return GameType.ZeroHour;
        }

        if (ContainsGeneralsToken(fileName) || ContainsGeneralsToken(executablePath))
        {
            return GameType.Generals;
        }

        return GameType.Unknown;
    }

    private static string ResolvePlatformDisplayName(ExecutablePlatform platform) => platform switch
    {
        ExecutablePlatform.Windows => GameClientConstants.PlatformWindowsDisplayName,
        ExecutablePlatform.Linux => GameClientConstants.PlatformLinuxDisplayName,
        ExecutablePlatform.MacOS => GameClientConstants.PlatformMacOSDisplayName,
        _ => GameClientConstants.PlatformCrossPlatformDisplayName,
    };

    private static string FormatDisplayName(GameType gameType, string platformName) => gameType switch
    {
        GameType.Generals => $"{PublisherTypeConstants.CommunityDisplayName} {GameClientConstants.GeneralsShortName} ({platformName})",
        GameType.ZeroHour => $"{PublisherTypeConstants.CommunityDisplayName} {GameClientConstants.ZeroHourShortName} ({platformName})",
        _ => $"{PublisherTypeConstants.CommunityDisplayName} ({platformName})",
    };

    private static string? ResolveTargetBinary(string executablePath)
    {
        var normalizedPath = executablePath.Replace('\\', '/');
        if (normalizedPath.EndsWith(ContentFormatConstants.MacAppBundleExtension, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(ContentFormatConstants.MacAppBundleExtension + "/", StringComparison.OrdinalIgnoreCase))
        {
            return GameClientEntryDetector.ResolveBundleExecutableAbsolute(executablePath);
        }

        return executablePath;
    }

    private static bool ContainsZeroHourToken(string text)
    {
        if (text.Contains("zerohour", StringComparison.OrdinalIgnoreCase)
            || text.Contains("zero-hour", StringComparison.OrdinalIgnoreCase)
            || text.Contains("zero hour", StringComparison.OrdinalIgnoreCase)
            || text.Contains("generalszh", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = text.Split(BoundedTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(t =>
            t.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || t.Equals("zerohour", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("zh", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsGeneralsToken(string text)
    {
        var tokens = text.Split(BoundedTokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(t => t.Equals("generals", StringComparison.OrdinalIgnoreCase) || t.StartsWith("generals", StringComparison.OrdinalIgnoreCase));
    }
}
