using GenHub.Core.Constants;
using GenHub.Core.Models.Manifest;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Tests for <see cref="InstallationArgumentBindingResolver"/>.
/// </summary>
public class InstallationArgumentBindingResolverTests : IDisposable
{
    private readonly string _packageDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationArgumentBindingResolverTests"/> class.
    /// </summary>
    public InstallationArgumentBindingResolverTests()
    {
        _packageDirectory = Path.Combine(Path.GetTempPath(), $"genhub-bindings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_packageDirectory);
    }

    /// <summary>
    /// A JSON binding fills its argument entry from the delivered file.
    /// </summary>
    [Fact]
    public void ResolveArguments_JsonBinding_FillsEntry()
    {
        WriteFile(Path.Combine("EasyAntiCheat", "Settings.json"), """{"productid": "abc123"}""");
        var step = BoundStep();

        var result = InstallationArgumentBindingResolver.ResolveArguments(step, _packageDirectory);

        Assert.True(result.Success);
        Assert.Equal(["install", "abc123"], result.Data);
        Assert.Equal(string.Empty, step.Arguments![1]);
    }

    /// <summary>
    /// Steps without bindings pass their arguments through untouched.
    /// </summary>
    [Fact]
    public void ResolveArguments_NoBindings_PassesThrough()
    {
        var step = new InstallationStep { Name = "Plain", Arguments = ["install", "literal"] };

        var result = InstallationArgumentBindingResolver.ResolveArguments(step, _packageDirectory);

        Assert.True(result.Success);
        Assert.Equal(["install", "literal"], result.Data);
    }

    /// <summary>
    /// An explicit step key would pin a dynamic step forever, so the combination fails.
    /// </summary>
    [Fact]
    public void ResolveArguments_ExplicitStepKeyWithBindings_Fails()
    {
        var step = BoundStep();
        step.StepKey = "pinned";

        var result = InstallationArgumentBindingResolver.ResolveArguments(step, _packageDirectory);

        Assert.False(result.Success);
        Assert.Contains("step key", result.FirstError);
    }

    /// <summary>
    /// Binding indexes must address a declared argument entry.
    /// </summary>
    /// <param name="index">The bound argument index.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void ResolveArguments_IndexOutOfRange_Fails(int index)
    {
        var step = BoundStep();
        step.ArgumentBindings![0].ArgumentIndex = index;

        var result = InstallationArgumentBindingResolver.ResolveArguments(step, _packageDirectory);

        Assert.False(result.Success);
        Assert.Contains($"#{index}", result.FirstError);
    }

    /// <summary>
    /// Only the JSON source exists today; anything else fails loudly.
    /// </summary>
    [Fact]
    public void ResolveArguments_UnknownSource_Fails()
    {
        var step = BoundStep();
        step.ArgumentBindings![0].Source = "xml";

        var result = InstallationArgumentBindingResolver.ResolveArguments(step, _packageDirectory);

        Assert.False(result.Success);
        Assert.Contains("unsupported binding source", result.FirstError);
    }

    /// <summary>
    /// Binding paths cannot escape the delivered content directory.
    /// </summary>
    [Fact]
    public void ResolveArguments_EscapingPath_Fails()
    {
        var step = BoundStep();
        step.ArgumentBindings![0].RelativePath = "../outside.json";

        var result = InstallationArgumentBindingResolver.ResolveArguments(step, _packageDirectory);

        Assert.False(result.Success);
        Assert.Contains("escapes the content directory", result.FirstError);
    }

    /// <summary>
    /// Missing, unreadable, or valueless binding files fail with the file named.
    /// </summary>
    /// <param name="contents">The file contents, or null to write no file.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"other": "x"}""")]
    [InlineData("""{"productid": ""}""")]
    [InlineData("""{"productid": 42}""")]
    public void ResolveArguments_UnusableFile_Fails(string? contents)
    {
        if (contents is not null)
        {
            WriteFile(Path.Combine("EasyAntiCheat", "Settings.json"), contents);
        }

        var step = BoundStep();

        var result = InstallationArgumentBindingResolver.ResolveArguments(step, _packageDirectory);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Duplicate bindings for the same argument index fail.
    /// </summary>
    [Fact]
    public void ResolveArguments_DuplicateBindingIndex_Fails()
    {
        WriteFile(Path.Combine("EasyAntiCheat", "Settings.json"), """{"productid": "p1", "otherkey": "p2"}""");
        var step = BoundStep();
        step.ArgumentBindings!.Add(new InstallationArgumentBinding
        {
            ArgumentIndex = 1,
            Source = ManifestConstants.InstallationBindingJsonSource,
            RelativePath = "EasyAntiCheat/Settings.json",
            Key = "otherkey",
        });

        var result = InstallationArgumentBindingResolver.ResolveArguments(step, _packageDirectory);

        Assert.False(result.Success);
        Assert.Contains("duplicate bindings for argument #1", result.FirstError);
    }

    /// <summary>
    /// Binding file exceeding the 64KB bound fails.
    /// </summary>
    [Fact]
    public void ResolveArguments_FileExceedsLimit_Fails()
    {
        var oversized = new string('x', (int)ManifestConstants.InstallationBindingMaxFileSizeBytes + 10);
        WriteFile(Path.Combine("EasyAntiCheat", "Settings.json"), oversized);
        var step = BoundStep();

        var result = InstallationArgumentBindingResolver.ResolveArguments(step, _packageDirectory);

        Assert.False(result.Success);
        Assert.Contains("exceeds the", result.FirstError);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_packageDirectory))
        {
            Directory.Delete(_packageDirectory, recursive: true);
        }
    }

    private static InstallationStep BoundStep() => new()
    {
        Name = "Bound",
        Arguments = ["install", string.Empty],
        ArgumentBindings =
        [
            new InstallationArgumentBinding
            {
                ArgumentIndex = 1,
                Source = ManifestConstants.InstallationBindingJsonSource,
                RelativePath = "EasyAntiCheat/Settings.json",
                Key = "productid",
            },
        ],
    };

    private void WriteFile(string relativePath, string contents)
    {
        var fullPath = Path.Combine(_packageDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }
}
