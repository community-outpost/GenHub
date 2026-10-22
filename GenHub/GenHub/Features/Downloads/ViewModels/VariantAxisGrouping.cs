using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Builds <see cref="VariantAxisGroup"/> collections from a flat variant list.
/// Multi-axis is rendering infrastructure only — no cross-product filtering.
/// </summary>
public static class VariantAxisGrouping
{
    /// <summary>
    /// Rebuilds <paramref name="axes"/> from <paramref name="variants"/>, wiring
    /// <paramref name="onSelectionCommitted"/> to each group's selection event.
    /// </summary>
    /// <param name="variants">Flat variant list.</param>
    /// <param name="axes">Target collection to clear and refill.</param>
    /// <param name="selectedVariant">Current selection to sync into each axis.</param>
    /// <param name="onSelectionCommitted">Handler for ComboBox picks.</param>
    /// <param name="unsubscribe">Previous unsubscribe action (clears prior handlers).</param>
    /// <returns>A new unsubscribe action for the wired handlers.</returns>
    public static Action Rebuild(
        IList<InstallableVariant> variants,
        ObservableCollection<VariantAxisGroup> axes,
        InstallableVariant? selectedVariant,
        Action<InstallableVariant?> onSelectionCommitted,
        Action? unsubscribe)
    {
        unsubscribe?.Invoke();

        axes.Clear();

        if (variants.Count == 0)
        {
            return () => { };
        }

        var groups = new List<VariantAxisGroup>();
        foreach (var variant in variants)
        {
            var key = string.IsNullOrWhiteSpace(variant.VariantType) ? "default" : variant.VariantType.Trim();
            var existing = groups.FirstOrDefault(g =>
                string.Equals(g.AxisKey, key, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new VariantAxisGroup
                {
                    AxisKey = key,
                    AxisLabel = FormatAxisLabel(key),
                };
                groups.Add(existing);
            }

            existing.Options.Add(variant);
        }

        var showLabels = groups.Count > 1;
        var handlers = new List<(VariantAxisGroup Group, Action<InstallableVariant?> Handler)>();

        foreach (var group in groups)
        {
            group.ShowAxisLabel = showLabels;
            Action<InstallableVariant?> handler = onSelectionCommitted;
            group.SelectionCommitted += handler;
            handlers.Add((group, handler));
            axes.Add(group);
        }

        SyncSelections(axes, selectedVariant);

        return () =>
        {
            foreach (var (group, handler) in handlers)
            {
                group.SelectionCommitted -= handler;
            }
        };
    }

    /// <summary>
    /// Syncs each axis's <see cref="VariantAxisGroup.SelectedOption"/> from the card/detail selection.
    /// </summary>
    /// <param name="axes">Axis groups.</param>
    /// <param name="selectedVariant">Active variant.</param>
    public static void SyncSelections(
        IEnumerable<VariantAxisGroup> axes,
        InstallableVariant? selectedVariant)
    {
        foreach (var axis in axes)
        {
            axis.SuppressSelectionEvents = true;
            try
            {
                if (selectedVariant != null &&
                    axis.Options.Any(o => ReferenceEquals(o, selectedVariant)))
                {
                    axis.SelectedOption = selectedVariant;
                }
                else if (selectedVariant != null)
                {
                    var match = axis.Options.FirstOrDefault(o =>
                        !string.IsNullOrEmpty(selectedVariant.ManifestId) &&
                        string.Equals(o.ManifestId, selectedVariant.ManifestId, StringComparison.OrdinalIgnoreCase));
                    axis.SelectedOption = match ?? axis.Options.FirstOrDefault();
                }
                else
                {
                    axis.SelectedOption = axis.Options.FirstOrDefault();
                }
            }
            finally
            {
                axis.SuppressSelectionEvents = false;
            }
        }
    }

    /// <summary>
    /// Formats an axis key for UI display.
    /// </summary>
    /// <param name="axisKey">Raw key.</param>
    /// <returns>Title-cased label.</returns>
    public static string FormatAxisLabel(string axisKey)
    {
        if (string.Equals(axisKey, "default", StringComparison.OrdinalIgnoreCase))
        {
            return "Variant";
        }

        if (string.Equals(axisKey, "game-type", StringComparison.OrdinalIgnoreCase))
        {
            return "Game";
        }

        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(axisKey.Replace('-', ' ').ToLowerInvariant());
    }
}
