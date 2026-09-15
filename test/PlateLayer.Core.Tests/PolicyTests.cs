namespace PlateLayer.Core.Tests;

public class PolicyTests
{
    private static List<Curve2D> Parts(int n, double w, double h) => Enumerable.Range(0, n)
        .Select(i => new Curve2D($"p{i:000}", "rPLY_3-4", CurveKind.Polyline,
            new BBox(0, i * (h + 20), w, i * (h + 20) + h), true))
        .ToList();

    private static RunPlan Plan(IEnumerable<Curve2D> curves, ScalePolicy policy)
    {
        var opt = new PlateLayerOptions { Policy = policy };
        var g = Grouping.Group(curves.ToList(), opt);
        return PlanBuilder.Build(g.Parts, opt, g.Warnings);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(25)]
    [InlineData(40)]
    public void Minimum_never_needs_more_sheets_than_balanced(int n)
    {
        var balanced = Plan(Parts(n, 94, 42), ScalePolicy.Balanced).TotalPages;
        var minimum = Plan(Parts(n, 94, 42), ScalePolicy.FewestSheets).TotalPages;
        Assert.True(minimum <= balanced, $"{n} parts: Minimum gave {minimum}, Balanced gave {balanced}");
    }

    [Fact]
    public void Minimum_actually_reduces_sheets_where_the_cap_was_binding()
    {
        // The finding that prompted this: 13 equal parts sat on 2 plates because of the 12-part
        // cap, not because of the scale. Coarsening alone could not touch it.
        Assert.Equal(2, Plan(Parts(13, 94, 42), ScalePolicy.Balanced).TotalPages);
        Assert.Equal(1, Plan(Parts(13, 94, 42), ScalePolicy.FewestSheets).TotalPages);
    }

    [Fact]
    public void Minimum_pays_for_it_in_drawing_size()
    {
        var balanced = Plan(Parts(13, 94, 42), ScalePolicy.Balanced).Pages[0].Tiles[0];
        var minimum = Plan(Parts(13, 94, 42), ScalePolicy.FewestSheets).Pages[0].Tiles[0];
        Assert.True(minimum.Rung.Denominator > balanced.Rung.Denominator,
            $"Minimum did not coarsen: 1:{balanced.Rung.Denominator} -> 1:{minimum.Rung.Denominator}");
    }

    [Fact]
    public void Minimum_still_respects_the_legibility_floor()
    {
        var opt = new PlateLayerOptions { Policy = ScalePolicy.FewestSheets, MinTypicalPartPaperSize = 0.5 };
        var g = Grouping.Group(Parts(40, 94, 42), opt);
        foreach (var t in PlanBuilder.Build(g.Parts, opt, g.Warnings).Pages.SelectMany(p => p.Tiles))
            Assert.True(Math.Min(t.PartWPaper, t.PartHPaper) >= opt.MinTypicalPartPaperSize - 1e-9,
                $"{t.PartId} plots {Math.Min(t.PartWPaper, t.PartHPaper):0.###}\" on its short side");
    }

    [Fact]
    public void Minimum_never_exceeds_the_viewport_hard_cap()
    {
        foreach (var page in Plan(Parts(80, 94, 42), ScalePolicy.FewestSheets).Pages)
            Assert.True(page.Tiles.Count <= PlateLayerOptions.ViewportHardCap,
                $"Plate {page.Index} carries {page.Tiles.Count} viewports");
    }

    [Fact]
    public void Finest_never_coarsens()
    {
        var finest = Plan(Parts(13, 94, 42), ScalePolicy.FinestThatFits);
        Assert.DoesNotContain(finest.Warnings, w => w.Contains("coarsened"));
    }

    [Fact]
    public void Balanced_is_the_default()
        => Assert.Equal(ScalePolicy.Balanced, new PlateLayerOptions().Policy);

    [Fact]
    public void The_cap_is_named_as_the_limit_when_it_is_the_limit()
    {
        // Answering "why so many sheets" on the page, rather than leaving it to be guessed at.
        var plan = Plan(Parts(13, 94, 42), ScalePolicy.Balanced);
        Assert.Contains(plan.Warnings, w => w.Contains("parts-per-plate cap"));
    }

    [Fact]
    public void No_such_note_when_the_cap_is_not_the_limit()
    {
        var opt = new PlateLayerOptions();
        var g = Grouping.Group(SampleCutFile.Curves, opt);
        var plan = PlanBuilder.Build(g.Parts, opt, g.Warnings);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("parts-per-plate cap"));
    }

    [Fact]
    public void The_older_boolean_setting_still_maps_onto_a_policy()
    {
        Assert.Equal(ScalePolicy.FinestThatFits, new PlateLayerOptions { CoarsenForDensity = false }.Policy);
        Assert.Equal(ScalePolicy.Balanced, new PlateLayerOptions { CoarsenForDensity = true }.Policy);
        Assert.True(new PlateLayerOptions { Policy = ScalePolicy.FewestSheets }.CoarsenForDensity);
    }

    [Fact]
    public void Policy_keywords_round_trip()
    {
        foreach (var p in Enum.GetValues<ScalePolicy>())
            Assert.Equal(p, Prompts.PolicyFor(Prompts.PolicyKeyword(p)));
    }
}
