using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Utilities;
using System;
using System.IO;

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

        var normalizedPath = executablePath.Replace('\\', '/');
        if (normalizedPath.EndsWith(ContentFormatConstants.MacAppBundleExtension, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(ContentFormatConstants.MacAppBundleExtension + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

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

        return File.Exists(executablePath)
            && ExecutableFileClassifier.HasNativeExecutableMagicBytes(executablePath);
    }

    /// <inheritdoc/>
    public GameClientIdentification? Identify(string executablePath)
    {
        if (!CanIdentify(executablePath))
        {
            return null;
        }

        var platform = ExecutableFileClassifier.DetectPlatform(executablePath);
        var fileName = Path.GetFileName(executablePath);

        var isGenerals = (fileName.Contains("generals", StringComparison.OrdinalIgnoreCase)
            || executablePath.Contains("Generals", StringComparison.OrdinalIgnoreCase))
            && !fileName.Contains("zh", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains("zerohour", StringComparison.OrdinalIgnoreCase)
            && !executablePath.Contains("ZeroHour", StringComparison.OrdinalIgnoreCase)
            && !executablePath.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase);

        var isZeroHour = fileName.Contains("zh", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("zerohour", StringComparison.OrdinalIgnoreCase)
            || executablePath.Contains("ZeroHour", StringComparison.OrdinalIgnoreCase)
            || executablePath.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase);

        GameType gameType;
        if (isGenerals)
        {
            gameType = GameType.Generals;
        }
        else if (isZeroHour)
        {
            gameType = GameType.ZeroHour;
        }
        else
        {
            gameType = GameType.Unknown;
        }

        var platformName = platform switch
        {
            ExecutablePlatform.Windows => "Windows",
            ExecutablePlatform.Linux => "Linux",
            ExecutablePlatform.MacOS => "macOS",
            _ => "Cross-Platform",
        };

        var displayName = gameType switch
        {
            GameType.Generals => $"{PublisherTypeConstants.CommunityDisplayName} {GameClientConstants.GeneralsShortName} ({platformName})",
            GameType.ZeroHour => $"{PublisherTypeConstants.CommunityDisplayName} {GameClientConstants.ZeroHourShortName} ({platformName})",
            _ => $"{PublisherTypeConstants.CommunityDisplayName} ({platformName})",
        };
        var variant = platformName.ToLowerInvariant();

        return new GameClientIdentification(
            publisherId: PublisherTypeConstants.Community,
            variant: variant,
            displayName: displayName,
            gameType: gameType,
            localVersion: null);
    }
}
