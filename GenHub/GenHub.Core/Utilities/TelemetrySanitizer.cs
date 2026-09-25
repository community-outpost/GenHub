using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Telemetry;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GenHub.Core.Utilities;

/// <summary>
/// Default implementation of <see cref="ITelemetrySanitizer"/> that strips PII, usernames, home directories,
/// wine prefixes, IP addresses, and authorization tokens.
/// </summary>
public partial class TelemetrySanitizer : ITelemetrySanitizer
{
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled)]
    private static partial Regex Ipv4Regex();

    [GeneratedRegex(@"(?i)(?<![0-9a-f:])(?:(?:[0-9a-f]{1,4}:){7}[0-9a-f]{1,4}|(?:[0-9a-f]{1,4}:){1,7}:|:(?::[0-9a-f]{1,4}){1,7}|(?:[0-9a-f]{1,4}:){1,6}:[0-9a-f]{1,4}|(?:[0-9a-f]{1,4}:){1,5}(?::[0-9a-f]{1,4}){1,2}|(?:[0-9a-f]{1,4}:){1,4}(?::[0-9a-f]{1,4}){1,3}|(?:[0-9a-f]{1,4}:){1,3}(?::[0-9a-f]{1,4}){1,4}|(?:[0-9a-f]{1,4}:){1,2}(?::[0-9a-f]{1,4}){1,5}|[0-9a-f]{1,4}:(?:(?::[0-9a-f]{1,4}){1,6})|::)(?![0-9a-f:])", RegexOptions.Compiled)]
    private static partial Regex Ipv6Regex();

    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9_]{20,}", RegexOptions.Compiled)]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex(@"github_pat_[A-Za-z0-9_]{20,}", RegexOptions.Compiled)]
    private static partial Regex GitHubFineGrainedTokenRegex();

    [GeneratedRegex(@"(?i)bearer\s+[a-zA-Z0-9_\-\.\+/=]{20,}", RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"[a-zA-Z]:\\(?:Users|Documents and Settings)\\[^\\]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WindowsUserDirRegex();

    [GeneratedRegex(@"/(?:home|Users)/[^/]+", RegexOptions.Compiled)]
    private static partial Regex UnixUserDirRegex();

    [GeneratedRegex(@"(?:[^\s""]+)?/\.wine(?:-[^\s""]+)?/drive_c", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WinePrefixRegex();

    [GeneratedRegex(@"([?&](?:access_token|api_key|apikey|token|secret|password|client_secret)=)[^&\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecretRegex();

    [GeneratedRegex(@"\b((?:password|secret|apikey|api_key|client_secret)\s*[:=]\s*)[^\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecretRegex();

    private const int MaxSanitizeDepth = 10;
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

        // Mask Wine prefix paths
        result = WinePrefixRegex().Replace(result, TelemetryConstants.WinePrefixMask);

        // Mask exact user profile path if available
        if (!string.IsNullOrEmpty(_userProfilePath) && _userProfilePath.Length > 2)
        {
            result = result.Replace(_userProfilePath, TelemetryConstants.UserDirectoryMask, StringComparison.OrdinalIgnoreCase);
        }

        // Mask generic Windows user directory patterns (e.g. C:\Users\john)
        result = WindowsUserDirRegex().Replace(result, TelemetryConstants.UserDirectoryMask);

        // Mask generic Unix/macOS user directory patterns (e.g. /home/john or /Users/john)
        result = UnixUserDirRegex().Replace(result, TelemetryConstants.UserDirectoryMask);

        // Mask GitHub & authorization tokens
        result = GitHubTokenRegex().Replace(result, TelemetryConstants.SecretTokenMask);
        result = GitHubFineGrainedTokenRegex().Replace(result, TelemetryConstants.SecretTokenMask);
        result = BearerTokenRegex().Replace(result, "Bearer " + TelemetryConstants.SecretTokenMask);

        // Mask query string secrets (e.g. ?access_token=..., &api_key=...)
        result = QuerySecretRegex().Replace(result, "$1" + TelemetryConstants.SecretTokenMask);

        // Mask key-value credentials (e.g. password=..., secret: ...)
        result = KeyValueSecretRegex().Replace(result, "$1" + TelemetryConstants.SecretTokenMask);

        // Mask IP addresses
        result = Ipv4Regex().Replace(result, TelemetryConstants.IpAddressMask);
        result = Ipv6Regex().Replace(result, TelemetryConstants.IpAddressMask);

        // Mask exact username if prominent
        if (!string.IsNullOrEmpty(_userName) && _userName.Length > 2 && !_userName.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            result = Regex.Replace(result, $@"\b{Regex.Escape(_userName)}\b", "<USER>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
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

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { properties };
        var sanitized = new Dictionary<string, object?>(properties.Count);

        foreach (var (key, val) in properties)
        {
            sanitized[key] = SanitizeValue(val, visited, 0);
        }

        return sanitized;
    }

    private object? SanitizeValue(object? value, HashSet<object> visited, int depth)
    {
        if (value == null)
        {
            return null;
        }

        if (depth >= MaxSanitizeDepth)
        {
            return "[MaxDepth]";
        }

        if (value is string strValue)
        {
            return SanitizeString(strValue);
        }

        var valType = value.GetType();

        // For non-primitive reference types, track visited set to prevent cyclic recursion
        if (!valType.IsValueType && value is not (string or System.Type) && !visited.Add(value))
        {
            return "[CircularReference]";
        }

        if (value is IReadOnlyDictionary<string, object?> roDict)
        {
            return SanitizeReadOnlyDictionary(roDict, visited, depth);
        }

        if (value is System.Collections.IDictionary dict)
        {
            return SanitizeDictionary(dict, visited, depth);
        }

        if (valType.IsGenericType && valType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            return SanitizeKeyValuePair(value, valType, visited, depth);
        }

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            return SanitizeEnumerable(enumerable, visited, depth);
        }

        return SanitizeCustomObject(value, valType, visited, depth);
    }

    private Dictionary<string, object?> SanitizeReadOnlyDictionary(
        IReadOnlyDictionary<string, object?> nestedDict,
        HashSet<object> visited,
        int depth)
    {
        var newDict = new Dictionary<string, object?>(nestedDict.Count);
        foreach (var (k, v) in nestedDict)
        {
            newDict[SanitizeString(k) ?? string.Empty] = SanitizeValue(v, visited, depth + 1);
        }

        return newDict;
    }

    private Dictionary<string, object?> SanitizeDictionary(
        System.Collections.IDictionary dict,
        HashSet<object> visited,
        int depth)
    {
        var newDict = new Dictionary<string, object?>(dict.Count);
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            var key = SanitizeString(entry.Key?.ToString()) ?? string.Empty;
            newDict[key] = SanitizeValue(entry.Value, visited, depth + 1);
        }

        return newDict;
    }

    private KeyValuePair<string, object?> SanitizeKeyValuePair(
        object value,
        Type valType,
        HashSet<object> visited,
        int depth)
    {
        var kProp = valType.GetProperty("Key")?.GetValue(value)?.ToString() ?? string.Empty;
        var vProp = valType.GetProperty("Value")?.GetValue(value);
        return new KeyValuePair<string, object?>(SanitizeString(kProp) ?? string.Empty, SanitizeValue(vProp, visited, depth + 1));
    }

    private List<object?> SanitizeEnumerable(
        System.Collections.IEnumerable enumerable,
        HashSet<object> visited,
        int depth)
    {
        var sanitizedList = new List<object?>();
        foreach (var item in enumerable)
        {
            sanitizedList.Add(SanitizeValue(item, visited, depth + 1));
        }

        return sanitizedList;
    }

    private object? SanitizeCustomObject(
        object value,
        Type valType,
        HashSet<object> visited,
        int depth)
    {
        if (valType.IsPrimitive || valType.IsEnum || value is (DateTime or DateTimeOffset or TimeSpan or Guid or decimal or byte[]))
        {
            return value;
        }

        var props = valType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (props.Length == 0 || valType.Namespace == "System")
        {
            return value;
        }

        var dictObj = new Dictionary<string, object?>(props.Length);
        foreach (var prop in props)
        {
            if (prop.CanRead)
            {
                dictObj[prop.Name] = SanitizeValue(prop.GetValue(value), visited, depth + 1);
            }
        }

        return dictObj;
    }
}
