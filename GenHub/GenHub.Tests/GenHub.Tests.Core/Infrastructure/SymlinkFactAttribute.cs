namespace GenHub.Tests.Core.Infrastructure;

/// <summary>
/// Reports a test as skipped on hosts that cannot create symbolic links, such as Windows without
/// Developer Mode or elevation, so a run shows whether symlink coverage really executed.
/// </summary>
public sealed class SymlinkFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> SymbolicLinksSupported = new(ProbeSymbolicLinks);

    /// <summary>Initializes a new instance of the <see cref="SymlinkFactAttribute"/> class.</summary>
    public SymlinkFactAttribute()
    {
        if (!SymbolicLinksSupported.Value)
        {
            Skip = "This host cannot create symbolic links.";
        }
    }

    private static bool ProbeSymbolicLinks()
    {
        var directory = Directory.CreateTempSubdirectory("GenHub.SymlinkProbe.");
        try
        {
            var target = Path.Combine(directory.FullName, "target");
            File.WriteAllText(target, string.Empty);
            File.CreateSymbolicLink(Path.Combine(directory.FullName, "link"), target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of the probe directory.
            }
        }
    }
}
