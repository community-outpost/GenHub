namespace GenHub.Features.Workspace.Live;

/// <summary>
/// A file discovered under a live content root.
/// </summary>
/// <param name="RelativePath">Path relative to the live root, using forward slashes.</param>
/// <param name="FullPath">Absolute path of the source file.</param>
/// <param name="Size">Size of the source file in bytes.</param>
internal sealed record LiveSourceFile(string RelativePath, string FullPath, long Size);
