using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Content.Providers;

/// <summary>
/// Tests for the platform data on <see cref="ProviderDefinition"/>.
/// </summary>
public class ProviderDefinitionPlatformTests : IDisposable
{
    private readonly string _bundledDirectory = Path.Combine(Path.GetTempPath(), "GenHub.Tests", Guid.NewGuid().ToString());
    private readonly string _userDirectory = Path.Combine(Path.GetTempPath(), "GenHub.Tests", Guid.NewGuid().ToString());

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderDefinitionPlatformTests"/> class.
    /// </summary>
    public ProviderDefinitionPlatformTests()
    {
        Directory.CreateDirectory(_bundledDirectory);
    }

    /// <summary>
    /// A definition that declares no platforms supports every host.
    /// </summary>
    /// <param name="platform">The host platform to test.</param>
    [Theory]
    [InlineData(ExecutablePlatform.Windows)]
    [InlineData(ExecutablePlatform.MacOS)]
    [InlineData(ExecutablePlatform.Linux)]
    public void SupportsPlatform_WithNoDeclaredPlatforms_ReturnsTrue(ExecutablePlatform platform)
    {
        var definition = new ProviderDefinition();

        Assert.True(definition.SupportsPlatform(platform));
    }

    /// <summary>
    /// A Windows-only definition supports Windows and nothing else.
    /// </summary>
    /// <param name="platform">The host platform to test.</param>
    /// <param name="expected">Whether the platform is supported.</param>
    [Theory]
    [InlineData(ExecutablePlatform.Windows, true)]
    [InlineData(ExecutablePlatform.MacOS, false)]
    [InlineData(ExecutablePlatform.Linux, false)]
    [InlineData(ExecutablePlatform.Unknown, false)]
    public void SupportsPlatform_WithWindowsOnly_MatchesOnlyWindows(ExecutablePlatform platform, bool expected)
    {
        var definition = new ProviderDefinition { SupportedPlatforms = [ExecutablePlatform.Windows] };

        Assert.Equal(expected, definition.SupportsPlatform(platform));
    }

    /// <summary>
    /// The loader reads <c>supportedPlatforms</c> from provider JSON.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task LoadProvidersAsync_ParsesSupportedPlatformsAsync()
    {
        const string json = """
            {
                "providerId": "platform-test",
                "publisherType": "platform-test",
                "supportedPlatforms": [ "Windows", "linux" ]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_bundledDirectory, "platform-test.provider.json"), json);
        var loader = new ProviderDefinitionLoader(NullLogger<ProviderDefinitionLoader>.Instance, _bundledDirectory, _userDirectory);

        await loader.LoadProvidersAsync();
        var definition = loader.GetProvider("platform-test");

        Assert.NotNull(definition);
        Assert.Equal([ExecutablePlatform.Windows, ExecutablePlatform.Linux], definition.SupportedPlatforms);
    }

    /// <summary>
    /// The shipped Generals Online definition declares Windows as its only platform, because
    /// Easy Anti-Cheat has no macOS or Linux support.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task BundledGeneralsOnlineDefinition_IsWindowsOnlyAsync()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Providers");
        var loader = new ProviderDefinitionLoader(NullLogger<ProviderDefinitionLoader>.Instance, bundled, _userDirectory);

        await loader.LoadProvidersAsync();
        var definition = loader.GetProvider(GeneralsOnlineConstants.PublisherType);

        Assert.NotNull(definition);
        Assert.Equal([ExecutablePlatform.Windows], definition.SupportedPlatforms);
        Assert.False(definition.SupportsPlatform(ExecutablePlatform.MacOS));
        Assert.False(definition.SupportsPlatform(ExecutablePlatform.Linux));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_bundledDirectory))
        {
            Directory.Delete(_bundledDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
