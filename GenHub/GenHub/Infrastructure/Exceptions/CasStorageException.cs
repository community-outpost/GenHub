using System;

namespace GenHub.Infrastructure.Exceptions
{
    /// <summary>
    /// Represents errors that occur during CAS storage operations.
    /// </summary>
    public class CasStorageException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CasStorageException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public CasStorageException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CasStorageException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public CasStorageException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
