using System;
using System.IO;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="EasyAntiCheatPrecondition"/>.
/// </summary>
public sealed class EasyAntiCheatPreconditionTests
{
    private readonly EasyAntiCheatPrecondition _precondition = new(NullLogger<EasyAntiCheatPrecondition>.Instance);

    /// <summary>
    /// Verifies that CanHandle returns false when step or manifest is null.
    /// </summary>
    [Fact]
    public void CanHandle_NullStepOrManifest_ReturnsFalse()
    {
        var manifest = CreateBaseManifest();
        var step = CreateEacStep();

        Assert.False(_precondition.CanHandle(null!, manifest));
        Assert.False(_precondition.CanHandle(step, null!));
    }

    /// <summary>
    /// Verifies that CanHandle returns false when step kind is not RunVerifiedInstaller.
    /// </summary>
    [Fact]
    public void CanHandle_NonInstallerKind_ReturnsFalse()
    {
        var manifest = CreateBaseManifest();
        var step = new InstallationStep
        {
            Name = "Remove File Step",
            Kind = InstallationStepKind.RemoveFile,
            TargetRelativePath = GameClientConstants.GeneralsOnlineEacSetupExecutable,
        };

        Assert.False(_precondition.CanHandle(step, manifest));
    }

    /// <summary>
    /// Verifies that CanHandle returns false when publisher type is not GeneralsOnline.
    /// </summary>
    [Fact]
    public void CanHandle_NonGeneralsOnlinePublisher_ReturnsFalse()
    {
        var manifest = CreateBaseManifest();
        manifest.Publisher = new PublisherInfo
        {
            PublisherType = "OtherPublisher",
        };

        var step = CreateEacStep();

        Assert.False(_precondition.CanHandle(step, manifest));
    }

    /// <summary>
    /// Verifies that CanHandle returns false when executable name does not match EAC setup executable.
    /// </summary>
    [Fact]
    public void CanHandle_NonEacExecutable_ReturnsFalse()
    {
        var manifest = CreateBaseManifest();
        var step = new InstallationStep
        {
            Name = "Other Executable",
            Kind = InstallationStepKind.RunVerifiedInstaller,
            TargetRelativePath = "other_installer.exe",
        };

        Assert.False(_precondition.CanHandle(step, manifest));
    }

    /// <summary>
    /// Verifies that IsAlreadyFulfilled returns false on non-Windows platforms.
    /// </summary>
    [Fact]
    public void IsAlreadyFulfilled_NonWindows_ReturnsFalse()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var manifest = CreateBaseManifest();
        var step = CreateEacStep();

        Assert.False(_precondition.IsAlreadyFulfilled(step, manifest));
    }

    /// <summary>
    /// Verifies that CanHandle behavior matches operating system requirements.
    /// </summary>
    [Fact]
    public void CanHandle_ValidStep_MatchesOperatingSystem()
    {
        var manifest = CreateBaseManifest();
        var step = CreateEacStep();

        var result = _precondition.CanHandle(step, manifest);
        Assert.Equal(OperatingSystem.IsWindows(), result);
    }

    private static ContentManifest CreateBaseManifest() => new()
    {
        Id = "1.0.test.gameclient.variant",
        Name = "Generals Online",
        Version = "1.0.0",
        ContentType = ContentType.GameClient,
        TargetGame = GameType.ZeroHour,
        Publisher = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
        },
    };

    private static InstallationStep CreateEacStep() => new()
    {
        Name = GeneralsOnlineConstants.EacStepName,
        Kind = InstallationStepKind.RunVerifiedInstaller,
        TargetRelativePath = GameClientConstants.GeneralsOnlineEacSetupExecutable,
        Arguments = ["install", GeneralsOnlineConstants.EacProductId],
        StepKey = GeneralsOnlineConstants.EacStepKey,
        RunOnce = true,
    };
}
