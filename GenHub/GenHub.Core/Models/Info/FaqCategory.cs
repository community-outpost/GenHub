using System.Collections.Generic;

namespace GenHub.Core.Models.Info;

/// <summary>
/// Represents a category of FAQ items.
/// </summary>
/// <param name="Title">The title of the category.</param>
/// <param name="Items">The list of FAQ items in this category.</param>
public record FaqCategory(string Title, IReadOnlyList<FaqItem> Items);
