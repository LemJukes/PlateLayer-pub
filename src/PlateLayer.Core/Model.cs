namespace PlateLayer.Core;

/// <summary>One physical piece: an outer loop plus any cutouts and interior marks.</summary>
public sealed record Part(
    string Id,
    string Material,
    IReadOnlyList<Curve2D> Curves,
    BBox Outer,
    string Label)
{
    public double WModel => Outer.Width;
    public double HModel => Outer.Height;
    public int CutoutCount => Curves.Count - 1;
}

/// <summary>A rung of the scale ladder. <see cref="Denominator"/> is model units per paper unit.</summary>
public readonly record struct ScaleRung(int Denominator, string Name)
{
    /// <summary>Paper units per model unit — this is what an AutoCAD viewport CustomScale wants.</summary>
    public double PaperPerModel => 1.0 / Denominator;
    public override string ToString() => $"1:{Denominator} ({Name})";
}

public sealed record ScaleChoice(string PartId, ScaleRung Rung);

/// <summary>
/// A part's footprint on paper. Three nested rectangles:
/// the <b>tile</b> (what the packer places), the <b>viewport</b> inside it, and the <b>part</b>
/// inside that, inset by <see cref="Margin"/> so its outline is not sitting on the viewport frame.
/// Origin is the tile's lower-left on the plate.
/// </summary>
public sealed record Tile(
    string PartId,
    string Label,
    string Material,
    ScaleRung Rung,
    double PartWPaper,
    double PartHPaper,
    double WModel,
    double HModel,
    double ModelCenterX,
    double ModelCenterY,
    double Margin,
    double DimOffsetX,
    double DimOffsetY,
    double GutterLeft,
    double GutterRight,
    double GutterBottom,
    double GutterTop,
    double OriginX = 0,
    double OriginY = 0)
{
    public double ViewportW => PartWPaper + 2 * Margin;
    public double ViewportH => PartHPaper + 2 * Margin;

    public double WPaper => GutterLeft + ViewportW + GutterRight;
    public double HPaper => GutterBottom + ViewportH + GutterTop;

    /// <summary>Lower-left of the viewport window.</summary>
    public Pt ViewportOrigin => new(OriginX + GutterLeft, OriginY + GutterBottom);
    public Pt ViewportCenter => new(ViewportOrigin.X + ViewportW / 2.0, ViewportOrigin.Y + ViewportH / 2.0);

    /// <summary>Lower-left of the part itself, inset from the viewport frame by the margin.</summary>
    public Pt PartOrigin => new(ViewportOrigin.X + Margin, ViewportOrigin.Y + Margin);
    public BBox PartRect => new(PartOrigin.X, PartOrigin.Y, PartOrigin.X + PartWPaper, PartOrigin.Y + PartHPaper);

    public Tile At(double x, double y) => this with { OriginX = x, OriginY = y };
    public Tile Shift(double dx, double dy) => this with { OriginX = OriginX + dx, OriginY = OriginY + dy };
}

public sealed record PagePlan(int Index, string Material, IReadOnlyList<Tile> Tiles, double FillFraction = 0)
{
    public string SheetName => $"PL - {Material} - {Index:00}";

    /// <summary>Paper area the tiles occupy, annotation bands included.</summary>
    public double UsedArea => Tiles.Sum(t => t.WPaper * t.HPaper);
}

public sealed record Exclusion(string PartId, string Label, string Material, string Reason);

public sealed record RunPlan(
    IReadOnlyList<PagePlan> Pages,
    IReadOnlyList<Exclusion> Excluded,
    IReadOnlyList<string> Warnings)
{
    public int TotalPages => Pages.Count;
    public int TotalParts => Pages.Sum(p => p.Tiles.Count);
}
