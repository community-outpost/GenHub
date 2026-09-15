namespace GenHub.Core.Features.ActionSets;

using System.Collections.Generic;

/// <summary>
/// Defines a provider for discovering ActionSets.
/// </summary>
public interface IActionSetProvider
{
    /// <summary>
    /// Gets the action sets provided by this source.
    /// </summary>
    /// <returns>A collection of action sets.</returns>
    IEnumerable<IActionSet> GetActionSets();
}
