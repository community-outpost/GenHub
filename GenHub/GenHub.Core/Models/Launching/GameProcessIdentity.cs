namespace GenHub.Core.Models.Launching;

/// <summary>
/// One way a launched game can present itself to the operating system: the name it runs under and
/// the directory its image resides in. A game started through a symbolic link can be reported under
/// the link or under its target depending on the platform, so discovery may carry both.
/// </summary>
/// <param name="ProcessName">The expected process name, which may include its file extension.</param>
/// <param name="Directory">The directory the image must reside in; when null or empty, the check is skipped.</param>
public sealed record GameProcessIdentity(string ProcessName, string? Directory)
{
    /// <inheritdoc/>
    public override string ToString() =>
        string.IsNullOrEmpty(Directory) ? ProcessName : $"{ProcessName} in {Directory}";
}
