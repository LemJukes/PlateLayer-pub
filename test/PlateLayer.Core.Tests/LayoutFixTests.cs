namespace PlateLayer.Core.Tests;

/// <summary>
/// Regressions from the first test plot (see RUN_SHEET Pass 0.1). Both faults were invisible to
/// the earlier tests because those only checked that tiles fit — not what was drawn inside them.
/// </summary>
public class LayoutFixTests
{
    private static (RunPlan Plan, PlateLayerOptions Opt) RealPlan(PlateLayerOptions? opt = null)
    {
        opt ??= new PlateLayerOptions();
        var g = Grouping.Group(SampleCutFile.Curves, opt);
        return (PlanBuilder.Build(g.Parts, opt, g.Warnings), opt);
    }

    private static IEnumerable<Tile> AllTiles(RunPlan plan) => plan.Pages.SelectMany(p => p.Tiles);

    [Fact]
    public void Part_outline_never_sits_on_the_viewport_frame()
    {
        var (plan, opt) = RealPlan();
        Assert.True(opt.ViewportMargin > 0);

        foreach (var t in AllTiles(plan))
        {
            var vpMinX = t.ViewportOrigin.X;
            var vpMinY = t.ViewportOrigin.Y;
            var vpMaxX = vpMinX + t.ViewportW;
            var vpMaxY = vpMinY + t.ViewportH;
            var part = t.PartRect;

            Assert.True(part.MinX > vpMinX + 1e-9, $"{t.PartId} left edge on the frame");
            Assert.True(part.MinY > vpMinY + 1e-9, $"{t.PartId} bottom edge on the frame");
            Assert.True(part.MaxX < vpMaxX - 1e-9, $"{t.PartId} right edge on the frame");
            Assert.True(part.MaxY < vpMaxY - 1e-9, $"{t.PartId} top edge on the frame");
        }
    }

    [Fact]
    public void Margin_at_scale_stays_well_inside_the_gap_between_parts_in_the_source()
    {
        // A cut file stacks its parts a few inches apart. Revealing more model space than half
        // that would bring a neighbouring part into the viewport.
        var (plan, opt) = RealPlan();
        foreach (var t in AllTiles(plan))
        {
            var modelRevealed = opt.ViewportMargin * t.Rung.Denominator;
            Assert.True(modelRevealed < 4.2 / 2,
                $"{t.PartId} at 1:{t.Rung.Denominator} reveals {modelRevealed}\" of model space per side.");
        }
    }

    [Fact]
    public void Y_dimension_text_stays_on_the_sheet()
    {
        var (plan, opt) = RealPlan();
        foreach (var t in AllTiles(plan))
        {
            // Horizontal text, centred on a dimension line DimLineOffset outside the part.
            var dimLineX = t.PartRect.MinX - t.DimOffsetY;
            var halfText = TextMetrics.SafeWidth(ArchFormat.Inches(t.HModel), opt) / 2.0;
            Assert.True(dimLineX - halfText >= opt.DrawableArea.MinX - 1e-9,
                $"{t.PartId}: '{ArchFormat.Inches(t.HModel)}' starts at {dimLineX - halfText:0.###}, " +
                $"drawable begins at {opt.DrawableArea.MinX}.");
        }
    }

    [Fact]
    public void X_dimension_text_stays_on_the_sheet()
    {
        var (plan, opt) = RealPlan();
        foreach (var t in AllTiles(plan))
        {
            var half = TextMetrics.SafeWidth(ArchFormat.Inches(t.WModel), opt) / 2.0;
            var centre = t.PartRect.Center.X;
            Assert.True(centre - half >= opt.DrawableArea.MinX - 1e-9, $"{t.PartId} X dim text off the left edge");
            Assert.True(centre + half <= opt.DrawableArea.MaxX + 1e-9, $"{t.PartId} X dim text off the right edge");
        }
    }

    [Fact]
    public void Dimension_lines_themselves_stay_on_the_sheet()
    {
        var (plan, opt) = RealPlan();
        foreach (var t in AllTiles(plan))
        {
            Assert.True(t.PartRect.MinY - t.DimOffsetX >= opt.DrawableArea.MinY - 1e-9,
                $"{t.PartId} X dimension line below the drawable area");
            Assert.True(t.PartRect.MinX - t.DimOffsetY >= opt.DrawableArea.MinX - 1e-9,
                $"{t.PartId} Y dimension line left of the drawable area");
        }
    }

    [Fact]
    public void Label_row_stays_on_the_sheet()
    {
        var (plan, opt) = RealPlan();
        foreach (var t in AllTiles(plan))
        {
            var top = t.ViewportOrigin.Y + t.ViewportH + opt.LabelTextHeight + opt.TextPad * 0.4;
            Assert.True(top <= opt.DrawableArea.MaxY + 1e-9, $"{t.PartId} label above the drawable area");
        }
    }

    [Fact]
    public void Each_plate_is_centred_in_the_drawable_area()
    {
        var (plan, opt) = RealPlan();
        foreach (var page in plan.Pages)
        {
            var minX = page.Tiles.Min(t => t.OriginX);
            var maxX = page.Tiles.Max(t => t.OriginX + t.WPaper);
            var minY = page.Tiles.Min(t => t.OriginY);
            var maxY = page.Tiles.Max(t => t.OriginY + t.HPaper);

            Assert.Equal(minX - opt.DrawableArea.MinX, opt.DrawableArea.MaxX - maxX, 6);
            Assert.Equal(minY - opt.DrawableArea.MinY, opt.DrawableArea.MaxY - maxY, 6);
        }
    }

    [Fact]
    public void Y_dimension_text_never_lands_on_the_part_outline()
    {
        var (plan, opt) = RealPlan();
        foreach (var t in AllTiles(plan))
        {
            var dimLineX = t.PartRect.MinX - t.DimOffsetY;
            var halfText = TextMetrics.SafeWidth(ArchFormat.Inches(t.HModel), opt) / 2.0;
            Assert.True(dimLineX + halfText + opt.TextClearance <= t.PartRect.MinX + 1e-9,
                $"{t.PartId}: '{ArchFormat.Inches(t.HModel)}' overlaps the part's left edge by " +
                $"{dimLineX + halfText - t.PartRect.MinX:0.###}\".");
        }
    }

    [Fact]
    public void X_dimension_text_never_lands_on_the_part_outline()
    {
        var (plan, opt) = RealPlan();
        foreach (var t in AllTiles(plan))
            Assert.True(t.PartRect.MinY - t.DimOffsetX + opt.DimTextHeight <= t.PartRect.MinY - 1e-9,
                $"{t.PartId} X dimension text overlaps the part's bottom edge");
    }

    [Fact]
    public void Clearance_holds_across_the_whole_safety_band()
    {
        // The code promises the layout survives a font up to TextWidthSafety wider than measured.
        // Check exactly that promise — not the raw estimate, which the layout itself is built from
        // and so could never fail against.
        var opt = new PlateLayerOptions();
        var g = Grouping.Group(SampleCutFile.Curves, opt);
        var plan = PlanBuilder.Build(g.Parts, opt, g.Warnings);

        var pessimistic = opt.TextWidthFactor * opt.TextWidthSafety;
        foreach (var t in plan.Pages.SelectMany(p => p.Tiles))
        {
            var half = TextMetrics.Width(ArchFormat.Inches(t.HModel), opt.DimTextHeight, pessimistic) / 2.0;
            Assert.True(t.PartRect.MinX - t.DimOffsetY + half <= t.PartRect.MinX + 1e-9,
                $"{t.PartId}: at the {opt.TextWidthSafety:0.##}x safety band, '{ArchFormat.Inches(t.HModel)}' " +
                $"overlaps the part by {t.PartRect.MinX - t.DimOffsetY + half - t.PartRect.MinX:0.###}\".");
        }
    }

    [Fact]
    public void A_bigger_text_height_buys_a_wider_gutter_rather_than_overflowing()
    {
        var small = new PlateLayerOptions { DimTextHeight = 0.125 };
        var large = new PlateLayerOptions { DimTextHeight = 0.375 };

        var a = ScaleLadder.MakeTile("p", "01", "PLY", 94.3125, 42, 0, 0, new ScaleRung(8, "x"), small);
        var b = ScaleLadder.MakeTile("p", "01", "PLY", 94.3125, 42, 0, 0, new ScaleRung(8, "x"), large);

        Assert.True(b.GutterLeft > a.GutterLeft);
        Assert.Equal(a.ViewportW, b.ViewportW, 9);   // the viewport itself is unaffected
    }
}
