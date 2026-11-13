using Xunit;

namespace GenHub.Tests.Core.Collections;

/// <summary>
/// Prevents tests that mutate static testing hooks on <see cref="GenHub.Features.Tools.ViewModels.PublishShareViewModel"/>
/// or <see cref="GenHub.Features.Content.Services.Catalog.CatalogDocumentReader"/> from running concurrently.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PublishShareStaticStateCollection
{
    /// <summary>
    /// The collection name used by tests mutating static publisher studio hooks.
    /// </summary>
    public const string Name = "PublishShare static state";
}
