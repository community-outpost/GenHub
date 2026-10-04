using System;
using System.Collections.Generic;
using System.Net.Http;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Base class for Steam Workshop test fixtures that track and dispose HTTP clients.
/// </summary>
public abstract class SteamWorkshopHttpTestBase : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the list of tracked HTTP clients.
    /// </summary>
    protected ICollection<HttpClient> TrackedHttpClients => _httpClients;

    /// <summary>
    /// Creates a mock HTTP client factory whose created clients are tracked and disposed with the test fixture.
    /// </summary>
    /// <param name="responder">The responder delegate generating responses for requests.</param>
    /// <returns>The mock HTTP client factory.</returns>
    protected IHttpClientFactory CreateHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return SteamWorkshopTestBuilders.CreateHttpClientFactory(responder, _httpClients);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    /// <param name="disposing">True when disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var client in _httpClients)
            {
                client.Dispose();
            }

            _httpClients.Clear();
        }
    }
}
