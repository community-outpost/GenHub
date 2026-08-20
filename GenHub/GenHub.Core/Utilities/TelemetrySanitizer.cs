using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Telemetry;

namespace GenHub.Core.Utilities;

/// <summary>
/// Default implementation of <see cref="ITelemetrySanitizer"/> that strips PII, usernames, home directories,
/// wine prefixes, IP addresses, and authorization tokens.
/// </summary>
public partial class TelemetrySanitizer : ITelemetrySanitizer
{
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"\b(?:[0-9a-fA-F]{1,4}:){2,7}[0-9a-fA-F]{1,4}\b", RegexOptions.Compiled)]
    private static partial Regex Ipv6Regex();

    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9_]{20,}", RegexOptions.Compiled)]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"github_pat_[A-Za-z0-9_]{20,}", RegexOptions.Compiled)]
    private static partial Regex GitHubFineGrainedTokenRegex();

    [GeneratedRegex(@"(?i)bearer\s+[a-zA-Z0-9_\-\.]{20,}", RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"[a-zA-Z]:\\(?:Users|Documents and Settings)\\[^\\]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WindowsUserDirRegex();

    [GeneratedRegex(@"/(?:home|Users)/[^/]+", RegexOptions.Compiled)]
    private static partial Regex UnixUserDirRegex();

    [GeneratedRegex(@"/[^\s""]+/\.wine(?:-[^\s""]+)?/drive_c", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WinePrefixRegex();

    private readonly string? _userProfilePath;
    private readonly string? _userName;

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetrySanitizer"/> class.
    /// </summary>
    public TelemetrySanitizer()
    {
        try
        {
            _userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _userName = Environment.UserName;
        }
        catch
        {
            _userProfilePath = null;
            _userName = null;
        }
    }

    /// <inheritdoc/>
    public string SanitizeString(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var result = input;

        // Mask exact user profile path if available
        if (!string.IsNullOrEmpty(_userProfilePath) && _userProfilePath.Length > 2)
        {
            result = result.Replace(_userProfilePath, TelemetryConstants.UserDirectoryMask, StringComparison.OrdinalIgnoreCase);
        }

        // Mask generic Windows user directory patterns (e.g. C:\Users\john)
        result = WindowsUserDirRegex().Replace(result, TelemetryConstants.UserDirectoryMask);

        // Mask generic Unix/macOS user directory patterns (e.g. /home/john or /Users/john)
        result = UnixUserDirRegex().Replace(result, TelemetryConstants.UserDirectoryMask);

        // Mask Wine prefix paths
        result = WinePrefixRegex().Replace(result, TelemetryConstants.WinePrefixMask);

        // Mask GitHub & authorization tokens
        result = GitHubTokenRegex().Replace(result, TelemetryConstants.SecretTokenMask);
        result = GitHubFineGrainedTokenRegex().Replace(result, TelemetryConstants.SecretTokenMask);
        result = BearerTokenRegex().Replace(result, "Bearer " + TelemetryConstants.SecretTokenMask);

        // Mask IP addresses
        result = Ipv4Regex().Replace(result, TelemetryConstants.IpAddressMask);
        result = Ipv6Regex().Replace(result, TelemetryConstants.IpAddressMask);

        // Mask exact username if prominent
        if (!string.IsNullOrEmpty(_userName) && _userName.Length > 2 && !_userName.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            result = Regex.Replace(result, $@"\b{Regex.Escape(_userName)}\b", "<USER>", RegexOptions.IgnoreCase);
        }

        return result;
    }

    /// <inheritdoc/>
    public string SanitizeStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
        {
            return string.Empty;
        }

        return SanitizeString(stackTrace);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> SanitizeProperties(IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties == null || properties.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var sanitized = new Dictionary<string, object?>(properties.Count);

        foreach (var (key, val) in properties)
        {
            sanitized[key] = SanitizeValue(val);
        }

        return sanitized;
    }

    private object? SanitizeValue(object? value)
    {
        if (value == null)
        {
            return null;
        }

        if (value is string strValue)
        {
            return SanitizeString(strValue);
        }

        if (value is IReadOnlyDictionary<string, object?> nestedDict)
        {
            return SanitizeProperties(nestedDict);
        }

        if (value is IDictionary<string, object?> dict)
        {
            var newDict = new Dictionary<string, object?>(dict.Count);
            foreach (var kvp in dict)
            {
                newDict[kvp.Key] = SanitizeValue(kvp.Value);
            }

            return newDict;
        }

        if (value is IEnumerable<string> stringList)
        {
            var sanitizedList = new List<string>();
            foreach (var item in stringList)
            {
                sanitizedList.Add(SanitizeString(item));
            }

            return sanitizedList;
        }

        return value;
    }
}
