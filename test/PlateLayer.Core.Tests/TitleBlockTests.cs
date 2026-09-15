namespace PlateLayer.Core.Tests;

public class TitleBlockTests
{
    private static PagePlan Page(string material = "rPLY_3-4", int index = 2, int tiles = 8)
    {
        var opt = new PlateLayerOptions();
        var list = Enumerable.Range(0, tiles)
            .Select(i => ScaleLadder.MakeTile($"p{i}", $"{i:00}", material, 40, 10, 0, 0, new ScaleRung(12, "1\"=1'"), opt))
            .ToList();
        return new PagePlan(index, material, list);
    }

    private static Dictionary<string, string> Values(PlateLayerOptions? opt = null) =>
        TitleBlock.Values(Page(), 3, opt ?? new PlateLayerOptions(), new DateTime(2026, 9, 14));

    [Fact]
    public void Operator_owned_fields_are_never_written()
    {
        // Project name, client, producer and the rest belong to whoever set up the drawing.
        // The plugin cannot know them and must not overwrite them, blank or otherwise.
        var written = Values().Keys;
        foreach (var tag in TitleBlock.OperatorOwnedTags)
            Assert.DoesNotContain(written, k => string.Equals(k, tag, StringComparison.Ordinal));
    }

    [Fact]
    public void Exactly_the_fields_the_plugin_can_know_are_written()
    {
        Assert.Equal(
            new[] { "#", "##", "#DRAWING_DATE", "$PIECE/PART_NAME", "$PLATE_NAME" },
            Values().Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Nothing_written_mentions_a_scale()
    {
        foreach (var v in Values().Values)
        {
            Assert.DoesNotContain("SCALE", v, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("=1'", v, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Nothing_written_states_a_part_count()
    {
        // "CNC Part Catalogue" legitimately contains the word; what must not appear is a count.
        foreach (var v in Values().Values)
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(v, @"\d+\s*parts?\b",
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                         $"'{v}' states a part count");
    }

    [Fact]
    public void The_piece_part_field_is_the_material_and_nothing_else()
        => Assert.Equal("3/4\" Plywood | rPLY_3-4", Values()["$PIECE/PART_NAME"]);

    [Fact]
    public void The_plate_name_is_the_fixed_catalogue_string()
        => Assert.Equal("CNC Part Catalogue", Values()["$PLATE_NAME"]);

    [Fact]
    public void Plate_numbering_is_two_digit()
    {
        var v = Values();
        Assert.Equal("02", v["#"]);
        Assert.Equal("03", v["##"]);
    }

    [Fact]
    public void The_date_is_the_run_date_not_the_clock_at_read_time()
        => Assert.Equal("09/14/2026", Values()["#DRAWING_DATE"]);

    [Fact]
    public void The_final_numbering_sweep_touches_only_the_two_number_fields()
    {
        var v = TitleBlock.PageNumberValues(1, 5, new PlateLayerOptions());
        Assert.Equal(new[] { "#", "##" }, v.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Retargeting_a_field_onto_an_operator_owned_tag_is_refused()
    {
        // Misconfiguration should not become a silent overwrite of someone's project name.
        var opt = new PlateLayerOptions { TitleBlockPlateName = "$PROJECT_NAME" };
        Assert.DoesNotContain(Values(opt).Keys, k => k == "$PROJECT_NAME");
    }
}
