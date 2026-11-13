using System;

namespace GenHub.Tests.Shared;

/// <summary>
/// Constants for the disposable test environment shared by the test projects.
/// </summary>
internal static class TestEnvironmentConstants
{
    /// <summary>
    /// The number of times cleanup tries to delete the test tree before leaving it in place.
    /// </summary>
    internal const int DeleteAttempts = 5;

    /// <summary>
    /// The base delay between delete attempts. Each retry waits this delay times the attempt number.
    /// </summary>
    internal static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(50);
}
