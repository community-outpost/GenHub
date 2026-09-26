using GenHub.Core.Interfaces.Common;
using System.Threading;

namespace GenHub.Common.Services;

/// <summary>
/// In-memory implementation of <see cref="ILinkActivationTracker"/>.
/// </summary>
public sealed class LinkActivationTracker : ILinkActivationTracker
{
    private int _received;

    /// <inheritdoc/>
    public bool HasReceivedLink => Volatile.Read(ref _received) != 0;

    /// <inheritdoc/>
    public void RecordLink() => Volatile.Write(ref _received, 1);
}
