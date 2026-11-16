using GenHub.Core.Models.Results;
using System;
using Xunit;

namespace GenHub.Tests.Core.Models.Results;

/// <summary>
/// Covers compatibility of profile-result metadata across dependency-validation callers.
/// </summary>
public class ProfileOperationResultTests
{
    /// <summary>
    /// Adding the validated profile ID must not reinterpret an existing positional error code.
    /// </summary>
    [Fact]
    public void CreateMissingDependencies_PositionalErrorCode_PreservesExistingContract()
    {
        var elapsed = TimeSpan.FromMilliseconds(42);
        var result = ProfileOperationResult<bool>.CreateMissingDependencies("Missing item", [], "dependency_missing", elapsed: elapsed);

        Assert.False(result.Success);
        Assert.Equal("dependency_missing", result.ErrorCode);
        Assert.Equal(elapsed, result.Elapsed);
        Assert.Null(result.MissingDependenciesProfileId);
    }
}
