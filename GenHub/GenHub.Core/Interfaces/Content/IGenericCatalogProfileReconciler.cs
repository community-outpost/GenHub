using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for reconciling profiles when subscribed generic catalog updates are detected.
/// When an update is found, this service updates profiles using catalog content,
/// prompts user for strategy (replace vs new profile) if not configured, and applies the update.
/// </summary>
public interface IGenericCatalogProfileReconciler : IPublisherReconciler
{
}
