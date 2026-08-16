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
/// Unit tests for <see cref="ExternalToolService"/>.
/// </summary>
public sealed class ExternalToolServiceTests : IDisposable
{
    private readonly Mock<ILogger<ExternalToolService>> _mockLogger;
    private readonly string _tempDirectory;
    private readonly ExternalToolService _service;

    public ExternalToolServiceTests()
    {
        _mockLogger = new Mock<ILogger<ExternalToolService>>();
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
        _service = new ExternalToolService(_mockLogger.Object);
    }

    public void Dispose()
    {
        _service?.Dispose();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Act
        var service = new ExternalToolService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteToolAsync_WithNonExistentTool_ReturnsFailure()
    {
        // Arrange
        var nonExistentTool = Path.Combine(_tempDirectory, "nonexistent.exe");

        // Act
        var result = await _service.ExecuteToolAsync(
            nonExistentTool,
            string.Empty,
            _tempDirectory);

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteToolAsync_WithEmptyArguments_DoesNotThrow()
    {
        // Arrange
        var scriptPath = await CreateExecutableScriptAsync("test", "@echo off\nexit /b 0", "exit 0");

        // Act
        var result = await _service.ExecuteToolAsync(
            scriptPath,
            string.Empty,
            _tempDirectory);

        // Assert
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteToolAsync_WithValidTool_ReturnsSuccess()
    {
        // Arrange
        var scriptPath = await CreateExecutableScriptAsync("success", "@echo off\nexit /b 0", "exit 0");

        // Act
        var result = await _service.ExecuteToolAsync(
            scriptPath,
            string.Empty,
            _tempDirectory);

        // Assert
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteToolAsync_WithFailingTool_ReturnsFailure()
    {
        // Arrange
        var scriptPath = await CreateExecutableScriptAsync("failure", "@echo off\nexit /b 1", "exit 1");

        // Act
        var result = await _service.ExecuteToolAsync(
            scriptPath,
            string.Empty,
            _tempDirectory);

        // Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteToolAsync_WithArguments_PassesArgumentsCorrectly()
    {
        // Arrange
        var outputFile = Path.Combine(_tempDirectory, "output.txt");
        var scriptPath = await CreateExecutableScriptAsync(
            "args",
            $"@echo off\necho %* > \"{outputFile}\"",
            $"echo \"$@\" > \"{outputFile}\"");

        // Act
        var result = await _service.ExecuteToolAsync(
            scriptPath,
            "arg1 arg2",
            _tempDirectory);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputFile).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteToolAsync_WithWorkingDirectory_UsesCorrectDirectory()
    {
        // Arrange
        var workDir = Path.Combine(_tempDirectory, "workdir");
        Directory.CreateDirectory(workDir);
        var outputFile = Path.Combine(_tempDirectory, "pwd.txt");
        var scriptPath = await CreateExecutableScriptAsync(
            "pwd",
            $"@echo off\ncd > \"{outputFile}\"",
            $"pwd > \"{outputFile}\"");

        // Act
        var result = await _service.ExecuteToolAsync(
            scriptPath,
            string.Empty,
            workDir);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputFile).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteToolAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var scriptPath = await CreateExecutableScriptAsync(
            "long",
            "@echo off\ntimeout /t 10 /nobreak",
            "sleep 10");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await _service.ExecuteToolAsync(
            scriptPath,
            string.Empty,
            _tempDirectory,
            null,
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteToolAsync_CalledMultipleTimes_WorksCorrectly()
    {
        // Arrange
        var scriptPath = await CreateExecutableScriptAsync("multi", "@echo off\nexit /b 0", "exit 0");

        // Act
        var result1 = await _service.ExecuteToolAsync(scriptPath, string.Empty, _tempDirectory);
        var result2 = await _service.ExecuteToolAsync(scriptPath, string.Empty, _tempDirectory);
        var result3 = await _service.ExecuteToolAsync(scriptPath, string.Empty, _tempDirectory);

        // Assert
        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();
        result3.Success.Should().BeTrue();
    }

    private async Task<string> CreateExecutableScriptAsync(
        string name,
        string windowsContent,
        string unixContent)
    {
        var isWindows = OperatingSystem.IsWindows();
        var fileName = name + (isWindows ? ".bat" : ".sh");
        var filePath = Path.Combine(_tempDirectory, fileName);
        var content = isWindows ? windowsContent : $"#!/bin/sh\n{unixContent}\n";

        await File.WriteAllTextAsync(filePath, content);

        if (!isWindows)
        {
            File.SetUnixFileMode(
                filePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return filePath;
    }

    [Fact]
    public async Task ValidateToolAsync_WithExistingTool_ReturnsTrue()
    {
        // Arrange
        var toolPath = Path.Combine(_tempDirectory, "tool.exe");
        await File.WriteAllTextAsync(toolPath, "dummy");

        // Act
        var result = await _service.ValidateToolAsync(toolPath);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateToolAsync_WithNonExistentTool_ReturnsFalse()
    {
        // Arrange
        var toolPath = Path.Combine(_tempDirectory, "nonexistent.exe");

        // Act
        var result = await _service.ValidateToolAsync(toolPath);

        // Assert
        result.Success.Should().BeFalse();
        result.Data.Should().BeFalse();
    }
}
