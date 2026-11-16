namespace GenHub.Tests.Core.Infrastructure;

/// <summary>
/// Finds a Unix sleep binary that still runs after a fixture copies it under another name.
/// </summary>
internal static class RenamableSleepExecutable
{
    /// <summary>
    /// Candidates in order of preference. Ubuntu 26.04 ships uutils coreutils as /bin/sleep. That build
    /// is a multicall binary that picks its tool from the file name, so a renamed copy exits with
    /// "unknown program" instead of sleeping. The GNU build ships beside it as gnusleep and does not
    /// care about its name.
    /// </summary>
    private static readonly string[] Candidates = ["/usr/bin/gnusleep", "/bin/sleep", "/usr/bin/sleep"];

    /// <summary>
    /// Gets the path of the preferred sleep binary on this host.
    /// </summary>
    /// <exception cref="FileNotFoundException">No candidate exists on this host.</exception>
    public static string FilePath =>
        Candidates.FirstOrDefault(File.Exists)
        ?? throw new FileNotFoundException($"No renamable sleep binary found. Probed: {string.Join(", ", Candidates)}");
}
