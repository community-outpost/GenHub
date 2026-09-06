using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Interface for binary .rep replay header parsing.
/// </summary>
public interface IReplayHeaderParser
{
    /// <summary>
    /// Parses replay header metadata from a stream.
    /// </summary>
    /// <param name="stream">The binary stream containing replay data.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing the parsed replay metadata or errors.</returns>
    Task<OperationResult<ReplayMetadata>> ParseHeaderAsync(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses replay header metadata from a file path on disk.
    /// </summary>
    /// <param name="filePath">The absolute path to the replay file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An operation result containing the parsed replay metadata or errors.</returns>
    Task<OperationResult<ReplayMetadata>> ParseHeaderAsync(string filePath, CancellationToken cancellationToken = default);
}
