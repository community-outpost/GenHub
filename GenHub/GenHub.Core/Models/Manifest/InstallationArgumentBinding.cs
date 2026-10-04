namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Binds one installation-step argument to a value inside a delivered package file,
/// resolved when the step executes. Values known only after delivery (installer IDs
/// carried in a package's own config files) are declared this way instead of baked
/// as literals that rot when the publisher changes them.
/// </summary>
public class InstallationArgumentBinding
{
    /// <summary>
    /// Gets or sets the zero-based index into the step's arguments this binding fills.
    /// </summary>
    public int ArgumentIndex { get; set; }

    /// <summary>
    /// Gets or sets the binding source. Only <c>json</c> is supported: the value is
    /// read from a JSON property in the referenced file.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the package-relative path of the file to read.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the JSON property name whose string value fills the argument.
    /// </summary>
    public string Key { get; set; } = string.Empty;
}
