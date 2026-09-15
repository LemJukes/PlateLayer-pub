using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlateLayer.Core;

/// <summary>
/// Parts -> RunPlan. Everything here is pure: the whole pagination is computed before the
/// AutoCAD database is touched, so 'nn of NN' is knowable on page one and a dry run is free.
/// </summary>
public static class PlanBuilder
{
    public static RunPlan Build(IReadOnlyList<Part> parts, PlateLayerOptions opt, IEnumerable<string>? carriedWarnings = null)
    {
        var warnings = new List<string>(carriedWarnings ?? Array.Empty<string>());
        var excluded = new List<Exclusion>();
        var byMaterial = new List<(string Material, List<Tile> Tiles)>();

        var groups = opt.OneMaterialPerPlate
            ? parts.GroupBy(p => p.Material, StringComparer.OrdinalIgnoreCase)
                   .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                   .ToList()
            : new List<IGrouping<string, Part>> { new SingleGroup("ALL", parts) };

        foreach (var group in groups)
        {
            // Best fit per part first — it is needed either way, as the input to the uniform choice.
            var fitted = new List<(Part Part, ScaleRung Rung)>();
            foreach (var part in group)
            {
                var rung = ScaleLadder.Choose(part.WModel, part.HModel, opt);
                if (rung is null)
                {
                    excluded.Add(new Exclusion(part.Id, part.Label, part.Material,
                        $"{ArchFormat.Size(part.WModel, part.HModel)} will not fit the drawable area at the 1:{opt.ScaleFloorDenominator} floor."));
                    continue;
                }
                fitted.Add((part, rung.Value));
            }
            if (fitted.Count == 0) continue;

            if (opt.ScaleMode == ScaleMode.UniformPerPlate)
            {
                // The coarsest rung anything in this material needs governs the whole plate...
                var governing = fitted.OrderByDescending(f => f.Rung.Denominator).First().Rung;

                // ...then coarsen further if that buys plates back.
                if (opt.Policy != ScalePolicy.FinestThatFits)
                    governing = Coarsen(fitted.Select(f => f.Part).ToList(), governing, opt, warnings);

                fitted = fitted.Select(f => (f.Part, governing)).ToList();
            }

            byMaterial.Add((group.Key, fitted.Select(f => ScaleLadder.MakeTile(f.Part, f.Rung, opt)).ToList()));
        }

        var sheet = opt.DrawableArea.Width * opt.DrawableArea.Height;
        var pages = new List<PagePlan>();
        var pageIndex = 0;
        foreach (var (material, tiles) in byMaterial)
            foreach (var pageTiles in ShelfPacker.Pack(tiles, opt))
            {
                var used = pageTiles.Sum(t => t.WPaper * t.HPaper);
                pages.Add(new PagePlan(++pageIndex, material, pageTiles, sheet > 0 ? used / sheet : 0));
            }

        foreach (var page in pages)
            if (page.Tiles.Count > PlateLayerOptions.ViewportHardCap)
                warnings.Add($"Plate {page.Index} plans {page.Tiles.Count} viewports; MAXACTVP caps display at 64.");

        // "Why so many sheets" is not answerable from a plan that only lists scales, and the scale is
        // usually not the reason. Name whichever constraint is actually holding.

        // The strongest one first: one plate per material is a floor no scale policy can cross.
        var materialCount = pages.Select(p => p.Material).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (opt.OneMaterialPerPlate && materialCount > 1 && pages.Count == materialCount)
            warnings.Add($"{pages.Count} plates is one per material, which is the fewest possible while " +
                         $"a plate holds a single material. No scale policy goes below this; only mixing " +
                         $"materials on a plate would, and that is off by design.");

        if (opt.Policy != ScalePolicy.FewestSheets)
            foreach (var group in pages.GroupBy(p => p.Material, StringComparer.OrdinalIgnoreCase))
            {
                var partCount = group.Sum(p => p.Tiles.Count);
                var capFloor = (int)Math.Ceiling(partCount / (double)opt.EffectivePartsPerPage);
                if (group.Count() > 1 && group.Count() == capFloor)
                    warnings.Add($"{group.Key}: {group.Count()} plates is the parts-per-plate cap " +
                                 $"({opt.EffectivePartsPerPage}) talking, not the scale. Raise it with PLLSET > Parts, " +
                                 $"or switch PLLSET > Coarsen to FewestSheets.");
            }

        // Say it about the output, not the algorithm: which materials ended up with a part smaller
        // than the floor asks for, and which parts have no thickness at all.
        foreach (var group in pages.GroupBy(p => p.Material, StringComparer.OrdinalIgnoreCase))
        {
            var tiles = group.SelectMany(p => p.Tiles).ToList();
            // The floor governs the typical part; report the smallest anyway, because a hairline is
            // worth knowing about even when nothing can be done about it at this scale.
            var real = tiles.Where(t => Math.Min(t.WModel, t.HModel) > 1e-9).ToList();
            if (real.Count > 0)
            {
                var thinnest = real.Min(t => Math.Min(t.PartWPaper, t.PartHPaper));
                if (thinnest < opt.MinTypicalPartPaperSize - 1e-9)
                    warnings.Add($"{group.Key}: the smallest part plots {thinnest:0.###}\" on its short side. " +
                                 $"The scale is set by the typical part, so a small one sharing a sheet with large " +
                                 $"panels stays small — only its own sheet would fix that.");
            }

            var flat = tiles.Where(t => Math.Min(t.WModel, t.HModel) <= 1e-9).ToList();
            if (flat.Count > 0)
                warnings.Add($"{group.Key}: {flat.Count} part(s) have zero size on one axis — a scored line or " +
                             $"stray curve rather than a part. Check the source geometry.");
        }

        // Sparse plates are not always avoidable — a material holding two small parts cannot fill a
        // sheet without being split across two — but they should be visible before they are printed
        // rather than noticed on paper.
        foreach (var page in pages.Where(p => p.FillFraction < opt.LowFillWarning))
            warnings.Add($"Plate {page.Index} [{page.Material}] uses {page.FillFraction:0%} of the sheet " +
                         $"with {page.Tiles.Count} part(s) — the rest is white space.");

        // Input is sheet goods by contract. A layer whose code does not expand is either a sheet
        // good missing from the table or geometry that should not be in the run — both worth saying
        // out loud rather than quietly printing the raw layer name on a shop drawing.
        foreach (var material in pages.Select(p => p.Material).Distinct(StringComparer.OrdinalIgnoreCase))
            if (MaterialNames.Expand(material, opt) is null)
                warnings.Add($"Material layer '{material}' is not a recognised sheet-good code — " +
                             $"the title block will show the layer name as-is. Add it with PLLSET > Materials, " +
                             $"or check whether that geometry belongs in a plate run.");

        return new RunPlan(pages, excluded, warnings);
    }

    /// <summary>
    /// Finest rung that achieves the fewest plates for this material. Scans the whole ladder from
    /// the starting rung down rather than stopping at the first non-improvement, so a plateau
    /// followed by a gain is not missed. Ties go to the finer scale.
    /// </summary>
    private static ScaleRung Coarsen(IReadOnlyList<Part> parts, ScaleRung start, PlateLayerOptions opt, List<string> warnings)
    {
        int PagesAt(ScaleRung r) =>
            ShelfPacker.Pack(parts.Select(p => ScaleLadder.MakeTile(p, r, opt)).ToList(), opt).Count;

        // Median short side of the real parts. Degenerate ones — a scored line with no thickness —
        // are not parts and must not drag the statistic to zero.
        var sides = parts
            .Select(p => Math.Min(p.WModel, p.HModel))
            .Where(v => v > 1e-9)
            .OrderBy(v => v)
            .ToList();
        var typicalSide = sides.Count > 0 ? sides[sides.Count / 2] : 0.0;

        bool Legible(ScaleRung r) => typicalSide * r.PaperPerModel >= opt.MinTypicalPartPaperSize;

        // If the finest scale that fits already puts the smallest part under the floor, the floor
        // cannot be met at ANY scale — coarser only makes it smaller. Letting it veto coarsening
        // then protects nothing and costs sheets: one 2"-wide strip, or a zero-height scored line,
        // held a whole material at its finest scale and five plates it did not need.
        var floorReachable = Legible(start);

        var ladder = ScaleLadder.Rungs;
        var startIndex = 0;
        for (var i = 0; i < ladder.Count; i++)
            if (ladder[i].Denominator == start.Denominator) { startIndex = i; break; }

        var candidates = new List<(ScaleRung Rung, int Pages)> { (start, PagesAt(start)) };
        for (var i = startIndex + 1; i < ladder.Count; i++)
        {
            var rung = ladder[i];
            if (rung.Denominator > opt.ScaleFloorDenominator) break;
            if (floorReachable && !Legible(rung)) break; // the ladder only gets coarser from here
            candidates.Add((rung, PagesAt(rung)));
        }

        var fewest = candidates.Min(c => c.Pages);
        var best = candidates.First(c => c.Pages == fewest).Rung;   // candidates are finest-first

        if (best.Denominator != start.Denominator)
            warnings.Add($"{parts[0].Material}: coarsened {start} to {best} — {candidates[0].Pages} plate(s) becomes {fewest}.");

        return best;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ToJson(RunPlan plan) => JsonSerializer.Serialize(plan, Json);

    public static string ToReport(RunPlan plan)
    {
        var sb = new System.Text.StringBuilder();
        var materials = plan.Pages.Select(p => p.Material).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        sb.AppendLine($"PlateLayer dry run: {plan.TotalParts} part(s) on {plan.TotalPages} plate(s) " +
                      $"across {materials} material(s).");
        foreach (var page in plan.Pages)
        {
            sb.AppendLine($"  Plate {page.Index:00} of {plan.TotalPages:00}  [{page.Material}]  " +
                          $"{page.Tiles.Count} part(s)  {page.FillFraction:0%} of the sheet");
            foreach (var t in page.Tiles)
                sb.AppendLine($"      {t.Label}  {ArchFormat.Size(t.WModel, t.HModel),-28} at {t.Rung}");
        }
        foreach (var e in plan.Excluded)
            sb.AppendLine($"  EXCLUDED {e.Material} {e.Label}: {e.Reason}");
        foreach (var w in plan.Warnings)
            sb.AppendLine($"  WARNING: {w}");
        return sb.ToString();
    }

    private sealed class SingleGroup : List<Part>, IGrouping<string, Part>
    {
        public SingleGroup(string key, IEnumerable<Part> items) : base(items) => Key = key;
        public string Key { get; }
    }
}
