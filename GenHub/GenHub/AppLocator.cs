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
    /// Gets or sets service provider for dependency injection.
    /// </summary>
    public static IServiceProvider? Services { get; set; }
}