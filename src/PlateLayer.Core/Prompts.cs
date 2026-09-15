namespace PlateLayer.Core;

/// <summary>
/// The command line's keyword sets, and the rule that keeps them usable.
///
/// AutoCAD derives a keyword's shortcut from its capital letters, falling back to the first
/// letter. Two keywords in one prompt that reduce to the same shortcut are not rejected — the
/// first one silently wins, and the second becomes unreachable by keystroke. That is exactly what
/// happened with Dims and Density, and it is invisible to any test that does not know the rule.
///
/// So the sets live here, in the assembly that can be tested without AutoCAD, and
/// <see cref="Validate"/> is asserted over every one of them.
///
/// Where a natural name collides, capitalise the distinguishing letter the way AutoCAD's own
/// commands do: "aLl" answers to L, "eXit" to X.
/// </summary>
public static class Prompts
{
    public static readonly string[] Settings =
    {
        "Template",    // T - layout to clone
        "Parts",       // P - max parts per plate
        "Scalemode",   // S
        "Dims",        // D
        "Margin",      // M - viewport margin
        "Coarsen",     // C - coarsen for density
        "Labels",      // L - per-part labels and scale notes
        "Names",       // N - material code table
        "Approval",    // A
        "Quit",        // Q
    };

    public static readonly string[] Approval =
    {
        "Accept",      // A
        "Redo",        // R
        "Skip",        // S
        "aLl",         // L - accept this and every remaining plate
        "eXit",        // X
    };

    /// <summary>
    /// Scale policy keywords: F, B, M. Single plain words with distinct first letters, rather than
    /// the enum names — "FinestThatFits" and "FewestSheets" would both answer to F, and mixed-case
    /// spellings like "feWestSheets" are not how AutoCAD's own commands read.
    /// </summary>
    public static readonly string[] Policies = { "Finest", "Balanced", "Minimum" };

    public static string PolicyKeyword(ScalePolicy policy) => policy switch
    {
        ScalePolicy.FinestThatFits => "Finest",
        ScalePolicy.FewestSheets   => "Minimum",
        _                          => "Balanced",
    };

    public static ScalePolicy? PolicyFor(string keyword) => keyword switch
    {
        "Finest"  => ScalePolicy.FinestThatFits,
        "Minimum" => ScalePolicy.FewestSheets,
        "Balanced" => ScalePolicy.Balanced,
        _ => null,
    };

    public static readonly string[] Proceed = { "Yes", "No", "Dryrun" };
    public static readonly string[] YesNo = { "Yes", "No" };

    /// <summary>The keystroke AutoCAD will match: the capital letters, else the first letter.</summary>
    public static string Shortcut(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return "";
        var caps = new string(keyword.Where(char.IsUpper).ToArray());
        return caps.Length > 0 ? caps : keyword[..1].ToUpperInvariant();
    }

    /// <summary>Throws when two keywords in a set share a shortcut. Call it on every set.</summary>
    public static void Validate(string setName, IReadOnlyList<string> keywords)
    {
        var clash = keywords
            .GroupBy(Shortcut, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (clash is not null)
            throw new InvalidOperationException(
                $"Prompt '{setName}': {string.Join(" and ", clash)} both answer to '{clash.Key}'. " +
                $"Capitalise a distinguishing letter, as in aLl or eXit.");
    }

    /// <summary>Every set this command line uses, for a single test to walk.</summary>
    public static IEnumerable<(string Name, string[] Keywords)> All()
    {
        yield return (nameof(Settings), Settings);
        yield return (nameof(Approval), Approval);
        yield return (nameof(Policies), Policies);
        yield return (nameof(Proceed), Proceed);
        yield return (nameof(YesNo), YesNo);
    }
}
