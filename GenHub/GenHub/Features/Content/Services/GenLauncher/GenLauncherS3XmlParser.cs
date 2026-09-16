using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using GenHub.Core.Models.GenLauncher;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Parser for S3 XML ListBucketResult responses from MinIO or S3-compatible providers.
/// </summary>
public static class GenLauncherS3XmlParser
{
    /// <summary>
    /// Parses an S3 ListBucketResult XML document.
    /// </summary>
    /// <param name="xmlContent">The XML content returned from the S3 bucket list query.</param>
    /// <param name="folderPrefix">The folder prefix within the bucket.</param>
    /// <param name="s3Host">The S3 host (e.g. gen.insave.ovh:9000 or wasabi host).</param>
    /// <param name="bucketName">The bucket name.</param>
    /// <returns>A list of parsed file entries.</returns>
    public static List<GenLauncherS3FileEntry> ParseListBucketResult(
        string xmlContent,
        string folderPrefix,
        string s3Host,
        string bucketName)
    {
        var entries = new List<GenLauncherS3FileEntry>();
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return entries;
        }

        var doc = XDocument.Parse(xmlContent);
        var normalizedFolder = (folderPrefix ?? string.Empty).TrimEnd('/') + "/";
        var (scheme, host) = NormalizeHostAndScheme(s3Host);

        foreach (var contents in doc.Descendants().Where(e => e.Name.LocalName == "Contents"))
        {
            var key = contents.Elements().FirstOrDefault(e => e.Name.LocalName == "Key")?.Value;
            if (string.IsNullOrWhiteSpace(key) || key.EndsWith('/'))
            {
                continue;
            }

            var etag = contents.Elements().FirstOrDefault(e => e.Name.LocalName == "ETag")?.Value;
            var cleanedEtag = etag?.Trim('\"', ' ', '&', 'q', 'u', 'o', 't', ';') ?? string.Empty;
            // Clean any remaining quotes
            cleanedEtag = cleanedEtag.Replace("\"", string.Empty).Trim();

            var sizeStr = contents.Elements().FirstOrDefault(e => e.Name.LocalName == "Size")?.Value;
            long.TryParse(sizeStr, out var size);

            var relativePath = key;
            if (!string.IsNullOrEmpty(folderPrefix))
            {
                if (key.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = key[normalizedFolder.Length..];
                }
                else if (key.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = key[folderPrefix.Length..].TrimStart('/');
                }
            }

            // Direct download URL: reuse the listing scheme; GenLauncher MinIO endpoints are plain HTTP
            var downloadUrl = $"{scheme}://{host}/{bucketName}/{key.TrimStart('/')}";

            entries.Add(new GenLauncherS3FileEntry
            {
                Key = key,
                RelativePath = relativePath,
                ETag = cleanedEtag,
                Size = size,
                DownloadUrl = downloadUrl,
            });
        }

        return entries;
    }

    /// <summary>
    /// Normalizes the host string and determines scheme (plain HTTP for MinIO/custom ports, HTTPS by default).
    /// </summary>
    /// <param name="s3Host">The raw host or endpoint string.</param>
    /// <returns>A tuple containing the scheme and cleaned host.</returns>
    public static (string Scheme, string Host) NormalizeHostAndScheme(string? s3Host)
    {
        var rawHost = (s3Host ?? string.Empty).Trim();
        string scheme;
        if (rawHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "https";
        }
        else if (rawHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "http";
        }
        else if (rawHost.Contains(":9000") || rawHost.Contains(":80") || rawHost.Contains("gen.insave.ovh", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "http";
        }
        else
        {
            scheme = "https";
        }

        var host = rawHost;
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            host = host["http://".Length..];
        }
        else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            host = host["https://".Length..];
        }

        host = host.TrimEnd('/');
        return (scheme, host);
    }

    /// <summary>
    /// Builds the full S3 query URL for listing bucket keys.
    /// </summary>
    /// <param name="s3Host">The S3 host or endpoint.</param>
    /// <param name="bucketName">The S3 bucket name.</param>
    /// <param name="folderPrefix">The prefix folder path.</param>
    /// <returns>The constructed query URL.</returns>
    public static string BuildS3QueryUrl(string? s3Host, string bucketName, string? folderPrefix)
    {
        var (scheme, host) = NormalizeHostAndScheme(s3Host);
        return $"{scheme}://{host}/{bucketName}?prefix={Uri.EscapeDataString(folderPrefix ?? string.Empty)}";
    }
}
