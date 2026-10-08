namespace GenHub.Tests.Core.Infrastructure;

using Xunit;

/// <summary>
/// Reports a test as skipped on hosts that cannot create symbolic links, such as Windows without
/// Developer Mode or elevation, so a run shows whether symlink coverage really executed.
/// </summary>
public class SymlinkFactAttribute : FactAttribute
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
        DirectoryInfo? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("GenHub.SymlinkProbe.");
            var target = Path.Combine(directory.FullName, "target");
            File.WriteAllText(target, string.Empty);
            File.CreateSymbolicLink(Path.Combine(directory.FullName, "link"), target);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                directory?.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the probe directory.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of the probe directory.
            }
        }
    }
}
