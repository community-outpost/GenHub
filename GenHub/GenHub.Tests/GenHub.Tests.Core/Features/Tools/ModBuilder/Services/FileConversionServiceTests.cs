using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="FileConversionService"/>.
/// </summary>
public sealed class FileConversionServiceTests : IDisposable
{
    private readonly Mock<IImageConversionService> _mockImageService;
    private readonly Mock<IStringTableConversionService> _mockStringTableService;
    private readonly Mock<ITextProcessingService> _mockTextService;
    private readonly Mock<IExternalToolService> _mockExternalToolService;
    private readonly Mock<ILogger<FileConversionService>> _mockLogger;
    private readonly FileConversionService _service;
    private readonly string _tempDirectory;

    public FileConversionServiceTests()
    {
        _mockImageService = new Mock<IImageConversionService>();
        _mockStringTableService = new Mock<IStringTableConversionService>();
        _mockTextService = new Mock<ITextProcessingService>();
        _mockExternalToolService = new Mock<IExternalToolService>();
        _mockLogger = new Mock<ILogger<FileConversionService>>();
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        _service = new FileConversionService(
            _mockImageService.Object,
            _mockStringTableService.Object,
            _mockTextService.Object,
            _mockExternalToolService.Object,
            _mockLogger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var service = new FileConversionService(
            _mockImageService.Object,
            _mockStringTableService.Object,
            _mockTextService.Object,
            _mockExternalToolService.Object,
            _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task ConvertFileAsync_WithNonExistentSource_ReturnsFailure()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "nonexistent.txt");
        var destPath = Path.Combine(_tempDirectory, "output.txt");

        // Act
        var result = await _service.ConvertFileAsync(sourcePath, destPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task ConvertFileAsync_WithImageConversion_CallsImageService()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.psd");
        var destPath = Path.Combine(_tempDirectory, "test.dds");
        await File.WriteAllTextAsync(sourcePath, "dummy");

        _mockImageService.Setup(x => x.ConvertImageAsync(
            sourcePath, destPath, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ConvertFileAsync(sourcePath, destPath);

        // Assert
        result.Success.Should().BeTrue();
        _mockImageService.Verify(x => x.ConvertImageAsync(
            sourcePath, destPath, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConvertFileAsync_WithStringTableConversion_CallsStringTableService()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.str");
        var destPath = Path.Combine(_tempDirectory, "test.csf");
        await File.WriteAllTextAsync(sourcePath, "dummy");

        _mockStringTableService.Setup(x => x.ConvertStrToCsfAsync(
            sourcePath, destPath, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        // Act
        var result = await _service.ConvertFileAsync(sourcePath, destPath);

        // Assert
        result.Success.Should().BeTrue();
        _mockStringTableService.Verify(x => x.ConvertStrToCsfAsync(
            sourcePath, destPath, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConvertFileAsync_WithTextFile_CallsTextService()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.ini");
        var destPath = Path.Combine(_tempDirectory, "test.ini");
        await File.WriteAllTextAsync(sourcePath, "dummy");

        _mockTextService.Setup(x => x.ProcessTextAsync(
            It.IsAny<string>(), It.IsAny<TextProcessingOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("processed");

        // Act
        var result = await _service.ConvertFileAsync(sourcePath, destPath);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ConvertFileAsync_WithBlenderFile_CallsExternalToolService()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.blend");
        var destPath = Path.Combine(_tempDirectory, "test.w3d");
        await File.WriteAllTextAsync(sourcePath, "dummy");

        _mockExternalToolService.Setup(x => x.ExecuteToolAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolOperationResult.CreateSuccess());

        // Act
        var result = await _service.ConvertFileAsync(sourcePath, destPath);

        // Assert
        result.Success.Should().BeTrue();
        _mockExternalToolService.Verify(x => x.ExecuteToolAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConvertFileAsync_WithUnsupportedConversion_CopiesFile()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.dat");
        var destPath = Path.Combine(_tempDirectory, "test.dat");
        await File.WriteAllTextAsync(sourcePath, "content");

        // Act
        var result = await _service.ConvertFileAsync(sourcePath, destPath);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(destPath).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertFileAsync_WithProgress_ReportsProgress()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.txt");
        var destPath = Path.Combine(_tempDirectory, "output.txt");
        await File.WriteAllTextAsync(sourcePath, "content");

        var progressMock = new Mock<IProgress<double>>();

        // Act
        await _service.ConvertFileAsync(sourcePath, destPath, null, progress: progressMock.Object);

        // Assert
        progressMock.Verify(p => p.Report(It.IsAny<double>()), Times.AtLeastOnce());
    }

    [Fact]
    public async Task ConvertFileAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.txt");
        var destPath = Path.Combine(_tempDirectory, "output.txt");
        await File.WriteAllTextAsync(sourcePath, "content");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.ConvertFileAsync(sourcePath, destPath, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ConvertFileAsync_WithException_ReturnsFailure()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.psd");
        var destPath = Path.Combine(_tempDirectory, "test.dds");
        await File.WriteAllTextAsync(sourcePath, "dummy");

        _mockImageService.Setup(x => x.ConvertImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Conversion failed"));

        // Act
        var result = await _service.ConvertFileAsync(sourcePath, destPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Conversion failed"));
    }

    [Theory]
    [InlineData(".psd", ".dds")]
    [InlineData(".tga", ".dds")]
    [InlineData(".tiff", ".dds")]
    [InlineData(".bmp", ".dds")]
    public async Task ConvertFileAsync_WithImageFormats_RoutesToImageService(string sourceExt, string targetExt)
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, $"test{sourceExt}");
        var destPath = Path.Combine(_tempDirectory, $"test{targetExt}");
        await File.WriteAllTextAsync(sourcePath, "dummy");

        _mockImageService.Setup(x => x.ConvertImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ConvertFileAsync(sourcePath, destPath);

        // Assert
        result.Success.Should().BeTrue();
        _mockImageService.Verify(x => x.ConvertImageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
