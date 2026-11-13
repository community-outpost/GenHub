using GenHub.Core.Constants;
using GenHub.Core.Models.GenLauncher;
using GenHub.Core.Models.Results;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Parser for S3 XML ListBucketResult responses from MinIO or S3-compatible providers.
/// Supports AWS Signature Version 4 presigned URLs for authenticated repositories.
/// </summary>
public static class GenLauncherS3XmlParser
{
    private sealed record S3ParseContext(
        string? FolderPrefix,
        string NormalizedFolder,
        string S3Host,
        string BucketName,
        string? PublicKey,
        string? SecretKey,
        bool UseAuth);

    /// <summary>
    /// Parses an S3 ListBucketResult XML document.
    /// </summary>
    /// <param name="xmlContent">The XML content returned from the S3 bucket list query.</param>
    /// <param name="folderPrefix">The folder prefix within the bucket.</param>
    /// <param name="s3Host">The S3 host (e.g. gen.insave.ovh:9000 or wasabi host).</param>
    /// <param name="bucketName">The bucket name.</param>
    /// <returns>An operation result containing a list of parsed file entries.</returns>
    public static OperationResult<List<GenLauncherS3FileEntry>> ParseListBucketResult(
        string xmlContent,
        string folderPrefix,
        string s3Host,
        string bucketName)
    {
        return ParseListBucketResult(xmlContent, folderPrefix, s3Host, bucketName, out _, out _, null, null, true);
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
    /// <returns>An operation result containing a list of parsed file entries.</returns>
    public static OperationResult<List<GenLauncherS3FileEntry>> ParseListBucketResult(
        string xmlContent,
        string folderPrefix,
        string s3Host,
        string bucketName,
        out bool isTruncated,
        out string? nextMarker)
    {
        return ParseListBucketResult(xmlContent, folderPrefix, s3Host, bucketName, out isTruncated, out nextMarker, null, null, true);
    }

    /// <summary>
    /// Parses an S3 ListBucketResult XML document, specifying whether to sign download URLs.
    /// </summary>
    /// <param name="xmlContent">The XML content returned from the S3 bucket list query.</param>
    /// <param name="folderPrefix">The folder prefix within the bucket.</param>
    /// <param name="s3Host">The S3 host (e.g. gen.insave.ovh:9000 or wasabi host).</param>
    /// <param name="bucketName">The bucket name.</param>
    /// <param name="isTruncated">Outputs whether more pages exist.</param>
    /// <param name="nextMarker">Outputs the next marker or continuation token if truncated.</param>
    /// <param name="useAuth">Whether to sign file download URLs.</param>
    /// <returns>An operation result containing a list of parsed file entries.</returns>
    public static OperationResult<List<GenLauncherS3FileEntry>> ParseListBucketResult(
        string xmlContent,
        string folderPrefix,
        string s3Host,
        string bucketName,
        out bool isTruncated,
        out string? nextMarker,
        bool useAuth)
    {
        return ParseListBucketResult(xmlContent, folderPrefix, s3Host, bucketName, out isTruncated, out nextMarker, null, null, useAuth);
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
    /// <param name="useAuth">Whether to sign file download URLs.</param>
    /// <returns>An operation result containing a list of parsed file entries.</returns>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Overload with paging out parameters and explicit credentials")]
    public static OperationResult<List<GenLauncherS3FileEntry>> ParseListBucketResult(
        string xmlContent,
        string folderPrefix,
        string s3Host,
        string bucketName,
        out bool isTruncated,
        out string? nextMarker,
        string? publicKey,
        string? secretKey,
        bool useAuth = true)
    {
        isTruncated = false;
        nextMarker = null;
        var entries = new List<GenLauncherS3FileEntry>();
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return OperationResult<List<GenLauncherS3FileEntry>>.CreateSuccess(entries);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xmlContent);
        }
        catch (XmlException ex)
        {
            return OperationResult<List<GenLauncherS3FileEntry>>.CreateFailure($"Invalid S3 XML response: {ex.Message}");
        }

        var errorEl = doc.Root != null && doc.Root.Name.LocalName == "Error"
            ? doc.Root
            : doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Error");

        if (errorEl != null)
        {
            var code = errorEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Code")?.Value ?? "Unknown";
            var message = errorEl.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value ?? "S3 returned an error";
            return OperationResult<List<GenLauncherS3FileEntry>>.CreateFailure($"S3 Error: {code} - {message}");
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
        var context = new S3ParseContext(
            folderPrefix,
            normalizedFolder,
            s3Host,
            bucketName,
            publicKey,
            secretKey,
            useAuth);

        foreach (var contents in doc.Descendants().Where(e => e.Name.LocalName == "Contents"))
        {
            var entry = TryParseContentEntry(contents, context);
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

        return OperationResult<List<GenLauncherS3FileEntry>>.CreateSuccess(entries);
    }

    /// <summary>
    /// Normalizes the host string and determines scheme (plain HTTP for MinIO/custom ports, HTTPS by default).
    /// </summary>
    /// <param name="s3Host">The raw host or endpoint string.</param>
    /// <returns>A tuple containing the scheme and cleaned host.</returns>
    public static (string Scheme, string Host) NormalizeHostAndScheme(string? s3Host)
    {
        var (scheme, hostHeader, pathPrefix) = GenLauncherS3Signer.NormalizeHostAndPath(s3Host ?? string.Empty);
        var host = !string.IsNullOrEmpty(pathPrefix) ? $"{hostHeader}/{pathPrefix}" : hostHeader;
        return (scheme, host);
    }

    /// <summary>
    /// Builds the full S3 query URL for listing bucket keys with optional pagination marker and credentials signing.
    /// </summary>
    /// <param name="s3Host">The S3 host or endpoint.</param>
    /// <param name="bucketName">The S3 bucket name.</param>
    /// <param name="folderPrefix">The prefix folder path.</param>
    /// <param name="marker">Optional pagination marker/continuation token.</param>
    /// <param name="credentials">Optional S3 credentials.</param>
    /// <param name="useAuth">Whether to sign the request using AWS4.</param>
    /// <param name="region">AWS region string (defaults to us-east-1).</param>
    /// <returns>The constructed query URL.</returns>
    public static string BuildS3QueryUrl(
        string? s3Host,
        string bucketName,
        string? folderPrefix,
        string? marker = null,
        S3Credentials? credentials = null,
        bool useAuth = true,
        string region = GenLauncherConstants.DefaultS3Region)
    {
        if (string.IsNullOrWhiteSpace(s3Host))
        {
            return string.Empty;
        }

        var extraParams = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(folderPrefix))
        {
            var normalizedPrefix = folderPrefix.Trim();
            if (!normalizedPrefix.EndsWith('/'))
            {
                normalizedPrefix += "/";
            }

            extraParams["prefix"] = normalizedPrefix;
        }

        if (!string.IsNullOrWhiteSpace(marker))
        {
            extraParams["marker"] = marker;
        }

        return GenLauncherS3Signer.GeneratePresignedGetUrl(
            s3Host,
            bucketName,
            objectKey: null,
            publicKey: credentials?.PublicKey,
            secretKey: credentials?.SecretKey,
            extraQueryParams: extraParams,
            forceUnsigned: !useAuth,
            region: region);
    }

    private static GenLauncherS3FileEntry? TryParseContentEntry(
        XElement contents,
        S3ParseContext context)
    {
        var key = contents.Elements().FirstOrDefault(e => e.Name.LocalName == "Key")?.Value;
        if (string.IsNullOrWhiteSpace(key) || key.EndsWith('/'))
        {
            return null;
        }

        var etag = contents.Elements().FirstOrDefault(e => e.Name.LocalName == "ETag")?.Value;
        var cleanedEtag = GenLauncherChecksumValidator.CleanETag(etag);

        var sizeStr = contents.Elements().FirstOrDefault(e => e.Name.LocalName == "Size")?.Value;
        long.TryParse(sizeStr, out var size);

        var relativePath = key;
        if (!string.IsNullOrEmpty(context.FolderPrefix))
        {
            if (key.StartsWith(context.NormalizedFolder, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = key[context.NormalizedFolder.Length..];
            }
            else if (key.StartsWith(context.FolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = key[context.FolderPrefix.Length..].TrimStart('/');
            }
        }

        var downloadUrl = GenLauncherS3Signer.GeneratePresignedGetUrl(
            context.S3Host,
            context.BucketName,
            key,
            context.PublicKey,
            context.SecretKey,
            forceUnsigned: !context.UseAuth);

        return new GenLauncherS3FileEntry
        {
            Key = key,
            RelativePath = relativePath,
            Size = size,
            ETag = cleanedEtag,
            DownloadUrl = downloadUrl,
        };
    }
}
