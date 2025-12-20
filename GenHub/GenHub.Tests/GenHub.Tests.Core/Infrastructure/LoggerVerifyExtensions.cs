using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Infrastructure;

/// <summary>
/// Extensions for verifying that logging occurred using <see cref="Moq.Mock{T}"/>.
/// </summary>
internal static class LoggerVerifyExtensions
{
    /// <summary>
    /// Verifies that an error-level log entry was written at least once.
    /// </summary>
    /// <typeparam name="T">The logger category type.</typeparam>
    /// <param name="mock">The mocked logger.</param>
    public static void VerifyLogErrorCalled<T>(this Mock<ILogger<T>> mock)
    {
        mock.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, __) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}
