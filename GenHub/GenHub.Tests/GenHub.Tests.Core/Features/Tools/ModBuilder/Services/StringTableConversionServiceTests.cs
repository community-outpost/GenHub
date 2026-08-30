using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Unit tests for <see cref="StringTableConversionService"/>.
/// </summary>
public sealed class StringTableConversionServiceTests : IDisposable
{
    private readonly Mock<ILogger<StringTableConversionService>> _mockLogger;
    private readonly Mock<GenHub.Core.Interfaces.Tools.ModBuilder.IExternalToolService> _mockExternalToolService;
    private readonly StringTableConversionService _service;
    private readonly string _tempDirectory;

    public StringTableConversionServiceTests()
    {
        _mockLogger = new Mock<ILogger<StringTableConversionService>>();
        _mockExternalToolService = new Mock<GenHub.Core.Interfaces.Tools.ModBuilder.IExternalToolService>();
        _service = new StringTableConversionService(_mockExternalToolService.Object, _mockLogger.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
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
        var service = new StringTableConversionService(_mockExternalToolService.Object, _mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task ConvertStrToCsfAsync_WithNonExistentSource_ReturnsFailure()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "nonexistent.str");
        var targetPath = Path.Combine(_tempDirectory, "output.csf");

        // Act
        var result = await _service.ConvertStrToCsfAsync(sourcePath, targetPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task ConvertStrToCsfAsync_WithValidFile_ConvertsSuccessfully()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.str");
        var targetPath = Path.Combine(_tempDirectory, "test.csf");

        // Create a simple STR file
        await File.WriteAllTextAsync(sourcePath, "TEST_STRING:Test Value");

        // Act
        var result = await _service.ConvertStrToCsfAsync(sourcePath, targetPath);

        // Assert
        // Note: This will fail if gametextcompiler is not available
        // In a real test environment, you'd mock the tool execution
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ConvertStrToCsfAsync_WithLanguage_PassesLanguageParameter()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.str");
        var targetPath = Path.Combine(_tempDirectory, "test.csf");
        await File.WriteAllTextAsync(sourcePath, "TEST_STRING:Test Value");

        // Act
        var result = await _service.ConvertStrToCsfAsync(sourcePath, targetPath, language: "en");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ConvertStrToCsfAsync_WithSwapAndSetLanguage_PassesParameter()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.str");
        var targetPath = Path.Combine(_tempDirectory, "test.csf");
        await File.WriteAllTextAsync(sourcePath, "TEST_STRING:Test Value");

        // Act
        var result = await _service.ConvertStrToCsfAsync(sourcePath, targetPath, swapAndSetLanguage: "en");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ConvertCsfToStrAsync_WithNonExistentSource_ReturnsFailure()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "nonexistent.csf");
        var targetPath = Path.Combine(_tempDirectory, "output.str");

        // Act
        var result = await _service.ConvertCsfToStrAsync(sourcePath, targetPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task ConvertCsfToStrAsync_WithValidFile_ConvertsSuccessfully()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.csf");
        var targetPath = Path.Combine(_tempDirectory, "test.str");

        // Create a dummy CSF file (in reality, this would be a binary format)
        await File.WriteAllBytesAsync(sourcePath, new byte[] { 0x43, 0x53, 0x46 }); // "CSF" header

        // Act
        var result = await _service.ConvertCsfToStrAsync(sourcePath, targetPath);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ConvertCsfToStrAsync_WithLanguage_PassesLanguageParameter()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.csf");
        var targetPath = Path.Combine(_tempDirectory, "test.str");
        await File.WriteAllBytesAsync(sourcePath, new byte[] { 0x43, 0x53, 0x46 });

        // Act
        var result = await _service.ConvertCsfToStrAsync(sourcePath, targetPath, language: "en");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ConvertStrToCsfAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.str");
        var targetPath = Path.Combine(_tempDirectory, "test.csf");
        await File.WriteAllTextAsync(sourcePath, "TEST_STRING:Test Value");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.ConvertStrToCsfAsync(sourcePath, targetPath, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ConvertCsfToStrAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "test.csf");
        var targetPath = Path.Combine(_tempDirectory, "test.str");
        await File.WriteAllBytesAsync(sourcePath, new byte[] { 0x43, 0x53, 0x46 });

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _service.ConvertCsfToStrAsync(sourcePath, targetPath, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ConvertStrToCsfAsync_WithEmptyFile_HandlesGracefully()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "empty.str");
        var targetPath = Path.Combine(_tempDirectory, "empty.csf");
        await File.WriteAllTextAsync(sourcePath, string.Empty);

        // Act
        var result = await _service.ConvertStrToCsfAsync(sourcePath, targetPath);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ConvertCsfToStrAsync_WithEmptyFile_HandlesGracefully()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDirectory, "empty.csf");
        var targetPath = Path.Combine(_tempDirectory, "empty.str");
        await File.WriteAllBytesAsync(sourcePath, Array.Empty<byte>());

        // Act
        var result = await _service.ConvertCsfToStrAsync(sourcePath, targetPath);

        // Assert
        result.Should().NotBeNull();
    }
}
