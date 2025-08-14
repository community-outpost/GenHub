namespace GenHub.Core.Models.Content
{
    /// <summary>
    /// Represents the phase of content acquisition.
    /// </summary>
    public enum ContentAcquisitionPhase
    {
        /// <summary>
        /// No acquisition phase specified.
        /// </summary>
        None,

        /// <summary>
        /// The phase where content is being downloaded.
        /// </summary>
        Downloading,

        /// <summary>
        /// The phase where content is being extracted from a package.
        /// </summary>
        Extracting,

        /// <summary>
        /// The phase where content is being copied to its destination.
        /// </summary>
        Copying,

        /// <summary>
        /// The phase where content is being validated for integrity.
        /// </summary>
        Validating,

        /// <summary>
        /// The phase where content is being delivered (moved) to its destination.
        /// </summary>
        Delivering,

        /// <summary>
        /// The phase indicating acquisition is completed.
        /// </summary>
        Completed,
    }
}
