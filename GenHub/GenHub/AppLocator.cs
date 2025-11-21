using System;

namespace GenHub;

/// <summary>
/// The AppLocator is needed to pass the service provider to the avalonia app without breaking the avalonia designer.
/// </summary>
/// <remarks>
/// There might be a more elegant solution, but this works for now - NH.
/// </remarks>
public static class AppLocator
{
    /// <summary>
    /// Gets or sets the service provider for dependency injection.
    /// </summary>
    public static IServiceProvider? Services { get; set; }

    /// <summary>
    /// Gets a service of type <typeparamref name="T"/> from the service provider, or returns <c>null</c> if not found.
    /// </summary>
    /// <typeparam name="T">The type of service to retrieve.</typeparam>
    /// <returns>
    /// An instance of <typeparamref name="T"/> if available; otherwise, <c>null</c>.
    /// </returns>
    public static T? GetServiceOrDefault<T>()
        where T : class
    {
        return Services?.GetService(typeof(T)) as T;
    }
}