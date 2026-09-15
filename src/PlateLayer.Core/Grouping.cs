namespace PlateLayer.Core;

public sealed record GroupingResult(IReadOnlyList<Part> Parts, IReadOnlyList<string> Warnings);

/// <summary>
/// Curves -> parts. Deterministic, pure, no AutoCAD.
///
/// Three stages:
///   1. drop curves on excluded layers (sheet outlines, guides, title block, annotation);
///   2. union-find clustering on bbox proximity, so a cutout joins the loop it sits inside;
///   3. sheet-outline rejection, for the case where a group rectangle was NOT caught by stage 1.
///
/// Known limitation, deliberate: a concave part whose bbox swallows a disjoint neighbour merges
/// the two. Verified absent from the reference cut file — every part bbox there is disjoint apart
/// from true cutouts. Endpoint-graph connectivity is the fix when a file needs it, and is not built.
/// </summary>
public static class Grouping
{
    public static GroupingResult Group(IReadOnlyList<Curve2D> curves, PlateLayerOptions opt)
    {
        var warnings = new List<string>();

        var kept = curves
            .Where(c => !opt.IsExcludedLayer(c.Layer))
            .Where(c => c.Extents.Width > 1e-9 || c.Extents.Height > 1e-9)
            .OrderBy(c => c.Extents.MinX).ThenBy(c => c.Extents.MinY).ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        // Name the layers, not just a total. A bare "24 curve(s) ignored" hid a whole material's
        // worth of parts sitting on an excluded layer, and gave no way to notice.
        var byLayer = curves
            .Where(c => opt.IsExcludedLayer(c.Layer))
            .GroupBy(c => c.Layer, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in byLayer)
            warnings.Add($"{group.Count()} curve(s) on excluded layer '{group.Key}' were not considered. " +
                         $"If those are parts, remove the layer from the exclusion list.");

        var degenerate = curves.Count(c => !opt.IsExcludedLayer(c.Layer)
                                           && c.Extents.Width <= 1e-9 && c.Extents.Height <= 1e-9);
        if (degenerate > 0) warnings.Add($"{degenerate} zero-length curve(s) ignored.");
        if (kept.Count == 0) return new GroupingResult(Array.Empty<Part>(), warnings);

        var clusters = Cluster(kept, opt.GapTolerance);
        var parts = new List<Part>();
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cluster in clusters)
        {
            foreach (var resolved in RejectSheetOutline(cluster, opt, warnings))
            {
                var outerCurve = resolved.OrderByDescending(c => c.Extents.Area).First();
                var material = outerCurve.Layer;
                counters.TryGetValue(material, out var n);
                counters[material] = ++n;
                parts.Add(new Part(
                    Id: $"{material}#{n:00}",
                    Material: material,
                    Curves: resolved,
                    Outer: outerCurve.Extents,
                    Label: $"{n:00}"));
            }
        }

        parts = parts
            .OrderBy(p => p.Material, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(p => p.Outer.MaxY)
            .ThenBy(p => p.Outer.MinX)
            .ToList();

        return new GroupingResult(parts, warnings);
    }

    /// <summary>Union-find over bbox proximity. O(n^2) on purpose — part counts are in the dozens.</summary>
    private static List<List<Curve2D>> Cluster(List<Curve2D> curves, double tol)
    {
        var parent = Enumerable.Range(0, curves.Count).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }

        for (var i = 0; i < curves.Count; i++)
            for (var j = i + 1; j < curves.Count; j++)
                if (curves[i].Extents.Touches(curves[j].Extents, tol))
                    Union(i, j);

        var buckets = new Dictionary<int, List<Curve2D>>();
        for (var i = 0; i < curves.Count; i++)
        {
            var root = Find(i);
            if (!buckets.TryGetValue(root, out var list)) buckets[root] = list = new List<Curve2D>();
            list.Add(curves[i]);
        }
        return buckets.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    }

    /// <summary>
    /// If the cluster's largest loop contains N mutually disjoint loops it is a sheet/group outline,
    /// not a part with cutouts. Drop it and re-cluster what is left.
    /// </summary>
    private static List<List<Curve2D>> RejectSheetOutline(List<Curve2D> cluster, PlateLayerOptions opt, List<string> warnings)
    {
        var single = new List<List<Curve2D>> { cluster };
        if (cluster.Count < opt.SheetOutlineMinChildren + 1) return single;

        var outer = cluster.OrderByDescending(c => c.Extents.Area).First();
        var inner = cluster.Where(c => !ReferenceEquals(c, outer)).ToList();
        if (!inner.All(c => outer.Extents.Contains(c.Extents, 1e-6))) return single;

        // Count TOP-LEVEL children: loops inside `outer` that are not themselves inside another inner
        // loop. Counting merely non-touching loops undercounts badly, because a part carrying cutouts
        // contributes zero — which is how a sheet holding eight cutout-bearing parts scored 2.
        var topLevel = inner.Count(a => !inner.Any(b => !ReferenceEquals(a, b) && b.Extents.Contains(a.Extents, 1e-6)));
        if (topLevel < opt.SheetOutlineMinChildren) return single;

        warnings.Add($"Loop on layer '{outer.Layer}' {outer.Extents} contains {topLevel} top-level loops — treated as a sheet outline, not a part.");
        return Cluster(inner.OrderBy(c => c.Extents.MinX).ThenBy(c => c.Id, StringComparer.Ordinal).ToList(), opt.GapTolerance);
    }
}
