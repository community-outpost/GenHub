using GenHub.Core.Constants;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Infrastructure.Services;

/// <summary>
/// Best-effort probe for remote file sizes using HEAD requests with manual redirect handling.
/// </summary>
/// <remarks>
/// Follows redirects hop by hop (validating every hop) so probes work with HTTP clients
/// configured with AllowAutoRedirect = false, and rejects descriptor documents and
/// non-payload content types so a manifest is never mistaken for a download payload.
/// </remarks>
public static class RemoteFileSizeProbe
{
    /// <summary>
    /// Probes the Content-Length of a remote URL with HEAD requests, following redirects.
    /// </summary>
    /// <param name="client">The HTTP client used for the probe.</param>
    /// <param name="url">The URL to probe.</param>
    /// <param name="timeout">The probe timeout applied across all redirect hops.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The probed size in bytes, or null when unknown or not a payload.</returns>
    public static async Task<long?> TryProbeSizeAsync(
        HttpClient client,
        string? url,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!ImageCacheService.IsSafeRemoteUrl(url, out var uri) || IsDescriptorPath(uri.LocalPath))
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var currentUri = uri;
            for (var hop = 0; hop <= ContentConstants.MaxSizeProbeRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, currentUri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (IsRedirectStatusCode(response.StatusCode))
                {
                    var nextUri = ResolveRedirectUri(currentUri, response.Headers.Location);
                    if (nextUri == null)
                    {
                        return null;
                    }

                    currentUri = nextUri;
                    continue;
                }

                return ExtractPayloadLength(response);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Probe timeout; size stays unknown
        }
        catch (HttpRequestException)
        {
            // Unreachable host or rejected connection; size stays unknown
        }
        catch (InvalidOperationException)
        {
            // Misconfigured request; size stays unknown
        }
        catch (UriFormatException)
        {
            // Malformed redirect target; size stays unknown
        }

        return null;
    }

    private static Uri? ResolveRedirectUri(Uri currentUri, Uri? location)
    {
        if (location == null)
        {
            return null;
        }

        var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
        if (!ImageCacheService.IsSafeRemoteUrl(nextUri.AbsoluteUri, out var validated) || IsDescriptorPath(validated.LocalPath))
        {
            return null;
        }

        return validated;
    }

    private static long? ExtractPayloadLength(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode ||
            !response.Content.Headers.ContentLength.HasValue ||
            response.Content.Headers.ContentLength.Value <= 0)
        {
            return null;
        }

        if (IsNonPayloadMediaType(response.Content.Headers.ContentType?.MediaType))
        {
            return null;
        }

        return response.Content.Headers.ContentLength.Value;
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Found or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static bool IsDescriptorPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return ContentConstants.SizeProbeDescriptorExtensions.Any(extension =>
            path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNonPayloadMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return false;
        }

        return mediaType.StartsWith(ContentConstants.SizeProbeTextMediaTypePrefix, StringComparison.OrdinalIgnoreCase) ||
            ContentConstants.SizeProbeDescriptorMediaTypes.Any(candidate =>
                mediaType.Equals(candidate, StringComparison.OrdinalIgnoreCase)) ||
            ContentConstants.SizeProbeDescriptorMediaTypeSuffixes.Any(suffix =>
                mediaType.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
