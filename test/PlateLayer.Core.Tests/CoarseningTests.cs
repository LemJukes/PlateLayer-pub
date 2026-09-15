namespace PlateLayer.Core.Tests;

public class CoarseningTests
{
    private static RunPlan Plan(PlateLayerOptions opt)
    {
        var g = Grouping.Group(SampleCutFile.Curves, opt);
        return PlanBuilder.Build(g.Parts, opt, g.Warnings);
    }

    private static int PagesFor(PlateLayerOptions opt) => Plan(opt).TotalPages;

    [Fact]
    public void Coarsening_never_costs_plates()
    {
        var off = PagesFor(new PlateLayerOptions { CoarsenForDensity = false });
        var on = PagesFor(new PlateLayerOptions { CoarsenForDensity = true });
        Assert.True(on <= off, $"coarsening made it worse: {off} -> {on}");
    }

    [Fact]
    public void Coarsening_is_on_by_default_and_wins_on_the_reference_file()
    {
        Assert.True(new PlateLayerOptions().CoarsenForDensity);
        Assert.True(PagesFor(new PlateLayerOptions()) < PagesFor(new PlateLayerOptions { CoarsenForDensity = false }));
    }

    [Fact]
    public void A_plate_still_carries_exactly_one_scale()
    {
        foreach (var page in Plan(new PlateLayerOptions()).Pages)
            Assert.Single(page.Tiles.Select(t => t.Rung.Denominator).Distinct());
    }

    [Fact]
    public void The_legibility_floor_is_respected()
    {
        var opt = new PlateLayerOptions { MinTypicalPartPaperSize = 0.5 };
        foreach (var t in Plan(opt).Pages.SelectMany(p => p.Tiles))
            Assert.True(Math.Min(t.PartWPaper, t.PartHPaper) >= opt.MinTypicalPartPaperSize - 1e-9,
                $"{t.PartId} plots {Math.Min(t.PartWPaper, t.PartHPaper):0.###}\" on its short side at 1:{t.Rung.Denominator}");
    }

    [Fact]
    public void A_tighter_legibility_floor_holds_a_finer_scale()
    {
        var loose = Plan(new PlateLayerOptions { MinTypicalPartPaperSize = 0.4 });
        var tight = Plan(new PlateLayerOptions { MinTypicalPartPaperSize = 2.0 });

        var loosest = loose.Pages.SelectMany(p => p.Tiles).Max(t => t.Rung.Denominator);
        var tightest = tight.Pages.SelectMany(p => p.Tiles).Max(t => t.Rung.Denominator);
        Assert.True(tightest <= loosest, $"tight floor gave 1:{tightest}, loose floor gave 1:{loosest}");
    }

    [Fact]
    public void Ties_go_to_the_finer_scale()
    {
        // Two rungs reaching the same plate count must resolve to the finer one; the coarser
        // achieves nothing and only shrinks the drawing.
        var opt = new PlateLayerOptions();
        var plan = Plan(opt);

        foreach (var group in plan.Pages.GroupBy(p => p.Material))
        {
            var chosen = group.First().Tiles[0].Rung;
            var pages = group.Count();
            var finer = ScaleLadder.Rungs
                .Where(r => r.Denominator < chosen.Denominator)
                .OrderByDescending(r => r.Denominator);

            foreach (var rung in finer)
            {
                var parts = group.SelectMany(p => p.Tiles)
                    .Select(t => new Part(t.PartId, t.Material, Array.Empty<Curve2D>(),
                        new BBox(0, 0, t.WModel, t.HModel), t.Label))
                    .ToList();
                var tiles = parts.Select(p => ScaleLadder.MakeTile(p, rung, opt)).ToList();
                if (tiles.Any(t => t.WPaper > opt.DrawableArea.Width || t.HPaper > opt.DrawableArea.Height)) continue;
                Assert.True(ShelfPacker.Pack(tiles, opt).Count > pages,
                    $"{group.Key}: 1:{rung.Denominator} would also reach {pages} plate(s); 1:{chosen.Denominator} is needlessly coarse.");
            }
        }
    }

    [Fact]
    public void Coarsening_is_reported_not_silent()
    {
        var plan = Plan(new PlateLayerOptions());
        Assert.Contains(plan.Warnings, w => w.Contains("coarsened"));
    }
}
