using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GameReplays;

/// <summary>
/// HTTP client interface for GameReplays API and web scraping.
/// Handles cookie management and HTTP requests.
/// </summary>
public interface IGameReplaysHttpClient
{
    /// <summary>
    /// Gets HTML content from a URL.
    /// </summary>
    /// <param name="url">The URL to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing HTML content.</returns>
    Task<OperationResult<string>> GetHtmlAsync(
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts form data to a URL.
    /// </summary>
    /// <param name="url">The URL to post to.</param>
    /// <param name="formData">The form data to post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing response content.</returns>
    Task<OperationResult<string>> PostFormAsync(
        string url,
        Dictionary<string, string> formData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets JSON content from an API endpoint.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="url">The API endpoint URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing deserialized data.</returns>
    Task<OperationResult<T>> GetJsonAsync<T>(
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts JSON data to an API endpoint.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="url">The API endpoint URL.</param>
    /// <param name="data">The data to post.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing deserialized response.</returns>
    Task<OperationResult<TResponse>> PostJsonAsync<TRequest, TResponse>(
        string url,
        TRequest data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets authentication cookie for requests.
    /// </summary>
    /// <param name="cookieValue">The cookie value.</param>
    void SetAuthCookie(string cookieValue);

    /// <summary>
    /// Clears authentication cookie.
    /// </summary>
    void ClearAuthCookie();

    /// <summary>
    /// Gets the current authentication cookie value.
    /// </summary>
    /// <returns>The cookie value or null if not set.</returns>
    string? GetAuthCookie();
}
