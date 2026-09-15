namespace PlateLayer.Core.Tests;

public class PromptTests
{
    [Fact]
    public void No_prompt_offers_two_keywords_that_answer_to_the_same_keystroke()
    {
        // The bug this exists for: Dims and Density both reduced to D, so pressing D always got
        // Dims and Density was unreachable. AutoCAD accepts such a set without complaint.
        foreach (var (name, keywords) in Prompts.All())
            Prompts.Validate(name, keywords);
    }

    [Theory]
    [InlineData("Template", "T")]
    [InlineData("Dims", "D")]
    [InlineData("aLl", "L")]
    [InlineData("eXit", "X")]
    [InlineData("Accept", "A")]
    public void Shortcuts_follow_AutoCADs_capital_letter_rule(string keyword, string expected)
        => Assert.Equal(expected, Prompts.Shortcut(keyword));

    [Fact]
    public void The_settings_prompt_still_covers_every_setting_it_used_to()
    {
        // Renaming for unique keystrokes must not quietly drop an option.
        Assert.Equal(
            new[] { "Approval", "Coarsen", "Dims", "Labels", "Margin", "Names", "Parts", "Quit", "Scalemode", "Template" },
            Prompts.Settings.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_approval_prompt_keeps_all_five_answers()
        => Assert.Equal(5, Prompts.Approval.Length);

    [Fact]
    public void A_colliding_set_is_refused_rather_than_accepted_silently()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Prompts.Validate("test", new[] { "Dims", "Density" }));
        Assert.Contains("'D'", ex.Message);
    }

    [Fact]
    public void Every_keyword_reduces_to_a_non_empty_shortcut()
    {
        foreach (var (_, keywords) in Prompts.All())
            foreach (var k in keywords)
                Assert.False(string.IsNullOrEmpty(Prompts.Shortcut(k)), $"'{k}' has no shortcut");
    }
}

public class LayerConventionTests
{
    [Fact]
    public void Dimensions_go_on_the_drawings_own_paper_dimension_layer()
    {
        // Not a knob the operator has to set, and deliberately not persisted in the settings
        // xrecord — a stale saved value cannot move dimensions off this layer.
        Assert.Equal("rDIMS_PAPER", new PlateLayerOptions().DimLayer);
    }

    [Fact]
    public void Viewport_frames_and_labels_use_the_drawings_existing_layers_too()
    {
        var opt = new PlateLayerOptions();
        Assert.Equal("rVIEWPORTS", opt.ViewportLayer);
        Assert.Equal("0-Paperspace Text", opt.LabelLayer);
    }
}
