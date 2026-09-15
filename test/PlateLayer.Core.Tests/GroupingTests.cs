namespace PlateLayer.Core.Tests;

public class GroupingTests
{
    private static PlateLayerOptions Opt() => new();

    [Fact]
    public void The_sample_file_yields_its_documented_part_count()
    {
        var result = Grouping.Group(SampleCutFile.Curves, Opt());
        Assert.Equal(SampleCutFile.ExpectedParts, result.Parts.Count);
    }

    [Theory]
    [InlineData("rPLY_1-2", 5)]
    [InlineData("rPLY_1-4", 2)]
    [InlineData("rPLY_3-4", 6)]
    public void Sample_file_part_counts_per_material(string material, int expected)
    {
        var result = Grouping.Group(SampleCutFile.Curves, Opt());
        Assert.Equal(expected, result.Parts.Count(p => p.Material == material));
    }

    [Fact]
    public void Group_rectangles_on_rOJECTS_never_become_parts()
    {
        var result = Grouping.Group(SampleCutFile.Curves, Opt());
        Assert.DoesNotContain(result.Parts, p => p.Material == "rOJECTS");
    }

    [Fact]
    public void Sheet_outline_is_rejected_even_when_its_layer_is_not_excluded()
    {
        // Same geometry, but the group rectangles are on a part layer, so only the geometric
        // fallback can catch them.
        //
        // Expected 12, not 13, and that is the honest number. Two of the three sheet rectangles
        // hold 6 and 5 top-level loops and are rejected. The third holds exactly 2 —
        // geometrically indistinguishable from a part with two cutouts, which this drawing also
        // contains. Lowering SheetOutlineMinChildren to 2 would split those real parts.
        // The layer filter is the reliable path; this is the belt-and-braces one.
        var curves = SampleCutFile.Curves
            .Select(c => c.Layer == "rOJECTS" ? c with { Layer = "rPLY_1-2" } : c)
            .ToList();
        var opt = Opt();
        opt.ExcludedLayers.Remove("rOJECTS");

        var result = Grouping.Group(curves, opt);

        Assert.Equal(12, result.Parts.Count);
        Assert.Contains(result.Warnings, w => w.Contains("sheet outline"));
    }

    [Fact]
    public void A_cutout_joins_its_parent_rather_than_becoming_a_part()
    {
        var curves = new Curve2D[]
        {
            new("outer", "PLY", CurveKind.Polyline, new BBox(0, 0, 40, 20), true),
            new("hole",  "PLY", CurveKind.Polyline, new BBox(5, 5, 10, 10), true),
        };
        var parts = Grouping.Group(curves, Opt()).Parts;
        var part = Assert.Single(parts);
        Assert.Equal(1, part.CutoutCount);
        Assert.Equal(new BBox(0, 0, 40, 20), part.Outer);
    }

    [Fact]
    public void Parts_four_inches_apart_stay_separate()
    {
        var curves = new Curve2D[]
        {
            new("a", "PLY", CurveKind.Polyline, new BBox(0, 0,  40, 20), true),
            new("b", "PLY", CurveKind.Polyline, new BBox(0, 24, 40, 44), true),
        };
        Assert.Equal(2, Grouping.Group(curves, Opt()).Parts.Count);
    }

    [Fact]
    public void Overlapping_neighbours_merge_the_documented_limitation()
    {
        // An L-shaped part's bbox swallowing a disjoint neighbour is a known, accepted failure.
        // Asserted so the day it changes is a deliberate day.
        var curves = new Curve2D[]
        {
            new("ell",      "PLY", CurveKind.Polyline, new BBox(0, 0, 40, 40), true),
            new("neighbour","PLY", CurveKind.Polyline, new BBox(30, 30, 38, 38), true),
        };
        Assert.Single(Grouping.Group(curves, Opt()).Parts);
    }

    [Fact]
    public void Grouping_is_deterministic()
    {
        var a = Grouping.Group(SampleCutFile.Curves, Opt()).Parts.Select(p => p.Id).ToArray();
        var shuffled = SampleCutFile.Curves.Reverse().ToList();
        var b = Grouping.Group(shuffled, Opt()).Parts.Select(p => p.Id).ToArray();
        Assert.Equal(a, b);
    }
}
