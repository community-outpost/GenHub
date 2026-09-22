using GenHub.Core.Models.Manifest;
using System.Threading;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Forwarding facade to <see cref="GenHub.Core.Helpers.GameClientEntryDetector"/>
/// for backward compatibility.
/// </summary>
public static class GameClientEntryDetector
{
    /// <inheritdoc cref="GenHub.Core.Helpers.GameClientEntryDetector.DetectEntryPoint"/>
    public static EntryPointResolution DetectEntryPoint(string extractedDirectory, CancellationToken cancellationToken = default)
        => GenHub.Core.Helpers.GameClientEntryDetector.DetectEntryPoint(extractedDirectory, cancellationToken);

    /// <inheritdoc cref="GenHub.Core.Helpers.GameClientEntryDetector.ResolveBundleExecutableAbsolute"/>
    public static string? ResolveBundleExecutableAbsolute(string bundleRoot, CancellationToken cancellationToken = default)
        => GenHub.Core.Helpers.GameClientEntryDetector.ResolveBundleExecutableAbsolute(bundleRoot, cancellationToken);
}
