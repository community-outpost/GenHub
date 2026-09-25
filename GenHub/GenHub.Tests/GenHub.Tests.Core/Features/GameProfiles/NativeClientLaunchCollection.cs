using System.Runtime.InteropServices;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Serialises the tests that launch a real native game client.
/// <para>
/// The engine enforces a single running instance, so two of these in parallel produce a
/// spurious failure: the second launch is refused by the first. That is engine behaviour
/// rather than a test defect, and it has a product consequence — GenHub cannot run two
/// native profiles simultaneously.
/// </para>
/// </summary>
/// <remarks>
/// Collection membership controls scheduling, not test filtering. Every test class that
/// launches the real engine must also declare
/// <c>[Trait(NativeClientFixture.TraitName, NativeClientFixture.TraitValue)]</c>
/// so routine unit runs can exclude it with <c>Category!=NativeEngine</c>.
/// Tests using only stub executables do not need the native-engine trait.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class NativeClientLaunchCollection
{
    /// <summary>The xUnit collection name.</summary>
    public const string Name = "Native client launch";
}
