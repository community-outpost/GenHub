using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Implements AWS Signature Version 4 signing for GenLauncher S3 and MinIO REST endpoints.
/// Supports both query parameter (presigned URL) and header authentication.
/// </summary>
public static class GenLauncherS3Signer
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private const string Service = "s3";
    private const string SignedHeaders = "host";
    private const string UnsignedPayload = "UNSIGNED-PAYLOAD";

    /// <summary>
    /// Checks whether the specified host is an InSave MinIO host used by GenLauncher.
    /// </summary>
    /// <param name="s3Host">The S3 host string.</param>
    /// <returns>True if the host matches gen.insave.ovh or similar; otherwise false.</returns>
    public static bool IsInSaveHost(string? s3Host)
    {
        if (string.IsNullOrWhiteSpace(s3Host))
        {
            return false;
        }

        var (scheme, hostHeader, _) = NormalizeHostAndPath(s3Host);
        var colonIdx = hostHeader.IndexOf(':');
        var hostOnly = colonIdx >= 0 ? hostHeader[..colonIdx] : hostHeader;

        return hostOnly.Equals("gen.insave.ovh", StringComparison.OrdinalIgnoreCase) ||
               hostOnly.EndsWith(".insave.ovh", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes the S3 host string, extracting the HTTP scheme, host header (with port if specified), and any path prefix.
    /// </summary>
    /// <param name="rawHost">The raw host string or URL.</param>
    /// <returns>A tuple of (Scheme, HostHeader, PathPrefix).</returns>
    public static (string Scheme, string HostHeader, string? PathPrefix) NormalizeHostAndPath(string rawHost)
    {
        if (string.IsNullOrWhiteSpace(rawHost))
        {
            return ("https", string.Empty, null);
        }

        var cleaned = rawHost.Trim();
        string scheme;
        if (cleaned.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "https";
            cleaned = cleaned["https://".Length..];
        }
        else if (cleaned.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "http";
            cleaned = cleaned["http://".Length..];
        }
        else if (cleaned.Contains(":9000", StringComparison.OrdinalIgnoreCase) ||
                 cleaned.Contains(":8000", StringComparison.OrdinalIgnoreCase) ||
                 cleaned.Contains("gen.insave.ovh", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "http";
        }
        else
        {
            scheme = "https";
        }

        cleaned = cleaned.TrimEnd('/');
        var slashIdx = cleaned.IndexOf('/');
        string hostHeader;
        string? pathPrefix = null;

        if (slashIdx >= 0)
        {
            hostHeader = cleaned[..slashIdx];
            var subPath = cleaned[(slashIdx + 1)..].Trim('/');
            if (!string.IsNullOrEmpty(subPath))
            {
                pathPrefix = subPath;
            }
        }
        else
        {
            hostHeader = cleaned;
        }

        return (scheme, hostHeader, pathPrefix);
    }

    /// <summary>
    /// Infers the AWS / S3 region from the host header if possible, falling back to defaultRegion.
    /// </summary>
    /// <param name="hostHeader">The host header.</param>
    /// <param name="defaultRegion">The fallback default region.</param>
    /// <returns>The inferred region string.</returns>
    public static string InferRegion(string hostHeader, string defaultRegion = GenLauncherConstants.DefaultS3Region)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            return defaultRegion;
        }

        var colonIdx = hostHeader.IndexOf(':');
        var host = colonIdx >= 0 ? hostHeader[..colonIdx] : hostHeader;

        if (host.EndsWith(".wasabisys.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = host.Split('.');
            if (parts.Length >= 4 && parts[0].Equals("s3", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(parts[1]))
            {
                return parts[1];
            }
        }

        return defaultRegion;
    }

    /// <summary>
    /// Generates a presigned S3 GET URL using AWS Signature Version 4.
    /// If no credentials are provided and the host is not an InSave host, returns an unsigned standard URL.
    /// </summary>
    /// <param name="s3Host">The S3 host name or URL (e.g. "gen.insave.ovh:9000").</param>
    /// <param name="bucket">The S3 bucket name.</param>
    /// <param name="objectKey">The object key or path within the bucket (may be empty for bucket-level listing).</param>
    /// <param name="publicKey">Explicit AWS access key ID, or null to check default.</param>
    /// <param name="secretKey">Explicit AWS secret access key, or null to check default.</param>
    /// <param name="extraQueryParams">Optional additional query parameters such as prefix or marker.</param>
    /// <param name="expiresInSeconds">Presigned URL validity duration in seconds (defaults to 24 hours).</param>
    /// <param name="forceUnsigned">If true, forces generation of an unsigned URL regardless of credentials.</param>
    /// <param name="region">AWS region string (defaults to us-east-1).</param>
    /// <returns>The presigned (or unsigned) GET URL string.</returns>
    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Handles scheme formatting for S3 endpoints")]
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Convenience method allowing callers to configure S3 signing parameters")]
    public static string GeneratePresignedGetUrl(
        string s3Host,
        string bucket,
        string? objectKey,
        string? publicKey = null,
        string? secretKey = null,
        IDictionary<string, string>? extraQueryParams = null,
        int expiresInSeconds = GenLauncherConstants.DefaultS3PresignedUrlExpirySeconds,
        bool forceUnsigned = false,
        string region = GenLauncherConstants.DefaultS3Region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(s3Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);

        var (scheme, hostHeader, pathPrefix) = NormalizeHostAndPath(s3Host);
        if (string.Equals(region, GenLauncherConstants.DefaultS3Region, StringComparison.OrdinalIgnoreCase))
        {
            region = InferRegion(hostHeader, region);
        }

        var (shouldSign, resolvedPub, resolvedSec) = forceUnsigned
            ? (false, string.Empty, string.Empty)
            : ResolveCredentials(s3Host, publicKey, secretKey);

        // Build canonical URI
        var pathSegments = new List<string>();
        if (!string.IsNullOrWhiteSpace(pathPrefix))
        {
            pathSegments.AddRange(pathPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        }

        pathSegments.Add(Uri.EscapeDataString(bucket));

        if (!string.IsNullOrWhiteSpace(objectKey))
        {
            var trimmedKey = objectKey.TrimStart('/');
            var segments = trimmedKey.Split('/', StringSplitOptions.None);
            pathSegments.AddRange(segments.Select(Uri.EscapeDataString));
        }

        var canonicalUri = "/" + string.Join("/", pathSegments);

        if (!shouldSign)
        {
            var baseUrl = $"{scheme}://{hostHeader}{canonicalUri}";
            if (extraQueryParams == null || extraQueryParams.Count == 0)
            {
                return baseUrl;
            }

            var queryString = string.Join("&", extraQueryParams.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            return $"{baseUrl}?{queryString}";
        }

        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var credentialScope = $"{dateStamp}/{region}/{Service}/aws4_request";

        var queryParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = Algorithm,
            ["X-Amz-Credential"] = $"{resolvedPub}/{credentialScope}",
            ["X-Amz-Date"] = amzDate,
            ["X-Amz-Expires"] = expiresInSeconds.ToString(CultureInfo.InvariantCulture),
            ["X-Amz-SignedHeaders"] = SignedHeaders,
        };

        if (extraQueryParams != null)
        {
            foreach (var (key, value) in extraQueryParams)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    queryParams[key] = value;
                }
            }
        }

        var canonicalQueryString = string.Join("&", queryParams.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        var canonicalHeaders = $"host:{hostHeader}\n";
        var canonicalRequest = $"GET\n{canonicalUri}\n{canonicalQueryString}\n{canonicalHeaders}\n{SignedHeaders}\n{UnsignedPayload}";

        var canonicalRequestHash = ComputeSha256Hex(canonicalRequest);
        var stringToSign = $"{Algorithm}\n{amzDate}\n{credentialScope}\n{canonicalRequestHash}";

        var signingKey = DeriveSigningKey(resolvedSec, dateStamp, region, Service);
        var signature = ComputeHmacSha256Hex(signingKey, stringToSign);

        return $"{scheme}://{hostHeader}{canonicalUri}?{canonicalQueryString}&X-Amz-Signature={signature}";
    }

    /// <summary>
    /// Resolves credentials for an S3 host link, falling back to GenLauncher's default InSave credentials when applicable.
    /// </summary>
    /// <param name="s3Host">The S3 host link or endpoint.</param>
    /// <param name="publicKey">Explicit public key from manifest, if any.</param>
    /// <param name="secretKey">Explicit secret key from manifest, if any.</param>
    /// <returns>A tuple indicating whether signing should be applied along with the resolved keys.</returns>
    public static (bool ShouldSign, string PublicKey, string SecretKey) ResolveCredentials(
        string s3Host,
        string? publicKey,
        string? secretKey)
    {
        if (!string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(secretKey))
        {
            return (true, publicKey, secretKey);
        }

        if (IsInSaveHost(s3Host))
        {
            return (true, GenLauncherConstants.DefaultGenInsavePublicKey, GenLauncherConstants.DefaultGenInsaveSecretKey);
        }

        return (false, string.Empty, string.Empty);
    }

    private static byte[] DeriveSigningKey(string key, string dateStamp, string regionName, string serviceName)
    {
        var kSecret = Encoding.UTF8.GetBytes("AWS4" + key);
        var kDate = ComputeHmacSha256Bytes(kSecret, dateStamp);
        var kRegion = ComputeHmacSha256Bytes(kDate, regionName);
        var kService = ComputeHmacSha256Bytes(kRegion, serviceName);
        return ComputeHmacSha256Bytes(kService, "aws4_request");
    }

    private static byte[] ComputeHmacSha256Bytes(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string ComputeHmacSha256Hex(byte[] key, string data)
    {
        var hash = ComputeHmacSha256Bytes(key, data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
