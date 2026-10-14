using System;
using System.Collections.Generic;
using System.Linq;

namespace Resrcify.SharedKernel.ArchitectureTesting.Helpers;

/// <summary>
/// The SharedKernel package restrictions with the custom layers added: each layer of the tier map that is neither a
/// conventional layer nor named by the restrictions takes the restrictions of the layer it is mapped to, or of the
/// layers at its tier.
/// </summary>
/// <param name="Restrictions">Each package → the layers that must not use it, custom layers included.</param>
/// <param name="InheritedFrom">Each custom layer → the layers whose restrictions it took.</param>
internal sealed record PackageRestrictionInheritance(
    IReadOnlyDictionary<SharedKernelPackage, string[]> Restrictions,
    IReadOnlyDictionary<string, string[]> InheritedFrom)
{
    /// <summary>
    /// Adds the custom layers of <paramref name="tiers"/> (and of <paramref name="like"/>) to
    /// <paramref name="restrictions"/>.
    /// </summary>
    /// <param name="restrictions">The configured restrictions: package → layers.</param>
    /// <param name="tiers">The layer → tier map.</param>
    /// <param name="like">Explicit choices: custom layer → the layer whose restrictions it takes.</param>
    /// <param name="conventionalLayers">The layers the conventional rules know; never treated as custom.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="like"/> maps a layer to itself, or to a name that is no layer.
    /// </exception>
    public static PackageRestrictionInheritance Resolve(
        IReadOnlyDictionary<SharedKernelPackage, string[]> restrictions,
        IReadOnlyDictionary<string, int> tiers,
        IReadOnlyDictionary<string, string> like,
        IReadOnlyCollection<string> conventionalLayers)
    {
        ArgumentNullException.ThrowIfNull(restrictions);
        ArgumentNullException.ThrowIfNull(tiers);
        ArgumentNullException.ThrowIfNull(like);
        ArgumentNullException.ThrowIfNull(conventionalLayers);

        var named = restrictions.Values
            .SelectMany(layers => layers)
            .ToHashSet(StringComparer.Ordinal);
        var conventional = conventionalLayers.ToHashSet(StringComparer.Ordinal);
        ValidateLike(like, named, conventional);

        var inheritedFrom = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var layer in CustomLayers(tiers, like, named, conventional))
        {
            var templates = like.TryGetValue(layer, out var chosen)
                ? [chosen]
                : LayersAtNearestTier(layer, tiers, like, named, conventional);
            if (templates.Length > 0)
                inheritedFrom[layer] = templates;
        }

        var resolved = restrictions.ToDictionary(
            restriction => restriction.Key,
            restriction => WithInheritingLayers(restriction.Value, inheritedFrom));
        return new PackageRestrictionInheritance(resolved, inheritedFrom);
    }

    /// <summary>
    /// <c>" (restricted like Web)"</c> for a layer that inherited its restrictions, else empty: appended to a failure,
    /// it tells where a custom layer's rule comes from.
    /// </summary>
    public string DescribeOrigin(string layer)
        => InheritedFrom.TryGetValue(layer, out var templates)
            ? $" (restricted like {string.Join(" and ", templates)}; choose with PackageRestrictionsLike)"
            : string.Empty;

    // A layer is custom when it is neither conventional nor named by the restrictions (a service naming it there has
    // configured it). One that is mapped explicitly is custom whatever else is true: the mapping is what was asked for.
    private static IEnumerable<string> CustomLayers(
        IReadOnlyDictionary<string, int> tiers,
        IReadOnlyDictionary<string, string> like,
        HashSet<string> named,
        HashSet<string> conventional)
        => tiers.Keys
            .Where(layer => !conventional.Contains(layer) && !named.Contains(layer))
            .Concat(like.Keys)
            .Distinct(StringComparer.Ordinal);

    // The layers that serve as templates at the tier nearest the custom layer's: all of them at that tier (the
    // strictest choice), the inner tier on a tie between one below and one above.
    private static string[] LayersAtNearestTier(
        string layer,
        IReadOnlyDictionary<string, int> tiers,
        IReadOnlyDictionary<string, string> like,
        HashSet<string> named,
        HashSet<string> conventional)
    {
        if (!tiers.TryGetValue(layer, out var tier))
            return [];

        var templates = tiers
            .Where(candidate => !StringComparer.Ordinal.Equals(candidate.Key, layer))
            .Where(candidate => !like.ContainsKey(candidate.Key))
            .Where(candidate => conventional.Contains(candidate.Key) || named.Contains(candidate.Key))
            .ToList();
        if (templates.Count == 0)
            return [];

        var nearest = templates
            .OrderBy(candidate => Math.Abs(candidate.Value - tier))
            .ThenBy(candidate => candidate.Value)
            .First()
            .Value;
        return templates
            .Where(candidate => candidate.Value == nearest)
            .Select(candidate => candidate.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] WithInheritingLayers(
        string[] layers,
        Dictionary<string, string[]> inheritedFrom)
    {
        var inheriting = inheritedFrom
            .Where(custom => custom.Value.Any(template => layers.Contains(template, StringComparer.Ordinal)))
            .Select(custom => custom.Key)
            .Where(custom => !layers.Contains(custom, StringComparer.Ordinal));
        return [.. layers, .. inheriting];
    }

    private static void ValidateLike(
        IReadOnlyDictionary<string, string> like,
        HashSet<string> named,
        HashSet<string> conventional)
    {
        foreach (var (layer, template) in like)
        {
            if (StringComparer.Ordinal.Equals(layer, template))
                throw new InvalidOperationException(
                    $"PackageRestrictionsLike maps {layer} to itself: map it to the layer whose SharedKernel package restrictions it takes.");

            var known = conventional.Contains(template) || named.Contains(template);
            if (!known || like.ContainsKey(template))
                throw new InvalidOperationException(
                    $"PackageRestrictionsLike maps {layer} to {template}, which isn't a conventional layer nor one the " +
                    "SharedKernel package restrictions name: map it to one of those (e.g. Layers.Web).");
        }
    }
}
