using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Manifest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides shared helpers for attributing downloads to publishers and reporting
/// download telemetry with consistent property keys and fallback values.
/// </summary>
public static partial class DownloadTelemetryHelper
{
    private static readonly (string[] Hosts, string PublisherId)[] KnownHostPublishers =
    [
        ([TelemetryConstants.DownloadAttribution.GitHubHost, TelemetryConstants.DownloadAttribution.GitHubUserContentHost], TelemetryConstants.DownloadAttribution.GitHub),
        (TelemetryConstants.DownloadAttribution.GoogleDriveHosts, TelemetryConstants.DownloadAttribution.GoogleDrive),
        (TelemetryConstants.DownloadAttribution.OneDriveHosts, TelemetryConstants.DownloadAttribution.OneDrive),
        ([TelemetryConstants.DownloadAttribution.CommunityOutpostHost], TelemetryConstants.DownloadAttribution.CommunityOutpost),
        ([TelemetryConstants.DownloadAttribution.GeneralsOnlineHost], TelemetryConstants.DownloadAttribution.GeneralsOnline),
        ([TelemetryConstants.DownloadAttribution.GenToolHost], TelemetryConstants.DownloadAttribution.GenTool),
    ];

    /// <summary>
    /// Copies publisher attribution from a content manifest onto a download configuration,
    /// preserving any values the caller already set explicitly.
    /// </summary>
    /// <param name="configuration">The download configuration to enrich.</param>
    /// <param name="manifest">The manifest describing the content being downloaded.</param>
    /// <param name="providerFallback">Provider identifier used when the manifest carries no publisher name.</param>
    public static void ApplyManifestAttribution(DownloadConfiguration configuration, ContentManifest manifest, string? providerFallback = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(manifest);

        var publisherId = FirstNonEmpty(SkipUnknown(manifest.Publisher?.PublisherType), manifest.OriginalProviderName, providerFallback);
        var publisherName = FirstNonEmpty(manifest.Publisher?.Name, manifest.OriginalProviderName, providerFallback);
        if (string.IsNullOrWhiteSpace(configuration.PublisherId))
        {
            configuration.PublisherId = publisherId;
        }

        if (string.IsNullOrWhiteSpace(configuration.Author))
        {
            configuration.Author = publisherName;
        }

        if (string.IsNullOrWhiteSpace(configuration.ContentName))
        {
            configuration.ContentName = FirstNonEmpty(manifest.Name, null);
        }

        if (string.IsNullOrWhiteSpace(configuration.ContentId))
        {
            configuration.ContentId = FirstNonEmpty(manifest.Id.Value, configuration.ContentName);
        }

        if (string.IsNullOrWhiteSpace(configuration.ContentType))
        {
            configuration.ContentType = manifest.ContentType.ToString();
        }
    }

    /// <summary>
    /// Resolves the publisher identifier for a download, preferring explicit attribution
    /// and falling back to well-known host inference and finally the unknown sentinel.
    /// </summary>
    /// <param name="configuration">The download configuration.</param>
    /// <returns>The resolved publisher identifier, never null or empty.</returns>
    public static string ResolvePublisherId(DownloadConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.IsNullOrWhiteSpace(configuration.PublisherId))
        {
            return configuration.PublisherId;
        }

        var host = configuration.Url?.Host;
        if (!string.IsNullOrWhiteSpace(host))
        {
            if (ModDBConstants.IsModDbOrDbolicalHost(host))
            {
                return TelemetryConstants.DownloadAttribution.ModDb;
            }

            return KnownHostPublishers
                .Where(entry => entry.Hosts.Any(knownHost => HostMatches(host, knownHost)))
                .Select(entry => entry.PublisherId)
                .FirstOrDefault() ?? TelemetryConstants.DownloadAttribution.Unknown;
        }

        return TelemetryConstants.DownloadAttribution.Unknown;
    }

    /// <summary>
    /// Resolves the content name for a download, preferring explicit attribution
    /// and falling back to the destination file name and finally the unknown sentinel.
    /// </summary>
    /// <param name="configuration">The download configuration.</param>
    /// <returns>The resolved content name, never null or empty.</returns>
    public static string ResolveContentName(DownloadConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.IsNullOrWhiteSpace(configuration.ContentName))
        {
            return configuration.ContentName;
        }

        if (!string.IsNullOrWhiteSpace(configuration.DestinationPath))
        {
            var fileName = Path.GetFileName(configuration.DestinationPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return TelemetryConstants.DownloadAttribution.Unknown;
    }

    /// <summary>
    /// Resolves the content identifier for a download, falling back to the content name.
    /// </summary>
    /// <param name="configuration">The download configuration.</param>
    /// <returns>The resolved content identifier, never null or empty.</returns>
    public static string ResolveContentId(DownloadConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.IsNullOrWhiteSpace(configuration.ContentId))
        {
            return configuration.ContentId;
        }

        return ResolveContentName(configuration);
    }

    /// <summary>
    /// Resolves the author for a download, falling back to the unknown sentinel.
    /// </summary>
    /// <param name="configuration">The download configuration.</param>
    /// <returns>The resolved author, never null or empty.</returns>
    public static string ResolveAuthor(DownloadConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.IsNullOrWhiteSpace(configuration.Author))
        {
            return configuration.Author;
        }

        return TelemetryConstants.DownloadAttribution.Unknown;
    }

    /// <summary>
    /// Tracks a completed download with throughput and attribution properties.
    /// </summary>
    /// <param name="telemetryService">The telemetry service, or null to skip tracking.</param>
    /// <param name="configuration">The download configuration.</param>
    /// <param name="downloadedBytes">The number of bytes downloaded.</param>
    /// <param name="elapsed">The time the download took.</param>
    public static void TrackDownloadCompleted(ITelemetryService? telemetryService, DownloadConfiguration configuration, long downloadedBytes, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var totalElapsedSeconds = elapsed.TotalSeconds;
        var sizeMb = downloadedBytes / (1024.0 * 1024.0);
        var speedMbps = totalElapsedSeconds > 0 ? (sizeMb * 8.0) / totalElapsedSeconds : 0.0;
        var publisherId = ResolvePublisherId(configuration);
        var contentName = ResolveContentName(configuration);
        var contentId = ResolveContentId(configuration);

        var downloadProperties = new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.SizeMb] = Math.Round(sizeMb, 2),
            [TelemetryConstants.Properties.DurationSeconds] = Math.Round(totalElapsedSeconds, 2),
            [TelemetryConstants.Properties.SpeedMbps] = Math.Round(speedMbps, 2),
            [TelemetryConstants.Properties.ContentType] = configuration.ContentType ?? TelemetryConstants.DownloadAttribution.DefaultContentType,
            [TelemetryConstants.Properties.PublisherId] = publisherId,
            [TelemetryConstants.Properties.Publisher] = publisherId,
            [TelemetryConstants.Properties.ContentId] = contentId,
            [TelemetryConstants.Properties.ContentName] = contentName,
            [TelemetryConstants.Properties.Content] = contentName,
            [TelemetryConstants.Properties.Package] = contentName,
            [TelemetryConstants.Properties.Author] = ResolveAuthor(configuration),
            [TelemetryConstants.Properties.FileName] = Path.GetFileName(configuration.DestinationPath),
        };

        telemetryService?.TrackEvent(TelemetryConstants.Events.ContentDownloadCompleted, downloadProperties);
    }

    /// <summary>
    /// Tracks a failed download with attribution and error properties.
    /// </summary>
    /// <param name="telemetryService">The telemetry service, or null to skip tracking.</param>
    /// <param name="configuration">The download configuration.</param>
    /// <param name="errorMessage">The failure message.</param>
    /// <param name="elapsed">The time spent before failing, when known.</param>
    public static void TrackDownloadFailure(ITelemetryService? telemetryService, DownloadConfiguration configuration, string errorMessage, TimeSpan? elapsed = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(errorMessage);

        var publisherId = ResolvePublisherId(configuration);
        var contentName = ResolveContentName(configuration);
        var contentId = ResolveContentId(configuration);

        var properties = new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.ContentType] = configuration.ContentType ?? TelemetryConstants.DownloadAttribution.DefaultContentType,
            [TelemetryConstants.Properties.PublisherId] = publisherId,
            [TelemetryConstants.Properties.Publisher] = publisherId,
            [TelemetryConstants.Properties.ContentId] = contentId,
            [TelemetryConstants.Properties.ContentName] = contentName,
            [TelemetryConstants.Properties.Content] = contentName,
            [TelemetryConstants.Properties.Package] = contentName,
            [TelemetryConstants.Properties.Author] = ResolveAuthor(configuration),
            [TelemetryConstants.Properties.FileName] = Path.GetFileName(configuration.DestinationPath),
            [TelemetryConstants.Properties.ErrorMessage] = RedactUrls(errorMessage),
        };

        if (elapsed.HasValue)
        {
            properties[TelemetryConstants.Properties.DurationSeconds] = Math.Round(elapsed.Value.TotalSeconds, 2);
        }

        telemetryService?.TrackEvent(TelemetryConstants.Events.ContentDownloadFailed, properties);
    }

    private static bool HostMatches(string host, string knownHost)
    {
        var normalizedHost = host.TrimEnd('.');
        return normalizedHost.Equals(knownHost, StringComparison.OrdinalIgnoreCase) ||
            normalizedHost.EndsWith("." + knownHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

    private static string? SkipUnknown(string? value) =>
        string.Equals(value, PublisherTypeConstants.Unknown, StringComparison.OrdinalIgnoreCase) ? null : value;

    private static string RedactUrls(string message) => UrlPattern().Replace(message, TelemetryConstants.UrlMask);

    [GeneratedRegex(@"https?://[^\s""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlPattern();
}
