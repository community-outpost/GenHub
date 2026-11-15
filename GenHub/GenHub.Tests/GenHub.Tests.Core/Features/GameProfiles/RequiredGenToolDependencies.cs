using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Manifest;
using System.Collections.Generic;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Control Bar Pro's dependencies with GenTool made required. GenTool is only recommended for
/// Control Bar Pro, so tests that need a required add-on with a known download make it required here.
/// </summary>
internal static class RequiredGenToolDependencies
{
    /// <summary>
    /// Gets Control Bar Pro's dependencies with a required GenTool in place of the recommended one.
    /// </summary>
    /// <returns>The dependency list.</returns>
    public static List<ContentDependency> ForControlBarPro()
    {
        var requiredGenTool = GenPatcherDependencyBuilder.CreateGenToolDependency();
        var dependencies = GenPatcherDependencyBuilder.GetDependencies("cbpr", GenPatcherContentRegistry.GetMetadata("cbpr"));
        dependencies.RemoveAll(dependency => dependency.Id.Equals(requiredGenTool.Id));
        dependencies.Add(requiredGenTool);
        return dependencies;
    }
}
