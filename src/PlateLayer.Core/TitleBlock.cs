namespace PlateLayer.Core;

/// <summary>
/// Decides which title block attributes a plate writes, and what into them.
///
/// This lives in Core rather than the AutoCAD shell for one reason: it is the only part of the
/// plugin that edits fields a person owns, so what it does and — more importantly — what it does
/// NOT touch needs to be testable without AutoCAD running.
///
/// The plugin fills only fields it can know the answer to: what the plate is, what material it
/// shows, when it was generated, and where it sits in the set. Everything else in the title block
/// is the operator's: project name, full project name, project number, client, producer,
/// draftsperson, approved-by, and notes. Those are never written, not even when blank.
/// </summary>
public static class TitleBlock
{
    /// <summary>
    /// Attribute tags the plugin must never write, whatever else changes. Named so the intent
    /// survives someone later adding a field without thinking about it.
    /// </summary>
    public static readonly IReadOnlyList<string> OperatorOwnedTags = new[]
    {
        "$PROJECT_NAME",
        "$PROJECT_NAME_FULL",
        "$PROJECT_#",
        "$CLIENT_NAME",
        "$PRODUCER_NAME",
        "$DRAFTSPERSON_NAME",
        "$APPROVED_BY_NAME",
        "$NOTES",
    };

    public static Dictionary<string, string> Values(PagePlan page, int totalPages, PlateLayerOptions opt, DateTime now)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [opt.TitleBlockPlateName]   = opt.PlateNameText,
            [opt.TitleBlockPieceName]   = MaterialNames.Describe(page.Material, opt),
            [opt.TitleBlockDate]        = now.ToString("MM/dd/yyyy"),
            [opt.TitleBlockPlateNumber] = $"{page.Index:00}",
            [opt.TitleBlockPlateTotal]  = $"{totalPages:00}",
        };

        foreach (var tag in OperatorOwnedTags) values.Remove(tag);
        return values;
    }

    /// <summary>Page numbering only, for the final sweep once the kept plates are known.</summary>
    public static Dictionary<string, string> PageNumberValues(int index, int total, PlateLayerOptions opt) =>
        new(StringComparer.Ordinal)
        {
            [opt.TitleBlockPlateNumber] = $"{index:00}",
            [opt.TitleBlockPlateTotal]  = $"{total:00}",
        };
}
