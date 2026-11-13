namespace GenHub.Tests.Core.Infrastructure;

/// <summary>Runs a test everywhere except a Unix run as root, where permission bits do not block file operations.</summary>
public sealed class NonRootFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="NonRootFactAttribute"/> class.</summary>
    public NonRootFactAttribute()
    {
        if (UnixUser.IsRoot)
        {
            Skip = "Root bypasses Unix permission bits.";
        }
    }
}
