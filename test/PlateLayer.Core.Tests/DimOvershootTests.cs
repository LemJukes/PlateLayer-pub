namespace PlateLayer.Core.Tests;

/// <summary>
/// When text and arrowheads will not fit between a dimension's extension lines, AutoCAD moves them
/// outside and the dimension draws wider — and taller — than the part it measures. On the small
/// parts whose dimensions overshoot ran straight into the neighbouring tile.
/// </summary>
public class DimOvershootTests
{
    private static Tile Tile(double wModel, double hModel, PlateLayerOptions opt, int denominator = 12)
        => ScaleLadder.MakeTile("p", "01", "rPLY_3-4", wModel, hModel, 0, 0,
                                new ScaleRung(denominator, "1\"=1'"), opt);

    /// <summary>How far the drawn dimension reaches past the part, on each axis.</summary>
    private static (double X, double Y) Overshoot(Tile t, PlateLayerOptions opt)
    {
        var arrow = opt.DimArrowSize;
        var xText = TextMetrics.SafeWidth(ArchFormat.Inches(t.WModel), opt);
        var x = 2 * arrow + (t.PartWPaper < xText ? xText / 2.0 : 0.0);
        var textBlock = opt.DimTextHeight * (opt.StackedFractions ? 1.5 : 1.0);
        var y = 2 * arrow + (t.PartHPaper < textBlock ? textBlock : 0.0);
        return (x, y);
    }

    [Fact]
    public void A_short_part_reserves_room_above_it_for_the_pushed_out_vertical_dimension()
    {
        // 2" tall at 1:12 is 0.167" of paper — nothing like enough for text plus two arrowheads,
        // so AutoCAD puts them outside and they reach above the part.
        var opt = new PlateLayerOptions();
        var tile = Tile(37.4375, 2.0, opt);

        Assert.True(tile.GutterTop > 0,
            "no room reserved above a part whose vertical dimension cannot fit between its extension lines");
        var (_, y) = Overshoot(tile, opt);
        Assert.True(tile.GutterTop + opt.ViewportMargin >= y, $"reserved {tile.GutterTop:0.###}\", needs {y:0.###}\"");
    }

    [Fact]
    public void A_tall_part_reserves_the_arrowheads_but_not_the_text()
    {
        // Changed deliberately from "reserves nothing". Predicting when AutoCAD keeps arrows between
        // the extension lines was wrong on a real plate — a part far taller than text-plus-arrows
        // still got its arrows pushed outside, into the title block. The arrow allowance is now
        // unconditional; only the text allowance is still conditional.
        var opt = new PlateLayerOptions();
        var roomy = Tile(48, 36, opt);
        var cramped = Tile(48, 2, opt);

        Assert.True(roomy.GutterTop > 0, "a tall part should still own its arrowhead allowance");
        Assert.True(cramped.GutterTop > roomy.GutterTop,
            "a part too short for its dimension text should reserve more than a tall one");
    }

    [Fact]
    public void A_narrow_part_reserves_room_either_side_for_the_pushed_out_horizontal_dimension()
    {
        var opt = new PlateLayerOptions();
        var tile = Tile(11.6875, 2.0, opt);   // 11 11/16" — text far wider than the part on paper

        var (x, _) = Overshoot(tile, opt);
        Assert.True(tile.GutterRight + opt.ViewportMargin >= x, $"right reserved {tile.GutterRight:0.###}\", needs {x:0.###}\"");
        Assert.True(tile.GutterLeft + opt.ViewportMargin >= x, $"left reserved {tile.GutterLeft:0.###}\", needs {x:0.###}\"");
    }

    [Theory]
    [InlineData(37.4375, 2.0)]
    [InlineData(11.6875, 2.0)]
    [InlineData(46.875, 3.0)]
    [InlineData(94.3125, 42.0)]
    public void Nothing_a_dimension_draws_escapes_its_own_tile(double w, double h)
    {
        var opt = new PlateLayerOptions();
        var t = Tile(w, h, opt);
        var (ox, oy) = Overshoot(t, opt);

        var part = t.PartRect;                       // tile origin is 0,0 for an unplaced tile
        Assert.True(part.MinX - ox >= t.OriginX - 1e-9, "dimension reaches past the left edge of its tile");
        Assert.True(part.MaxX + ox <= t.OriginX + t.WPaper + 1e-9, "dimension reaches past the right edge of its tile");
        Assert.True(part.MinY - oy >= t.OriginY - 1e-9, "dimension reaches below its tile");
        Assert.True(part.MaxY + oy <= t.OriginY + t.HPaper + 1e-9, "dimension reaches above its tile");
    }

    [Fact]
    public void Small_parts_no_longer_crowd_their_neighbours()
    {
        var opt = new PlateLayerOptions { Policy = ScalePolicy.FewestSheets };
        var g = Grouping.Group(SampleCutFile.Curves, opt);
        var plan = PlanBuilder.Build(g.Parts, opt, g.Warnings);

        foreach (var page in plan.Pages)
        {
            // Tiles already do not overlap; what matters is that what each tile DRAWS stays inside it.
            foreach (var t in page.Tiles)
            {
                var (ox, oy) = Overshoot(t, opt);
                var part = t.PartRect;
                Assert.True(part.MinX - ox >= t.OriginX - 1e-9, $"{t.PartId}: dimension escapes left");
                Assert.True(part.MaxX + ox <= t.OriginX + t.WPaper + 1e-9, $"{t.PartId}: dimension escapes right");
                Assert.True(part.MinY - oy >= t.OriginY - 1e-9, $"{t.PartId}: dimension escapes below");
                Assert.True(part.MaxY + oy <= t.OriginY + t.HPaper + 1e-9, $"{t.PartId}: dimension escapes above");
            }
        }
    }

    [Fact]
    public void A_bigger_arrowhead_buys_more_room()
    {
        var small = Tile(37.4375, 2.0, new PlateLayerOptions { DimArrowSize = 0.0625 });
        var large = Tile(37.4375, 2.0, new PlateLayerOptions { DimArrowSize = 0.25 });
        Assert.True(large.GutterTop > small.GutterTop);
    }
}

public class StackedFractionTests
{
    [Theory]
    [InlineData(48.0, "4'-0\"")]
    [InlineData(94.3125, "7'-10\\S5/16;\"")]
    [InlineData(11.6875, "11\\S11/16;\"")]
    [InlineData(41.75, "3'-5\\S3/4;\"")]
    [InlineData(0.6875, "\\S11/16;\"")]
    [InlineData(2.0, "2\"")]
    public void Fractions_are_written_as_MText_stacks(double value, string expected)
        => Assert.Equal(expected, ArchFormat.InchesMText(value));

    [Theory]
    [InlineData(94.3125, "7'-10 5/16\"")]
    [InlineData(11.6875, "11 11/16\"")]
    [InlineData(0.6875, "11/16\"")]
    public void The_plain_form_is_unchanged_for_reports_and_logs(double value, string expected)
        => Assert.Equal(expected, ArchFormat.Inches(value));

    [Fact]
    public void Architectural_units_drop_the_leading_zero_on_a_bare_fraction()
    {
        // 0 1/2" is not how a drawing reads, and not what AutoCAD writes.
        Assert.Equal("1/2\"", ArchFormat.Inches(0.5));
        Assert.Equal("\\S1/2;\"", ArchFormat.InchesMText(0.5));
    }

    [Fact]
    public void A_whole_measurement_carries_no_stack_markup_at_all()
    {
        foreach (var v in new[] { 12.0, 48.0, 2.0, 96.0 })
            Assert.DoesNotContain("\\S", ArchFormat.InchesMText(v), StringComparison.Ordinal);
    }

    [Fact]
    public void The_option_chooses_between_the_two_forms()
    {
        Assert.Equal("7'-10\\S5/16;\"", ArchFormat.Inches(94.3125, new PlateLayerOptions()));
        Assert.Equal("7'-10 5/16\"", ArchFormat.Inches(94.3125, new PlateLayerOptions { StackedFractions = false }));
        Assert.True(new PlateLayerOptions().StackedFractions);
    }

    [Fact]
    public void Every_stack_is_closed()
    {
        // An unterminated \S swallows the rest of the string when AutoCAD renders it.
        for (var sixteenths = 1; sixteenths <= 16 * 8; sixteenths++)
        {
            var text = ArchFormat.InchesMText(sixteenths / 16.0);
            Assert.Equal(text.Split("\\S").Length - 1, text.Count(c => c == ';'));
        }
    }

    [Fact]
    public void Tile_width_is_budgeted_from_the_plain_form()
    {
        // A stacked fraction is narrower than the flat one, so estimating from the plain string
        // over-reserves. That is the safe direction and is deliberate.
        Assert.True(ArchFormat.Inches(94.3125).Length > ArchFormat.InchesMText(94.3125).Replace("\\S", "").Replace(";", "").Length);
    }
}
