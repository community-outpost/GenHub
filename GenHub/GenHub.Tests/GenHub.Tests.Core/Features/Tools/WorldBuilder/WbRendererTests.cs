// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Features.Tools.WorldBuilder.Rendering;
using System.Numerics;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the GL capability gate in <see cref="WbRenderer"/>. Full frame
/// tests need a real GL context and stay opt-in; the version gate is pure.
/// </summary>
public sealed class WbRendererTests
{
    /// <summary>
    /// Verifies desktop GL version requirements.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <param name="expected">Whether the context is usable.</param>
    [Theory]
    [InlineData("4.6.0", true)]
    [InlineData("3.3.0", true)]
    [InlineData("4.1 ATI-4.1.0", true)]
    [InlineData("3.2.0", false)]
    [InlineData("2.1.0", false)]
    public void CheckVersion_Desktop_EnforcesMinimum(string version, bool expected)
    {
        Assert.Equal(expected, WbRenderer.CheckVersion(version, out var error));
        Assert.Equal(expected, string.IsNullOrEmpty(error));
    }

    /// <summary>
    /// Verifies OpenGL ES version requirements.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <param name="expected">Whether the context is usable.</param>
    [Theory]
    [InlineData("OpenGL ES 3.2", true)]
    [InlineData("OpenGL ES 3.0", true)]
    [InlineData("OpenGL ES 2.0", false)]
    public void CheckVersion_Embedded_EnforcesMinimum(string version, bool expected)
    {
        Assert.Equal(expected, WbRenderer.CheckVersion(version, out _));
    }

    /// <summary>
    /// Verifies missing version strings fail closed.
    /// </summary>
    [Fact]
    public void CheckVersion_Missing_Fails()
    {
        Assert.False(WbRenderer.CheckVersion(null, out var error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.False(WbRenderer.CheckVersion("garbage", out _));
    }

    /// <summary>
    /// Verifies the GL upload bytes reproduce the row-vector transform: GL
    /// reads the sequence as column-major, so it must equal the matrix in row
    /// order, or every viewport renders transposed.
    /// </summary>
    [Fact]
    public void ToGlMatrix_RowOrder_ReproducesRowVectorTransform()
    {
        var matrix = new Matrix4x4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);

        var bytes = WbRenderer.ToGlMatrix(matrix);

        Assert.Equal([1f, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], bytes);
        var vector = new Vector4(17, 18, 19, 20);
        var expected = Vector4.Transform(vector, matrix);
        var actual = MultiplyColumnMajor(bytes, vector);
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
        Assert.Equal(expected.W, actual.W, 3);
    }

    /// <summary>
    /// Verifies the grid mesh covers the bounds with interior lines.
    /// </summary>
    [Fact]
    public void GridMesh_Build_CoversBounds()
    {
        var vertices = WbGridMesh.Build(0, 0, 100, 100, 50, 0.5f);

        Assert.True(vertices.Length % 3 == 0);
        Assert.True(vertices.Length > 0);
        Assert.Contains(0.0f, vertices);
        Assert.Contains(100.0f, vertices);
        Assert.Contains(50.0f, vertices);
        Assert.Contains(0.5f, vertices);
    }

    private static Vector4 MultiplyColumnMajor(float[] bytes, Vector4 vector)
    {
        return new Vector4(
            (bytes[0] * vector.X) + (bytes[4] * vector.Y) + (bytes[8] * vector.Z) + (bytes[12] * vector.W),
            (bytes[1] * vector.X) + (bytes[5] * vector.Y) + (bytes[9] * vector.Z) + (bytes[13] * vector.W),
            (bytes[2] * vector.X) + (bytes[6] * vector.Y) + (bytes[10] * vector.Z) + (bytes[14] * vector.W),
            (bytes[3] * vector.X) + (bytes[7] * vector.Y) + (bytes[11] * vector.Z) + (bytes[15] * vector.W));
    }
}
