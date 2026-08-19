using System;
using System.IO;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Precondition that checks whether Easy Anti-Cheat EOS service is already installed on Windows.
/// </summary>
public class EasyAntiCheatPrecondition : IInstallationStepPrecondition
{
    /// <inheritdoc />
    public bool CanHandle(InstallationStep step, ContentManifest manifest)
    {
        if (!OperatingSystem.IsWindows() || step == null)
        {
            return false;
        }

        if (step.Kind != InstallationStepKind.RunVerifiedInstaller)
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

        try
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFilesX86))
            {
                var serviceExe = Path.Combine(programFilesX86, "EasyAntiCheat_EOS", "EasyAntiCheat_EOS.exe");
                if (File.Exists(serviceExe))
                {
                    return true;
                }
            }

            var commonProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
            if (!string.IsNullOrEmpty(commonProgramFilesX86))
            {
                var commonExe = Path.Combine(commonProgramFilesX86, "EasyAntiCheat", "EasyAntiCheat_EOS.exe");
                if (File.Exists(commonExe))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
