using GenHub.Core.Interfaces.Common;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services;

/// <summary>
/// Sends HTTP requests with SSRF-safe manual redirect validation. The initial URI and every
/// redirect target are validated as public HTTP(S) destinations before connecting, so this
/// helper must be used with clients configured with <c>AllowAutoRedirect = false</c>.
/// </summary>
public static class SsrfSafeHttpHelper
{
    /// <summary>
    /// Sends a request for <paramref name="initialUri"/>, following redirects hop by hop with
    /// per-hop validation.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to send requests. Automatic redirects must be disabled.</param>
    /// <param name="requestFactory">Creates the request for each hop URI.</param>
    /// <param name="initialUri">The absolute URI to request first.</param>
    /// <param name="maxRedirects">The maximum redirect hops to follow.</param>
    /// <param name="urlValidator">Validates each hop before connecting.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The final response (the caller owns disposal) and the URI it was fetched from.</returns>
    /// <exception cref="HttpRequestException">Thrown when a hop is unsafe, a redirect is malformed, or the hop limit is exceeded.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the client followed a redirect automatically.</exception>
    public static async Task<(HttpResponseMessage Response, Uri FinalUri)> SendWithValidatedRedirectsAsync(
        HttpClient httpClient,
        Func<Uri, HttpRequestMessage> requestFactory,
        Uri initialUri,
        int maxRedirects,
        IDownloadUrlValidator urlValidator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(requestFactory);
        ArgumentNullException.ThrowIfNull(initialUri);
        ArgumentNullException.ThrowIfNull(urlValidator);

        var currentUri = initialUri;
        int redirectCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await urlValidator.IsSafeAsync(currentUri, cancellationToken).ConfigureAwait(false))
            {
                throw new HttpRequestException($"Refusing to connect to '{currentUri.Host}': the download target must resolve to a public internet address.");
            }

            using var request = requestFactory(currentUri);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!IsRedirectStatusCode(response.StatusCode))
            {
                EnforceNoAutoRedirect(response, currentUri);
                return (response, currentUri);
            }

            var location = response.Headers.Location;
            response.Dispose();
            currentUri = ResolveRedirectTarget(location, currentUri, maxRedirects, ref redirectCount);
        }
    }

    private static Uri ResolveRedirectTarget(Uri? location, Uri currentUri, int maxRedirects, ref int redirectCount)
    {
        if (++redirectCount > maxRedirects)
        {
            throw new HttpRequestException($"Too many redirects (exceeded {maxRedirects}).");
        }

        if (location == null)
        {
            throw new HttpRequestException("Redirect response is missing the Location header.");
        }

        var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
        if (nextUri.Scheme != Uri.UriSchemeHttp && nextUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new HttpRequestException($"Refusing to follow redirect to '{nextUri.Scheme}' URI: only HTTP and HTTPS targets are allowed.");
        }

        return nextUri;
    }

    private static void EnforceNoAutoRedirect(HttpResponseMessage response, Uri requestedUri)
    {
        if (response.RequestMessage?.RequestUri != null && response.RequestMessage.RequestUri != requestedUri)
        {
            throw new InvalidOperationException("HttpClient followed redirects automatically. The client must be configured with AllowAutoRedirect = false so that each redirect hop is validated individually before connecting.");
        }
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
                      HttpStatusCode.Found or
                      HttpStatusCode.SeeOther or
                      HttpStatusCode.TemporaryRedirect or
                      HttpStatusCode.PermanentRedirect;
}
