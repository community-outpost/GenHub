using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Manifest;
using System.Collections.Generic;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Control Bar Pro's dependencies with a required GenTool added. Control Bar Pro has no GenTool
/// dependency, so tests that need a required add-on with a known download add one here.
/// </summary>
internal static class RequiredGenToolDependencies
{
    /// <summary>
    /// Gets Control Bar Pro's dependencies with a required GenTool added.
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
