using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// A style rule matched against an element during style computation.
/// </summary>
/// <param name="Selector">The selector text that matched.</param>
/// <param name="Specificity">The selector specificity weight.</param>
/// <param name="Order">The document order of the rule.</param>
/// <param name="Source">The style sheet the rule was loaded from.</param>
public sealed record RmlMatchedRule(string Selector, int Specificity, int Order, string Source);
