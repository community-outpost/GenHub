using System;

namespace GenHub.Infrastructure.Exceptions;

/// <summary>
/// Represents errors that occur during CAS storage operations.
/// </summary>
/// <param name="message">The error message.</param>
/// <param name="innerException">The inner exception that is the cause of this exception.</param>
public class CasStorageException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
}
