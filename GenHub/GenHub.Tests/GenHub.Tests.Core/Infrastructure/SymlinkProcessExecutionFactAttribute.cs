namespace GenHub.Tests.Core.Infrastructure;

using System.Runtime.InteropServices;

/// <summary>
/// Reports a test as skipped on hosts that cannot create symbolic links or where system security
/// policies prevent executing copied system binaries through symlinks (such as AMFI on Apple Silicon macOS).
/// </summary>
public sealed class SymlinkProcessExecutionFactAttribute : SymlinkFactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="SymlinkProcessExecutionFactAttribute"/> class.</summary>
    public SymlinkProcessExecutionFactAttribute()
    {
        if (Skip == null && OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            Skip = "Apple Mobile File Integrity terminates ad-hoc signed copies of arm64e system binaries on Apple Silicon.";
        }
    }
}
