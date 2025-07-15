using GenHub.Core.Models.Validation;
using Xunit;

namespace GenHub.Tests.Features.Validation;

/// <summary>
/// Unit tests for <see cref="ValidationProgress"/>.
/// </summary>
public class ValidationProgressTests
{
    /// <summary>
    /// Verifies that the constructor sets properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var progress = new ValidationProgress(5, 10, "file.txt");
        Assert.Equal(5, progress.Processed);
        Assert.Equal(10, progress.Total);
        Assert.Equal("file.txt", progress.CurrentFile);
    }
}
