using System;
using System.IO;
using System.Runtime.Versioning;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Win32;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Precondition that checks whether Easy Anti-Cheat EOS product ID is already registered in the Windows registry.
/// </summary>
public class EasyAntiCheatPrecondition : IInstallationStepPrecondition
{
    /// <inheritdoc />
    public bool CanHandle(InstallationStep step, ContentManifest manifest)
    {
        if (!OperatingSystem.IsWindows() || step == null || manifest == null)
        {
            return false;
        }

        if (step.Kind != InstallationStepKind.RunVerifiedInstaller)
        {
            return false;
        }

        var isGeneralsOnline = string.Equals(
            manifest.Publisher?.PublisherType,
            PublisherTypeConstants.GeneralsOnline,
            StringComparison.OrdinalIgnoreCase);

        if (!isGeneralsOnline)
        {
            return false;
        }

        var fileName = Path.GetFileName(step.TargetRelativePath ?? string.Empty);
        return string.Equals(fileName, GameClientConstants.GeneralsOnlineEacSetupExecutable, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsAlreadyFulfilled(InstallationStep step, ContentManifest manifest)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsProductRegisteredOnWindows(step);
    }

    [SupportedOSPlatform("windows")]
    private static bool IsProductRegisteredOnWindows(InstallationStep step)
    {
        try
        {
            var productId = (step.Arguments is { Count: > 1 } && !string.IsNullOrWhiteSpace(step.Arguments[1]))
                ? step.Arguments[1]
                : GeneralsOnlineConstants.EacProductId;

            if (string.IsNullOrWhiteSpace(productId))
            {
                return false;
            }

            var subKeyPath = $@"SOFTWARE\EasyAntiCheat_EOS\{productId}";

            using var baseKey32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key32 = baseKey32.OpenSubKey(subKeyPath);
            if (key32 != null)
            {
                return true;
            }

            using var baseKey64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key64 = baseKey64.OpenSubKey(subKeyPath);
            if (key64 != null)
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
