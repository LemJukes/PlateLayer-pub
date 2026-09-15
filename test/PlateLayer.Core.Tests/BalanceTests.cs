namespace PlateLayer.Core.Tests;

public class BalanceTests
{
    private static List<Curve2D> Parts(int count, double w, double h, string layer = "rPLY_3-4")
        => Enumerable.Range(0, count)
            .Select(i => new Curve2D($"p{i:000}", layer, CurveKind.Polyline,
                new BBox(0, i * (h + 20), w, i * (h + 20) + h), true))
            .ToList();

    private static RunPlan Plan(IEnumerable<Curve2D> curves, PlateLayerOptions opt)
    {
        var g = Grouping.Group(curves.ToList(), opt);
        return PlanBuilder.Build(g.Parts, opt, g.Warnings);
    }

    [Fact]
    public void The_remainder_no_longer_lands_alone_on_the_last_plate()
    {
        // Thirteen identical parts over two plates came out twelve and one before balancing.
        var greedy = Plan(Parts(13, 94, 42), new PlateLayerOptions { BalancePlates = false });
        var balanced = Plan(Parts(13, 94, 42), new PlateLayerOptions());

        Assert.Equal(greedy.TotalPages, balanced.TotalPages);        // costs nothing in plates

        var greedySpread = greedy.Pages.Max(p => p.Tiles.Count) - greedy.Pages.Min(p => p.Tiles.Count);
        var balancedSpread = balanced.Pages.Max(p => p.Tiles.Count) - balanced.Pages.Min(p => p.Tiles.Count);
        Assert.True(balancedSpread < greedySpread,
            $"part spread across plates did not improve: {greedySpread} -> {balancedSpread}");
        Assert.True(balancedSpread <= 1, $"plates differ by {balancedSpread} parts");
    }

    [Fact]
    public void Fill_is_evened_out_not_just_part_count()
    {
        var balanced = Plan(Parts(13, 94, 42), new PlateLayerOptions());
        var spread = balanced.Pages.Max(p => p.FillFraction) - balanced.Pages.Min(p => p.FillFraction);
        Assert.True(spread < 0.25, $"fill still varies by {spread:P0} between plates");
    }

    [Fact]
    public void Balancing_never_costs_a_plate()
    {
        foreach (var n in new[] { 2, 3, 5, 7, 9, 13, 17, 25, 40 })
        {
            var off = Plan(Parts(n, 94, 42), new PlateLayerOptions { BalancePlates = false }).TotalPages;
            var on = Plan(Parts(n, 94, 42), new PlateLayerOptions()).TotalPages;
            Assert.True(on <= off, $"{n} parts: balancing turned {off} plates into {on}");
        }
    }

    [Fact]
    public void Every_part_survives_balancing()
    {
        foreach (var n in new[] { 3, 13, 26 })
        {
            var plan = Plan(Parts(n, 94, 42), new PlateLayerOptions());
            Assert.Equal(n, plan.TotalParts);
            Assert.Equal(n, plan.Pages.SelectMany(p => p.Tiles).Select(t => t.PartId).Distinct().Count());
        }
    }

    [Fact]
    public void Balanced_plates_still_respect_the_drawable_area_and_do_not_overlap()
    {
        var opt = new PlateLayerOptions();
        foreach (var page in Plan(Parts(13, 94, 42), opt).Pages)
        {
            var boxes = page.Tiles
                .Select(t => new BBox(t.OriginX, t.OriginY, t.OriginX + t.WPaper, t.OriginY + t.HPaper))
                .ToList();

            foreach (var b in boxes)
            {
                Assert.True(b.MinX >= opt.DrawableArea.MinX - 1e-6 && b.MaxX <= opt.DrawableArea.MaxX + 1e-6);
                Assert.True(b.MinY >= opt.DrawableArea.MinY - 1e-6 && b.MaxY <= opt.DrawableArea.MaxY + 1e-6);
            }

            for (var i = 0; i < boxes.Count; i++)
                for (var j = i + 1; j < boxes.Count; j++)
                    Assert.False(boxes[i].MinX + 1e-9 < boxes[j].MaxX && boxes[j].MinX + 1e-9 < boxes[i].MaxX &&
                                 boxes[i].MinY + 1e-9 < boxes[j].MaxY && boxes[j].MinY + 1e-9 < boxes[i].MaxY,
                                 $"Plate {page.Index}: tiles overlap after balancing");
        }
    }

    [Fact]
    public void Balancing_is_deterministic()
    {
        var a = PlanBuilder.ToJson(Plan(Parts(13, 94, 42), new PlateLayerOptions()));
        var b = PlanBuilder.ToJson(Plan(Parts(13, 94, 42), new PlateLayerOptions()));
        Assert.Equal(a, b);
    }

    [Fact]
    public void A_single_plate_material_is_left_alone()
    {
        var opt = new PlateLayerOptions();
        var plan = Plan(Parts(3, 94, 42), opt);
        Assert.Equal(1, plan.TotalPages);
    }

    [Fact]
    public void The_parts_per_plate_cap_still_holds_after_balancing()
    {
        var opt = new PlateLayerOptions { MaxPartsPerPage = 4 };
        foreach (var page in Plan(Parts(13, 94, 42), opt).Pages)
            Assert.True(page.Tiles.Count <= 4, $"Plate {page.Index} carries {page.Tiles.Count}");
    }

    [Fact]
    public void The_reference_file_is_unchanged_by_balancing()
    {
        // Every material there already fits one plate, so there is nothing to balance.
        var off = Plan(SampleCutFile.Curves, new PlateLayerOptions { BalancePlates = false });
        var on = Plan(SampleCutFile.Curves, new PlateLayerOptions());
        Assert.Equal(PlanBuilder.ToJson(off), PlanBuilder.ToJson(on));
    }
}

public class FillReportingTests
{
    [Fact]
    public void Every_plate_reports_a_fill_fraction()
    {
        var opt = new PlateLayerOptions();
        var g = Grouping.Group(SampleCutFile.Curves, opt);
        foreach (var page in PlanBuilder.Build(g.Parts, opt, g.Warnings).Pages)
        {
            Assert.True(page.FillFraction > 0, $"Plate {page.Index} reports no fill");
            Assert.True(page.FillFraction <= 1.0 + 1e-9, $"Plate {page.Index} reports {page.FillFraction:P0}");
        }
    }

    [Fact]
    public void A_sparse_plate_is_named_in_the_run_summary()
    {
        // Two small parts in a material of their own: one plate, mostly white space, and no
        // arrangement or scale fixes it — splitting them across two plates is worse. Say so.
        var opt = new PlateLayerOptions();
        var curves = new Curve2D[]
        {
            new("s0", "rPLY_3-4", CurveKind.Polyline, new BBox(0,  0, 18, 12), true),
            new("s1", "rPLY_3-4", CurveKind.Polyline, new BBox(0, 30, 18, 42), true),
        };
        var g = Grouping.Group(curves, opt);
        var plan = PlanBuilder.Build(g.Parts, opt, g.Warnings);

        Assert.Contains(plan.Warnings, w => w.Contains("white space"));
    }

    [Fact]
    public void A_well_filled_plate_produces_no_such_warning()
    {
        var opt = new PlateLayerOptions();
        var curves = Enumerable.Range(0, 6)
            .Select(i => new Curve2D($"p{i}", "rPLY_3-4", CurveKind.Polyline,
                new BBox(0, i * 60, 94, i * 60 + 42), true))
            .ToList();
        var g = Grouping.Group(curves, opt);
        var plan = PlanBuilder.Build(g.Parts, opt, g.Warnings);

        Assert.DoesNotContain(plan.Warnings, w => w.Contains("white space"));
    }
}
