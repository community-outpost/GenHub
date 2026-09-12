using System;

namespace GenHub.Core.Exceptions;

/// <summary>
/// Exception thrown when an archive entry expands past the budget allowed for it, which means the
/// size declared in the archive headers understated the real decompressed size.
/// </summary>
public class ArchiveExpansionLimitExceededException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveExpansionLimitExceededException"/> class.
    /// </summary>
    public ArchiveExpansionLimitExceededException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveExpansionLimitExceededException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ArchiveExpansionLimitExceededException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveExpansionLimitExceededException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public ArchiveExpansionLimitExceededException(string message, Exception? inner)
        : base(message, inner)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveExpansionLimitExceededException"/> class
    /// for a named entry that exceeded a byte budget.
    /// </summary>
    /// <param name="entryName">The archive-relative name of the offending entry.</param>
    /// <param name="limitBytes">The number of bytes the entry was allowed to expand to.</param>
    public ArchiveExpansionLimitExceededException(string entryName, long limitBytes)
        : this($"Archive entry '{entryName}' expanded past the allowed {limitBytes} bytes (potential zip bomb).", entryName, limitBytes)
    {
    }

    private ArchiveExpansionLimitExceededException(string message, string entryName, long limitBytes)
        : base(message)
    {
        EntryName = entryName;
        LimitBytes = limitBytes;
    }

    /// <summary>
    /// Gets the archive-relative name of the offending entry.
    /// </summary>
    public string EntryName { get; } = string.Empty;

    /// <summary>
    /// Gets the number of bytes the entry was allowed to expand to.
    /// </summary>
    public long LimitBytes { get; }

    /// <summary>
    /// Creates an exception for an entry refused because the archive-wide expansion budget was
    /// already spent, so no byte of it was ever read.
    /// </summary>
    /// <param name="entryName">The archive-relative name of the refused entry.</param>
    /// <returns>An exception describing the spent budget.</returns>
    public static ArchiveExpansionLimitExceededException ForSpentBudget(string entryName) =>
        new(
            $"Archive entry '{entryName}' was refused because the archive-wide expansion budget was already spent (potential zip bomb).",
            entryName,
            0);
}
