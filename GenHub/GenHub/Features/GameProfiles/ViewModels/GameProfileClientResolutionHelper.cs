using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Features.GameProfiles.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Helper methods for resolving and hydrating game clients for profile settings.
/// </summary>
internal static class GameProfileClientResolutionHelper
{
    /// <summary>
    /// Resolves the active game client from enabled content items and the selected installation.
    /// </summary>
    /// <param name="enabledContent">The enabled content items.</param>
    /// <param name="selectedInstallation">The selected installation item.</param>
    /// <param name="existingClient">Optional existing client from the profile being updated, to preserve metadata.</param>
    /// <returns>The resolved active game client, or null.</returns>
    internal static GameClient? ResolveActiveGameClient(
        IEnumerable<ContentDisplayItem> enabledContent,
        ContentDisplayItem? selectedInstallation,
        GameClient? existingClient = null)
    {
        var enabledClientItem = enabledContent.FirstOrDefault(c => c.IsEnabled && c.ContentType == ContentType.GameClient);
        var resolvedClient = ResolveClientFromDisplayItem(enabledClientItem, selectedInstallation?.SourceId);

        if (resolvedClient != null)
        {
            ApplyInstallationIdIfCompatible(resolvedClient, selectedInstallation);
            HydrateClientPaths(resolvedClient, selectedInstallation?.GameClient);
            HydrateClientMetadata(resolvedClient, existingClient);
            return resolvedClient;
        }

        var baseClient = selectedInstallation?.GameClient?.Clone();
        if (baseClient != null)
        {
            HydrateClientMetadata(baseClient, existingClient);
        }

        return baseClient;
    }

    /// <summary>
    /// Hydrates target client executable and working directory paths from a source client if missing.
    /// </summary>
    /// <param name="target">The target game client.</param>
    /// <param name="source">The source game client.</param>
    internal static void HydrateClientPaths(GameClient target, GameClient? source)
    {
        if (source == null || target.GameType != source.GameType)
        {
            return;
        }

        if (string.IsNullOrEmpty(target.ExecutablePath))
        {
            target.ExecutablePath = source.ExecutablePath;
        }

        if (string.IsNullOrEmpty(target.WorkingDirectory))
        {
            target.WorkingDirectory = source.WorkingDirectory;
        }
    }

    /// <summary>
    /// Preserves client metadata from an existing client when saving or updating a profile.
    /// </summary>
    /// <param name="target">The target game client being resolved.</param>
    /// <param name="existing">The existing client stored on the profile.</param>
    internal static void HydrateClientMetadata(GameClient target, GameClient? existing)
    {
        if (existing == null)
        {
            return;
        }

        if (string.Equals(target.Id, existing.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(target.CommandLineArgs))
            {
                target.CommandLineArgs = existing.CommandLineArgs;
            }

            if (target.BuildDate == default)
            {
                target.BuildDate = existing.BuildDate;
            }

            target.CreatedAt = existing.CreatedAt;
            target.LastDetected = existing.LastDetected;
        }
    }

    /// <summary>
    /// Creates a GameClient model from a ContentDisplayItem.
    /// </summary>
    /// <param name="item">The content display item.</param>
    /// <param name="installationId">The installation ID.</param>
    /// <returns>A new GameClient instance.</returns>
    internal static GameClient CreateGameClientFromDisplayItem(ContentDisplayItem item, string? installationId)
    {
        var manifestIdValue = item.ManifestId.Value ?? string.Empty;
        var segments = manifestIdValue.Split([ManifestConstants.ManifestIdSegmentSeparator], StringSplitOptions.None);
        var publisherType = ExtractPublisherType(segments, item.Publisher);
        var version = ExtractClientVersion(item.Version, segments, publisherType);

        var exePath = item.GameClient?.ExecutablePath;
        var workingDir = item.GameClient?.WorkingDirectory;

        if (string.IsNullOrEmpty(exePath) && !string.IsNullOrWhiteSpace(item.SourcePath) && File.Exists(item.SourcePath))
        {
            exePath = item.SourcePath;
            workingDir = Path.GetDirectoryName(item.SourcePath);
        }

        return new GameClient
        {
            Id = manifestIdValue,
            Name = !string.IsNullOrWhiteSpace(item.DisplayName) ? item.DisplayName : manifestIdValue,
            Version = version,
            GameType = item.GameType,
            SourceType = ContentType.GameClient,
            PublisherType = publisherType,
            InstallationId = installationId,
            ExecutablePath = exePath ?? string.Empty,
            WorkingDirectory = workingDir ?? string.Empty,
        };
    }

    /// <summary>
    /// Extracts the publisher type from manifest ID segments or publisher string.
    /// </summary>
    /// <param name="segments">The manifest ID segments.</param>
    /// <param name="itemPublisher">The publisher name from the item.</param>
    /// <returns>The publisher type string, or null.</returns>
    internal static string? ExtractPublisherType(string[] segments, string? itemPublisher)
    {
        if (segments.Length >= 4)
        {
            return segments[2].ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(itemPublisher))
        {
            return itemPublisher.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        }

        return null;
    }

    /// <summary>
    /// Extracts the client version string from item version or manifest segments.
    /// </summary>
    /// <param name="currentVersion">The current item version.</param>
    /// <param name="segments">The manifest ID segments.</param>
    /// <param name="publisherType">The publisher type string.</param>
    /// <returns>The extracted version string.</returns>
    internal static string ExtractClientVersion(string? currentVersion, string[] segments, string? publisherType)
    {
        if (!string.IsNullOrWhiteSpace(currentVersion))
        {
            return currentVersion;
        }

        if (segments.Length < 4 || string.IsNullOrEmpty(segments[1]))
        {
            return string.Empty;
        }

        var versionSegment = segments[1];
        if (int.TryParse(versionSegment, out var verNum) && verNum > 0)
        {
            return GameVersionHelper.FormatNumericManifestVersion(verNum, publisherType, includePrefix: false);
        }

        return !string.Equals(versionSegment, "0", StringComparison.OrdinalIgnoreCase)
            ? versionSegment
            : string.Empty;
    }

    /// <summary>
    /// Resolves a game client from an enabled content display item if present.
    /// </summary>
    /// <param name="item">The enabled content display item.</param>
    /// <param name="sourceId">The optional installation source ID.</param>
    /// <returns>The resolved game client, or null.</returns>
    private static GameClient? ResolveClientFromDisplayItem(ContentDisplayItem? item, string? sourceId)
    {
        if (item == null)
        {
            return null;
        }

        if (item.GameClient != null)
        {
            var client = item.GameClient.Clone();
            if (string.IsNullOrEmpty(client.PublisherType))
            {
                var manifestIdValue = item.ManifestId.Value ?? string.Empty;
                var segments = manifestIdValue.Split([ManifestConstants.ManifestIdSegmentSeparator], StringSplitOptions.None);
                client.PublisherType = ExtractPublisherType(segments, item.Publisher);
            }

            return client;
        }

        if (item.Manifest != null)
        {
            return ProfileContentLoader.CreateGameClientFromManifest(item.Manifest, sourceId);
        }

        if (!string.IsNullOrEmpty(item.ManifestId))
        {
            return CreateGameClientFromDisplayItem(item, sourceId);
        }

        return null;
    }

    /// <summary>
    /// Stamps the installation source ID onto the resolved client if compatible with the game type.
    /// </summary>
    /// <param name="resolvedClient">The resolved game client.</param>
    /// <param name="selectedInstallation">The selected installation item.</param>
    private static void ApplyInstallationIdIfCompatible(GameClient resolvedClient, ContentDisplayItem? selectedInstallation)
    {
        if (selectedInstallation == null)
        {
            return;
        }

        var isInstallationMatch = selectedInstallation.GameType == GameType.Unknown ||
            resolvedClient.GameType == selectedInstallation.GameType;
        var isClientMatch = selectedInstallation.GameClient == null ||
            resolvedClient.GameType == selectedInstallation.GameClient.GameType;

        if (isInstallationMatch && isClientMatch)
        {
            var installationSourceId = selectedInstallation.SourceId ?? selectedInstallation.GameClient?.InstallationId;
            if (!string.IsNullOrEmpty(installationSourceId))
            {
                resolvedClient.InstallationId = installationSourceId;
            }
        }
    }
}
