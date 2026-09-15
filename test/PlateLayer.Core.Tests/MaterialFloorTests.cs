namespace PlateLayer.Core.Tests;

public class MaterialFloorTests
{
    private static List<Curve2D> Multi(int materials, int partsEach, double w = 40, double h = 20)
    {
        var list = new List<Curve2D>();
        for (var m = 0; m < materials; m++)
            for (var i = 0; i < partsEach; i++)
                list.Add(new($"m{m}p{i}", $"rPLY_{m + 1}-4", CurveKind.Polyline,
                    new BBox(m * 400, i * (h + 20), m * 400 + w, i * (h + 20) + h), true));
        return list;
    }

    private static RunPlan Plan(IEnumerable<Curve2D> curves, ScalePolicy policy, bool onePerPlate = true)
    {
        var opt = new PlateLayerOptions { Policy = policy, OneMaterialPerPlate = onePerPlate };
        var g = Grouping.Group(curves.ToList(), opt);
        return PlanBuilder.Build(g.Parts, opt, g.Warnings);
    }

    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 2)]
    [InlineData(8, 3)]
    [InlineData(4, 5)]
    public void One_plate_per_material_is_a_floor_no_policy_crosses(int materials, int partsEach)
    {
        // The case this exists for: eight materials, eight sheets, and Minimum changed nothing
        // because it never could. The floor is the material split, not the scale.
        var curves = Multi(materials, partsEach);
        foreach (var policy in new[] { ScalePolicy.Balanced, ScalePolicy.FewestSheets })
            Assert.True(Plan(curves, policy).TotalPages >= materials,
                $"{policy} produced fewer plates than there are materials");

        Assert.Equal(materials, Plan(curves, ScalePolicy.FewestSheets).TotalPages);
    }

    [Fact]
    public void The_floor_is_named_when_it_is_what_is_holding()
    {
        var plan = Plan(Multi(8, 2), ScalePolicy.FewestSheets);
        Assert.Contains(plan.Warnings, w => w.Contains("one per material"));
    }

    [Fact]
    public void The_floor_is_not_named_when_something_else_is_holding()
    {
        // Two materials over four plates: the material split is not the binding constraint.
        var plan = Plan(Multi(2, 20, 94, 42), ScalePolicy.Balanced);
        Assert.True(plan.TotalPages > 2);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("one per material"));
    }

    [Fact]
    public void Minimum_still_works_below_the_floor()
    {
        // Two materials, twenty parts each: Balanced needs four plates, Minimum reaches the floor.
        var curves = Multi(2, 20, 94, 42);
        Assert.True(Plan(curves, ScalePolicy.Balanced).TotalPages > 2);
        Assert.Equal(2, Plan(curves, ScalePolicy.FewestSheets).TotalPages);
    }

    [Fact]
    public void Mixing_materials_is_the_only_thing_that_goes_below_the_floor()
    {
        // Recorded so the trade-off is explicit, not so it becomes the default: it collapses eight
        // plates to one and throws away the per-material grouping the tool exists to produce.
        Assert.Equal(1, Plan(Multi(8, 2), ScalePolicy.FewestSheets, onePerPlate: false).TotalPages);
        Assert.True(new PlateLayerOptions().OneMaterialPerPlate);
    }

    [Fact]
    public void The_dry_run_states_the_material_count()
    {
        var report = PlanBuilder.ToReport(Plan(Multi(8, 2), ScalePolicy.FewestSheets));
        Assert.Contains("8 material(s)", report);
    }
}
