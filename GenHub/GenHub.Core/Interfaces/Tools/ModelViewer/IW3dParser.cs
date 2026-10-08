using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ModelViewer;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModelViewer;

/// <summary>
/// Parses Westwood 3D (.w3d) model files into portable model data.
/// </summary>
public interface IW3dParser
{
    /// <summary>
    /// Parses model bytes.
    /// </summary>
    /// <param name="data">The file bytes.</param>
    /// <param name="sourceName">The optional source name used in error messages.</param>
    /// <returns>The parsed model, or a failure describing the problem.</returns>
    OperationResult<W3dModel> Parse(byte[] data, string? sourceName = null);

    /// <summary>
    /// Parses a model file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed model, or a failure describing the problem.</returns>
    Task<OperationResult<W3dModel>> ParseFileAsync(string path, CancellationToken cancellationToken = default);
}
