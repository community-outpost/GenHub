namespace GenHub.Tests.Core.Services.Security;

/// <summary>Reports Windows-only tests as skipped on other hosts.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="WindowsFactAttribute"/> class.</summary>
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test uses Windows DPAPI.";
        }
    }
}
