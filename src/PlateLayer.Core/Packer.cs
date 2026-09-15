namespace PlateLayer.Core;

/// <summary>
/// Shelf / first-fit-decreasing, then an even-out pass.
///
/// Not maxrects, not skyline: sheet count is not being optimised, and rows that read top-to-bottom
/// like a parts list are worth more here than density.
///
/// Greedy shelf packing has one ugly habit worth correcting. Tiles are placed tallest first, so
/// early plates fill up and whatever is left — the smallest parts — ends up alone on the last one.
/// Thirteen equal parts across two plates came out twelve and one. <see cref="Balance"/> spreads
/// them across the plates the material already needs, which changes no plate count and no scale.
/// </summary>
public static class ShelfPacker
{
    public static List<List<Tile>> Pack(IReadOnlyList<Tile> tiles, PlateLayerOptions opt)
    {
        var greedy = PackGreedy(tiles, opt);

        var result = opt.BalancePlates && greedy.Count > 1
            ? Balance(tiles, greedy.Count, opt) ?? greedy
            : greedy;

        return opt.CenterOnPlate
            ? result.Select(pg => Centre(pg, opt.DrawableArea)).ToList()
            : result;
    }

    // ------------------------------------------------------------------ greedy

    private static List<List<Tile>> PackGreedy(IReadOnlyList<Tile> tiles, PlateLayerOptions opt)
    {
        var area = opt.DrawableArea;
        var gutter = opt.Gutter;
        var cap = opt.EffectivePartsPerPage;

        var queue = tiles
            .OrderByDescending(t => t.HPaper)
            .ThenByDescending(t => t.WPaper)
            .ThenBy(t => t.PartId, StringComparer.Ordinal)
            .ToList();

        var pages = new List<List<Tile>>();
        var page = new List<Tile>();
        var shelfTopY = area.MaxY;      // shelves fill downward from the top of the drawable area
        var shelfHeight = 0.0;
        var cursorX = area.MinX;

        void NewPage()
        {
            if (page.Count > 0) pages.Add(page);
            page = new List<Tile>();
            shelfTopY = area.MaxY;
            shelfHeight = 0.0;
            cursorX = area.MinX;
        }

        foreach (var tile in queue)
        {
            if (page.Count >= cap) NewPage();

            // Does it fit on the current shelf?
            var fitsWidth = cursorX + tile.WPaper <= area.MaxX + 1e-9;
            var fitsHeightHere = shelfTopY - Math.Max(shelfHeight, tile.HPaper) >= area.MinY - 1e-9;

            if (!fitsWidth || !fitsHeightHere)
            {
                // Close the shelf and open the next one below it.
                if (shelfHeight > 0)
                {
                    shelfTopY -= shelfHeight + gutter;
                    shelfHeight = 0.0;
                    cursorX = area.MinX;
                }
                if (shelfTopY - tile.HPaper < area.MinY - 1e-9) NewPage();
            }

            page.Add(tile.At(cursorX, shelfTopY - tile.HPaper));
            cursorX += tile.WPaper + gutter;
            shelfHeight = Math.Max(shelfHeight, tile.HPaper);
        }

        if (page.Count > 0) pages.Add(page);
        return pages;
    }

    // ----------------------------------------------------------------- balance

    /// <summary>
    /// Deal the tiles into <paramref name="pageCount"/> groups of similar area — largest first into
    /// whichever group is currently lightest — then pack each group on its own.
    ///
    /// Area is a proxy for how full a plate looks, not a guarantee that a group's shapes actually
    /// fit one sheet, so every group is packed and checked. If any of them needs a second plate the
    /// whole attempt is abandoned and the greedy result stands: a balanced layout is worth having,
    /// an extra plate is not.
    /// </summary>
    private static List<List<Tile>>? Balance(IReadOnlyList<Tile> tiles, int pageCount, PlateLayerOptions opt)
    {
        if (pageCount < 2 || tiles.Count < pageCount) return null;

        var cap = opt.EffectivePartsPerPage;
        var bins = Enumerable.Range(0, pageCount).Select(_ => new List<Tile>()).ToList();
        var loads = new double[pageCount];

        var ordered = tiles
            .OrderByDescending(t => t.WPaper * t.HPaper)
            .ThenByDescending(t => t.HPaper)
            .ThenBy(t => t.PartId, StringComparer.Ordinal);

        foreach (var tile in ordered)
        {
            var target = -1;
            for (var i = 0; i < pageCount; i++)
            {
                if (bins[i].Count >= cap) continue;
                if (target < 0 || loads[i] < loads[target] - 1e-12) target = i;
            }
            if (target < 0) return null;            // the per-plate cap cannot absorb them

            bins[target].Add(tile);
            loads[target] += tile.WPaper * tile.HPaper;
        }

        var packed = new List<List<Tile>>();
        foreach (var bin in bins)
        {
            if (bin.Count == 0) return null;        // fewer plates than asked for: not this pass's job
            var pages = PackGreedy(bin, opt);
            if (pages.Count != 1) return null;      // the shapes do not fit even though the area does
            packed.Add(pages[0]);
        }

        return packed;
    }

    // ------------------------------------------------------------------ centre

    /// <summary>
    /// Shelf packing fills from the top-left corner, which leaves a sparse plate hugging two edges
    /// and crowds the annotation against the border. Re-centre the finished block; nothing about
    /// the relative layout changes.
    /// </summary>
    private static List<Tile> Centre(List<Tile> tiles, BBox area)
    {
        if (tiles.Count == 0) return tiles;

        var minX = tiles.Min(t => t.OriginX);
        var minY = tiles.Min(t => t.OriginY);
        var maxX = tiles.Max(t => t.OriginX + t.WPaper);
        var maxY = tiles.Max(t => t.OriginY + t.HPaper);

        var dx = area.MinX + (area.Width - (maxX - minX)) / 2.0 - minX;
        var dy = area.MinY + (area.Height - (maxY - minY)) / 2.0 - minY;

        return tiles.Select(t => t.Shift(dx, dy)).ToList();
    }
}
