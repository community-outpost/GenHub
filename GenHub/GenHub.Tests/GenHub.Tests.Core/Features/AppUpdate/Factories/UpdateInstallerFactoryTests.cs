using FluentAssertions;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using GenHub.Features.AppUpdate.Factories;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.AppUpdate.Factories;

/// <summary>
/// Tests for <see cref="UpdateInstallerFactory"/>.
/// </summary>
public class UpdateInstallerFactoryTests : IDisposable
{
    private readonly ServiceCollection _services;
    private readonly Mock<ILogger<UpdateInstallerFactory>> _mockLogger;
    private readonly Mock<IDownloadService> _mockDownloadService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateInstallerFactoryTests"/> class.
    /// </summary>
    public UpdateInstallerFactoryTests()
    {
        _services = new ServiceCollection();
        _mockLogger = new Mock<ILogger<UpdateInstallerFactory>>();
        _mockDownloadService = new Mock<IDownloadService>();

        // Setup mock download service with successful download
        _mockDownloadService.Setup(x => x.DownloadFileAsync(
                It.IsAny<DownloadConfiguration>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadResult.CreateSuccess("test-path", 1024, TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// Tests that constructor with valid parameters should not throw.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange
        var serviceProvider = _services.BuildServiceProvider();

        // Act & Assert
        var act = () => new UpdateInstallerFactory(serviceProvider, _mockLogger.Object);
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that constructor with null service provider should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_WithNullServiceProvider_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new UpdateInstallerFactory(null!, _mockLogger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    /// <summary>
    /// Tests that constructor with null logger should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        var serviceProvider = _services.BuildServiceProvider();

        // Act & Assert
        var act = () => new UpdateInstallerFactory(serviceProvider, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    /// <summary>
    /// Tests that CreateInstaller with registered platform installer should return installer.
    /// </summary>
    [Fact]
    public void CreateInstaller_WithRegisteredPlatformInstaller_ShouldReturnInstaller()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var testLogger = loggerFactory.CreateLogger<TestPlatformUpdateInstaller>();

        _services.AddSingleton<IPlatformUpdateInstaller>(
            new TestPlatformUpdateInstaller(_mockDownloadService.Object, testLogger));
        var serviceProvider = _services.BuildServiceProvider();
        var factory = new UpdateInstallerFactory(serviceProvider, _mockLogger.Object);

        // Act
        var installer = factory.CreateInstaller();

        // Assert
        installer.Should().NotBeNull();
        installer.Should().BeAssignableTo<IUpdateInstaller>();
        installer.Should().BeAssignableTo<IPlatformUpdateInstaller>();
        installer.Should().BeOfType<TestPlatformUpdateInstaller>();
    }

    /// <summary>
    /// Tests that CreateInstaller with no registered platform installer should throw InvalidOperationException.
    /// </summary>
    [Fact]
    public void CreateInstaller_WithNoRegisteredPlatformInstaller_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var serviceProvider = _services.BuildServiceProvider();
        var factory = new UpdateInstallerFactory(serviceProvider, _mockLogger.Object);

        // Act & Assert
        var act = () => factory.CreateInstaller();
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("No platform-specific update installer registered*");
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Test implementation of IPlatformUpdateInstaller for testing purposes.
    /// </summary>
    public class TestPlatformUpdateInstaller(
        IDownloadService downloadService,
        ILogger<TestPlatformUpdateInstaller> logger)
        : BaseUpdateInstaller(downloadService, logger), IPlatformUpdateInstaller
    {
        /// <inheritdoc/>
        protected override List<string> GetPlatformAssetPatterns()
        {
            return new List<string> { "test", ".zip" };
        }

        /// <inheritdoc/>
        protected override Task<bool> CreateAndLaunchExternalUpdaterAsync(
            string sourceDirectory,
            string targetDirectory,
            IProgress<UpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}