namespace PlateLayer.Core.Tests;

public class ScaleTests
{
    [Fact]
    public void Largest_rung_that_fits_is_chosen_not_merely_a_rung_that_fits()
    {
        var opt = new PlateLayerOptions();               // drawable 15.75 x 10.25
        // 95" x 12" part. At 1:8 the tile is 95/8 + 0.55 = 12.425 wide, 12/8 + 0.62 = 2.12 high. Fits.
        // At 1:4 it would be 24.3 wide. Does not fit. So 1:8 is the answer, computed here by hand.
        var rung = ScaleLadder.Choose(95.0, 12.0, opt);
        Assert.NotNull(rung);
        Assert.Equal(8, rung!.Value.Denominator);
    }

    [Fact]
    public void CustomScale_direction_is_paper_over_model()
    {
        var rung = new ScaleRung(8, "1 1/2\"=1'");
        Assert.Equal(0.125, rung.PaperPerModel, 9);      // 96" of timber draws 12" on paper
        Assert.Equal(12.0, 96.0 * rung.PaperPerModel, 9);
    }

    [Fact]
    public void A_part_too_large_for_the_floor_returns_null_rather_than_throwing()
    {
        var opt = new PlateLayerOptions { ScaleFloorDenominator = 96 };
        Assert.Null(ScaleLadder.Choose(100_000, 100_000, opt));
    }

    [Fact]
    public void Every_real_part_lands_on_the_ladder_well_above_the_floor()
    {
        var opt = new PlateLayerOptions();
        var parts = Grouping.Group(SampleCutFile.Curves, opt).Parts;
        foreach (var p in parts)
        {
            var rung = ScaleLadder.Choose(p.WModel, p.HModel, opt);
            Assert.NotNull(rung);
            Assert.True(rung!.Value.Denominator <= 16,
                $"{p.Id} chose 1:{rung.Value.Denominator}; the sample file should never need coarser than 1:16.");
        }
    }
}

public class PlanTests
{
    private static RunPlan RealPlan(PlateLayerOptions? opt = null)
    {
        opt ??= new PlateLayerOptions();
        var g = Grouping.Group(SampleCutFile.Curves, opt);
        return PlanBuilder.Build(g.Parts, opt, g.Warnings);
    }

    [Fact]
    public void Every_part_is_placed_or_named_in_an_exclusion()
    {
        var plan = RealPlan();
        Assert.Equal(SampleCutFile.ExpectedParts, plan.TotalParts + plan.Excluded.Count);
    }

    [Fact]
    public void A_plate_holds_exactly_one_material()
    {
        foreach (var page in RealPlan().Pages)
            Assert.Single(page.Tiles.Select(t => t.Material).Distinct());
    }

    [Fact]
    public void Page_numbering_is_contiguous_from_one()
    {
        var plan = RealPlan();
        Assert.Equal(Enumerable.Range(1, plan.TotalPages), plan.Pages.Select(p => p.Index));
    }

    [Fact]
    public void No_tile_escapes_the_drawable_area()
    {
        var opt = new PlateLayerOptions();
        foreach (var page in RealPlan(opt).Pages)
            foreach (var t in page.Tiles)
            {
                Assert.True(t.OriginX >= opt.DrawableArea.MinX - 1e-6, $"{t.PartId} left  {t.OriginX}");
                Assert.True(t.OriginY >= opt.DrawableArea.MinY - 1e-6, $"{t.PartId} below {t.OriginY}");
                Assert.True(t.OriginX + t.WPaper <= opt.DrawableArea.MaxX + 1e-6, $"{t.PartId} right {t.OriginX + t.WPaper}");
                Assert.True(t.OriginY + t.HPaper <= opt.DrawableArea.MaxY + 1e-6, $"{t.PartId} above {t.OriginY + t.HPaper}");
            }
    }

    [Fact]
    public void No_two_tiles_on_a_plate_overlap()
    {
        foreach (var page in RealPlan().Pages)
        {
            var boxes = page.Tiles
                .Select(t => new BBox(t.OriginX, t.OriginY, t.OriginX + t.WPaper, t.OriginY + t.HPaper))
                .ToList();
            for (var i = 0; i < boxes.Count; i++)
                for (var j = i + 1; j < boxes.Count; j++)
                    Assert.False(Overlaps(boxes[i], boxes[j]),
                        $"Plate {page.Index}: {page.Tiles[i].PartId} overlaps {page.Tiles[j].PartId}");
        }
        static bool Overlaps(BBox a, BBox b) =>
            a.MinX + 1e-9 < b.MaxX && b.MinX + 1e-9 < a.MaxX &&
            a.MinY + 1e-9 < b.MaxY && b.MinY + 1e-9 < a.MaxY;
    }

    [Fact]
    public void Parts_per_page_cap_is_respected()
    {
        var opt = new PlateLayerOptions { MaxPartsPerPage = 3 };
        foreach (var page in RealPlan(opt).Pages)
            Assert.True(page.Tiles.Count <= 3, $"Plate {page.Index} has {page.Tiles.Count} tiles.");
    }

    [Fact]
    public void Sixty_one_parts_of_one_material_never_share_a_plate()
    {
        var opt = new PlateLayerOptions { MaxPartsPerPage = 500 };
        var curves = Enumerable.Range(0, 61)
            .Select(i => new Curve2D($"c{i}", "PLY", CurveKind.Polyline, new BBox(0, i * 10, 8, i * 10 + 6), true))
            .ToList();
        var plan = PlanBuilder.Build(Grouping.Group(curves, opt).Parts, opt);
        Assert.All(plan.Pages, p => Assert.True(p.Tiles.Count <= PlateLayerOptions.ViewportHardCap));
    }

    [Fact]
    public void Plan_is_byte_identical_across_runs()
    {
        Assert.Equal(PlanBuilder.ToJson(RealPlan()), PlanBuilder.ToJson(RealPlan()));
    }
}

public class ArchFormatTests
{
    [Theory]
    [InlineData(48.0,     "4'-0\"")]
    [InlineData(94.3125,  "7'-10 5/16\"")]
    [InlineData(11.125,   "11 1/8\"")]
    // Changed deliberately: architectural units drop the leading zero on a bare fraction, which is
    // what AutoCAD writes and what a drawing reads. The old expectation of 0 1/2" was wrong.
    [InlineData(0.5,      "1/2\"")]
    [InlineData(12.0,     "1'-0\"")]
    [InlineData(23.25,    "1'-11 1/4\"")]
    public void Inches_render_as_architectural_feet_and_inches(double value, string expected)
        => Assert.Equal(expected, ArchFormat.Inches(value));
}
