using GenHub.Core.Models.GenLauncher;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Parser for S3 XML ListBucketResult responses from MinIO or S3-compatible providers.
/// Supports AWS Signature Version 4 presigned URLs for authenticated repositories.
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
        return ParseListBucketResult(xmlContent, folderPrefix, s3Host, bucketName, out _, out _, null, null);
    }

    /// <summary>
    /// Parses an S3 ListBucketResult XML document, including pagination markers.
    /// </summary>
    /// <param name="xmlContent">The XML content returned from the S3 bucket list query.</param>
    /// <param name="folderPrefix">The folder prefix within the bucket.</param>
    /// <param name="s3Host">The S3 host (e.g. gen.insave.ovh:9000 or wasabi host).</param>
    /// <param name="bucketName">The bucket name.</param>
    /// <param name="isTruncated">Outputs whether more pages exist.</param>
    /// <param name="nextMarker">Outputs the next marker or continuation token if truncated.</param>
    /// <returns>A list of parsed file entries.</returns>
    public static List<GenLauncherS3FileEntry> ParseListBucketResult(
        string xmlContent,
        string folderPrefix,
        string s3Host,
        string bucketName,
        out bool isTruncated,
        out string? nextMarker)
    {
        return ParseListBucketResult(xmlContent, folderPrefix, s3Host, bucketName, out isTruncated, out nextMarker, null, null);
    }

    /// <summary>
    /// Parses an S3 ListBucketResult XML document, including pagination markers and S3 credentials for presigned download URLs.
    /// </summary>
    /// <param name="xmlContent">The XML content returned from the S3 bucket list query.</param>
    /// <param name="folderPrefix">The folder prefix within the bucket.</param>
    /// <param name="s3Host">The S3 host (e.g. gen.insave.ovh:9000 or wasabi host).</param>
    /// <param name="bucketName">The bucket name.</param>
    /// <param name="isTruncated">Outputs whether more pages exist.</param>
    /// <param name="nextMarker">Outputs the next marker or continuation token if truncated.</param>
    /// <param name="publicKey">Explicit S3 public key, or null to check defaults.</param>
    /// <param name="secretKey">Explicit S3 secret key, or null to check defaults.</param>
    /// <returns>A list of parsed file entries.</returns>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Overload with paging out parameters and explicit credentials")]
    public static List<GenLauncherS3FileEntry> ParseListBucketResult(
        string xmlContent,
        string folderPrefix,
        string s3Host,
        string bucketName,
        out bool isTruncated,
        out string? nextMarker,
        string? publicKey,
        string? secretKey)
    {
        isTruncated = false;
        nextMarker = null;
        var entries = new List<GenLauncherS3FileEntry>();
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return entries;
        }

        var doc = XDocument.Parse(xmlContent);

        var errorEl = doc.Root != null && doc.Root.Name.LocalName == "Error"
            ? doc.Root
            : doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Error");

        if (errorEl != null)
        {
            var code = errorEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Code")?.Value ?? "Unknown";
            var message = errorEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value ?? "S3 returned an error";
            throw new InvalidOperationException($"S3 Error: {code} - {message}");
        }

        var isTruncatedEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "IsTruncated")?.Value;
        if (bool.TryParse(isTruncatedEl, out var truncated))
        {
            isTruncated = truncated;
        }

        var nextMarkerEl = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NextMarker" || e.Name.LocalName == "NextContinuationToken")?.Value;
        if (!string.IsNullOrWhiteSpace(nextMarkerEl))
        {
            nextMarker = nextMarkerEl;
        }

        var normalizedFolder = (folderPrefix ?? string.Empty).TrimEnd('/') + "/";

        foreach (var contents in doc.Descendants().Where(e => e.Name.LocalName == "Contents"))
        {
            var entry = TryParseContentEntry(contents, folderPrefix, normalizedFolder, s3Host, bucketName, publicKey, secretKey);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        // If truncated but NextMarker was not provided, use the last Key as next marker (standard S3 ListObjects v1 behavior)
        if (isTruncated && string.IsNullOrWhiteSpace(nextMarker) && entries.Count > 0)
        {
            nextMarker = entries[^1].Key;
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
        else if (rawHost.Contains(":9000", StringComparison.OrdinalIgnoreCase) || rawHost.Contains(":8000", StringComparison.OrdinalIgnoreCase) || rawHost.Contains("gen.insave.ovh", StringComparison.OrdinalIgnoreCase))
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
    /// Builds the full S3 query URL for listing bucket keys with optional pagination marker and credentials signing.
    /// </summary>
    /// <param name="s3Host">The S3 host or endpoint.</param>
    /// <param name="bucketName">The S3 bucket name.</param>
    /// <param name="folderPrefix">The prefix folder path.</param>
    /// <param name="marker">Optional pagination marker/continuation token.</param>
    /// <param name="publicKey">Explicit S3 public key, or null to check defaults.</param>
    /// <param name="secretKey">Explicit S3 secret key, or null to check defaults.</param>
    /// <returns>The constructed query URL.</returns>
    public static string BuildS3QueryUrl(
        string? s3Host,
        string bucketName,
        string? folderPrefix,
        string? marker = null,
        string? publicKey = null,
        string? secretKey = null)
    {
        if (string.IsNullOrWhiteSpace(s3Host))
        {
            return string.Empty;
        }

        var extraParams = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(folderPrefix))
        {
            extraParams["prefix"] = folderPrefix;
        }

        if (!string.IsNullOrWhiteSpace(marker))
        {
            extraParams["marker"] = marker;
        }

        return GenLauncherS3Signer.GeneratePresignedGetUrl(
            s3Host,
            bucketName,
            objectKey: null,
            publicKey: publicKey,
            secretKey: secretKey,
            extraQueryParams: extraParams);
    }

    private static GenLauncherS3FileEntry? TryParseContentEntry(
        XElement contents,
        string? folderPrefix,
        string normalizedFolder,
        string s3Host,
        string bucketName,
        string? publicKey,
        string? secretKey)
    {
        var key = contents.Elements().FirstOrDefault(e => e.Name.LocalName == "Key")?.Value;
        if (string.IsNullOrWhiteSpace(key) || key.EndsWith('/'))
        {
            return null;
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

        var downloadUrl = GenLauncherS3Signer.GeneratePresignedGetUrl(
            s3Host,
            bucketName,
            key,
            publicKey,
            secretKey);

        return new GenLauncherS3FileEntry
        {
            Key = key,
            RelativePath = relativePath,
            ETag = cleanedEtag,
            Size = size,
            DownloadUrl = downloadUrl,
        };
    }
}
