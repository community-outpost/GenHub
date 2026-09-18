using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Parses and transforms cloud mirror download links (Dropbox, OneDrive, Google Drive) into direct download links.
/// Based on GenLauncher DownloadLinkParser.
/// </summary>
public static partial class GenLauncherDownloadLinkParser
{
    private const string OneDriveDownloadEndpoint = "https://onedrive.live.com/download";
    private const string OneDriveViewAspx = "/view.aspx";
    private const string OneDriveDownloadAspx = "/download.aspx";
    private const string OneDriveEmbed = "/embed";
    private const string OneDriveDownload = "/download";

    /// <summary>
    /// Normalizes download links from cloud storage providers to direct download URLs.
    /// </summary>
    /// <param name="link">The raw download link from the manifest.</param>
    /// <returns>A direct download URL.</returns>
    public static string ParseDownloadLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return string.Empty;
        }

        link = link.Trim();

        // 1. Dropbox: dl.dropboxusercontent.com is already direct; dropbox.com needs ?dl=1
        if (link.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("dropboxusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDropboxLink(link);
        }

        // 2. OneDrive
        if (link.Contains("onedrive.live.com", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("1drv.ms", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeOneDriveLink(link);
        }

        // 3. Google Drive
        if (link.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeGoogleDriveLink(link);
        }

        return link;
    }

    private static string NormalizeDropboxLink(string link)
    {
        if (link.Contains("dl.dropboxusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return link;
        }

        if (link.Contains("?dl=0", StringComparison.OrdinalIgnoreCase))
        {
            return link.Replace("?dl=0", "?dl=1", StringComparison.OrdinalIgnoreCase);
        }

        if (link.Contains("&dl=0", StringComparison.OrdinalIgnoreCase))
        {
            return link.Replace("&dl=0", "&dl=1", StringComparison.OrdinalIgnoreCase);
        }

        if (!link.Contains("dl=1", StringComparison.OrdinalIgnoreCase))
        {
            return link.Contains('?') ? link + "&dl=1" : link + "?dl=1";
        }

        return link;
    }

    private static string NormalizeOneDriveLink(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            var queryParams = ParseQueryString(uri.Query);
            var cid = queryParams.GetValueOrDefault("cid");
            var resid = queryParams.GetValueOrDefault("resid") ?? queryParams.GetValueOrDefault("id");
            var authkey = queryParams.GetValueOrDefault("authkey");

            if (!string.IsNullOrEmpty(cid) && !string.IsNullOrEmpty(resid))
            {
                var builder = new UriBuilder(OneDriveDownloadEndpoint)
                {
                    Query = $"cid={Uri.EscapeDataString(cid)}&resid={Uri.EscapeDataString(resid)}" +
                            (!string.IsNullOrEmpty(authkey) ? $"&authkey={Uri.EscapeDataString(authkey)}" : string.Empty),
                };
                return builder.Uri.ToString();
            }

            var path = uri.AbsolutePath;
            if (path.Contains(OneDriveEmbed, StringComparison.OrdinalIgnoreCase))
            {
                var newPath = OneDriveEmbedRegex().Replace(path, OneDriveDownload);
                var builder = new UriBuilder(uri)
                {
                    Path = newPath,
                };
                return builder.Uri.ToString();
            }

            if (path.Contains(OneDriveViewAspx, StringComparison.OrdinalIgnoreCase))
            {
                var newPath = path.Replace(OneDriveViewAspx, OneDriveDownloadAspx, StringComparison.OrdinalIgnoreCase);
                var builder = new UriBuilder(uri)
                {
                    Path = newPath,
                };
                return builder.Uri.ToString();
            }
        }
        else if (link.Contains(OneDriveEmbed, StringComparison.OrdinalIgnoreCase))
        {
            return OneDriveEmbedRegex().Replace(link, OneDriveDownload);
        }
        else if (link.Contains(OneDriveViewAspx, StringComparison.OrdinalIgnoreCase))
        {
            return link.Replace(OneDriveViewAspx, OneDriveDownloadAspx, StringComparison.OrdinalIgnoreCase);
        }

        return link;
    }

    private static string NormalizeGoogleDriveLink(string link)
    {
        // Don't rewrite folder URLs to uc?export=download as they require different handling
        if (link.Contains("/folders/", StringComparison.OrdinalIgnoreCase))
        {
            return link;
        }

        var match = GoogleDrivePathRegex().Match(link);
        if (match.Success)
        {
            return $"https://drive.google.com/uc?export=download&id={match.Groups[1].Value}";
        }

        var idMatch = GoogleDriveQueryRegex().Match(link);
        if (idMatch.Success)
        {
            return $"https://drive.google.com/uc?export=download&id={idMatch.Groups[1].Value}";
        }

        return link;
    }

    [GeneratedRegex(@"/embed\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OneDriveEmbedRegex();

    [GeneratedRegex(@"/file/d/([a-zA-Z0-9_-]+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GoogleDrivePathRegex();

    [GeneratedRegex(@"[?&]id=([a-zA-Z0-9_-]+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GoogleDriveQueryRegex();

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        var trimmed = query.TrimStart('?');
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }
            else if (parts.Length == 1)
            {
                result[Uri.UnescapeDataString(parts[0])] = string.Empty;
            }
        }

        return result;
    }
}
