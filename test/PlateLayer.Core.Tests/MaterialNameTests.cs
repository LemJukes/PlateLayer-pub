namespace PlateLayer.Core.Tests;

public class MaterialNameTests
{
    private static PlateLayerOptions Opt() => new();

    [Theory]
    [InlineData("rPLY_3-4", "3/4\" Plywood | rPLY_3-4")]
    [InlineData("rPLY_1-2", "1/2\" Plywood | rPLY_1-2")]
    [InlineData("rPLY_1-4", "1/4\" Plywood | rPLY_1-4")]
    public void Layers_in_the_reference_file_expand(string layer, string expected)
        => Assert.Equal(expected, MaterialNames.Describe(layer, Opt()));

    [Fact]
    public void A_trailing_qualifier_is_carried_through()
        => Assert.Equal("1/2\" Bending Plywood | rPLY_1-2BEND",
                        MaterialNames.Describe("rPLY_1-2BEND", Opt()));

    [Fact]
    public void An_unknown_qualifier_is_passed_through_rather_than_dropped()
        => Assert.Equal("3/4\" FR Plywood | rPLY_3-4FR",
                        MaterialNames.Describe("rPLY_3-4FR", Opt()));

    [Theory]
    [InlineData("rPLY_1", "1\" Plywood | rPLY_1")]
    [InlineData("rPLY_1-1-2", "1 1/2\" Plywood | rPLY_1-1-2")]
    public void Whole_and_mixed_thicknesses_parse(string layer, string expected)
        => Assert.Equal(expected, MaterialNames.Describe(layer, Opt()));

    [Fact]
    public void A_code_with_no_thickness_still_names_the_material()
        => Assert.Equal("Plywood | rPLY", MaterialNames.Describe("rPLY", Opt()));

    [Theory]
    [InlineData("rPOP_3-4")]     // stick/board goods: never a sheet part, never expanded
    [InlineData("rSTL_1-8")]     // code not in the table
    [InlineData("rOJECTS")]      // not a material layer at all
    [InlineData("Defpoints")]
    [InlineData("")]
    public void An_unknown_code_is_never_guessed_at(string layer)
    {
        // A wrong expansion on a cut sheet is worse than none. Fall back to the bare layer name.
        Assert.Equal(layer, MaterialNames.Describe(layer, Opt()));
        Assert.Null(MaterialNames.Expand(layer, Opt()));
    }

    [Fact]
    public void The_layer_name_always_survives_the_translation()
    {
        // Whatever else happens, the string a person can search the cut file for must be present.
        var opt = Opt();
        foreach (var layer in new[] { "rPLY_3-4", "rPOP_3-4", "rSTL_1-8", "rPLY_1-2BEND" })
            Assert.Contains(layer, MaterialNames.Describe(layer, opt));
    }

    [Fact]
    public void Only_sheet_goods_are_seeded()
    {
        // Input is sheet goods by contract. Stick and board goods never reach a plate, so seeding
        // them would expand a layer that should never have been in the run in the first place.
        var opt = Opt();
        Assert.Equal(new[] { "PLY" }, opt.MaterialCodes.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void An_unrecognised_material_layer_is_reported_in_the_run_summary()
    {
        var opt = Opt();
        var curves = new Curve2D[]
        {
            new("a", "rPOP_3-4", CurveKind.Polyline, new BBox(0, 0, 40, 20), true),
        };
        var g = Grouping.Group(curves, opt);
        var plan = PlanBuilder.Build(g.Parts, opt, g.Warnings);

        Assert.Contains(plan.Warnings, w => w.Contains("rPOP_3-4") && w.Contains("not a recognised sheet-good code"));
    }

    [Fact]
    public void A_recognised_material_produces_no_such_warning()
    {
        var opt = Opt();
        var g = Grouping.Group(SampleCutFile.Curves, opt);
        var plan = PlanBuilder.Build(g.Parts, opt, g.Warnings);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("not a recognised sheet-good code"));
    }

    [Fact]
    public void Codes_added_at_run_time_take_effect()
    {
        var opt = Opt();
        opt.MaterialCodes["MDF"] = "MDF";
        Assert.Equal("3/4\" MDF | rMDF_3-4", MaterialNames.Describe("rMDF_3-4", opt));
    }

    [Fact]
    public void A_shop_that_does_not_prefix_its_layers_still_works()
    {
        var opt = Opt();
        opt.LayerPrefix = "";
        Assert.Equal("3/4\" Plywood | PLY_3-4", MaterialNames.Describe("PLY_3-4", opt));
    }
}

public class AnnotationToggleTests
{
    private static RunPlan Plan(PlateLayerOptions opt)
    {
        var g = Grouping.Group(SampleCutFile.Curves, opt);
        return PlanBuilder.Build(g.Parts, opt, g.Warnings);
    }

    [Fact]
    public void Labels_and_scale_notes_are_off_by_default()
    {
        var opt = new PlateLayerOptions();
        Assert.False(opt.ShowPartLabels);
        Assert.False(opt.ShowScaleNotes);
    }

    [Fact]
    public void With_labels_off_the_top_band_is_only_the_arrowhead_allowance()
    {
        // Changed deliberately: the band used to be exactly zero with labels off. It now always
        // carries the arrowhead allowance, because AutoCAD puts arrows outside the extension lines
        // more often than the old prediction expected — and one of them reached into the title
        // block. What it must not carry is the label row.
        var opt = new PlateLayerOptions();
        var arrowsOnly = 2 * opt.DimArrowSize + opt.TextPad - opt.ViewportMargin;

        foreach (var t in Plan(opt).Pages.SelectMany(p => p.Tiles))
            Assert.True(t.GutterTop <= arrowsOnly + 1e-9,
                $"{t.PartId} reserves {t.GutterTop:0.###}\" above, more than the arrowhead allowance");
    }

    [Fact]
    public void With_labels_on_the_top_band_comes_back()
    {
        var opt = new PlateLayerOptions { ShowPartLabels = true };
        foreach (var t in Plan(opt).Pages.SelectMany(p => p.Tiles))
            Assert.True(t.GutterTop > 0);
    }

    [Fact]
    public void Dropping_the_label_row_never_costs_height()
    {
        // Was "tiles must shrink". They no longer do on this file, because the arrowhead allowance
        // above a tile is now larger than the label row it replaced — so the label rides for free.
        // What still has to hold is that turning labels off never makes a plate bigger.
        var withLabels = Plan(new PlateLayerOptions { ShowPartLabels = true });
        var without = Plan(new PlateLayerOptions());

        var a = withLabels.Pages.SelectMany(p => p.Tiles).Sum(t => t.HPaper);
        var b = without.Pages.SelectMany(p => p.Tiles).Sum(t => t.HPaper);
        Assert.True(b <= a + 1e-9, $"tiles grew when labels were dropped: {a:0.###} -> {b:0.###}");
        Assert.True(without.TotalPages <= withLabels.TotalPages);
    }

    [Fact]
    public void The_label_collision_that_prompted_this_cannot_recur_while_labels_are_off()
    {
        // On a short part the vertical dimension text sits near the part's top edge, which is where
        // the label was drawn. With no label there is nothing there to hit.
        var opt = new PlateLayerOptions();
        foreach (var t in Plan(opt).Pages.SelectMany(p => p.Tiles))
        {
            var dimTextTop = t.PartRect.Center.Y + opt.DimTextHeight / 2.0;
            var tileTop = t.OriginY + t.HPaper;
            Assert.True(dimTextTop <= tileTop + 1e-9,
                $"{t.PartId}: dimension text reaches above its own tile");
        }
    }
}
