namespace PlateLayer.Core;

/// <summary>
/// Plotted width of a string. Core cannot call into AutoCAD, so the factor is supplied by the
/// shell, which measures the drawing's real dimension text style; the option default is a fat
/// fallback for when that measurement fails.
///
/// This is an estimate, and every clearance built on it carries an explicit
/// <see cref="PlateLayerOptions.TextClearance"/> on top — a shared estimate used by both the code
/// and its tests proves nothing on its own.
/// </summary>
public static class TextMetrics
{
    public static double Width(string text, double height, double widthFactor) =>
        text.Length * height * widthFactor;

    public static double Width(string text, PlateLayerOptions opt) =>
        Width(text, opt.DimTextHeight, opt.TextWidthFactor);

    /// <summary>Width with the safety band applied. This is what tile geometry is built from.</summary>
    public static double SafeWidth(string text, PlateLayerOptions opt) =>
        Width(text, opt.DimTextHeight, opt.TextWidthFactor * opt.TextWidthSafety);
}

/// <summary>Imperial architectural ladder, coarsening downward.</summary>
public static class ScaleLadder
{
    public static readonly IReadOnlyList<ScaleRung> Rungs = new[]
    {
        new ScaleRung( 1, "FULL"),
        new ScaleRung( 2, "6\"=1'"),
        new ScaleRung( 4, "3\"=1'"),
        new ScaleRung( 8, "1 1/2\"=1'"),
        new ScaleRung(12, "1\"=1'"),
        new ScaleRung(16, "3/4\"=1'"),
        new ScaleRung(24, "1/2\"=1'"),
        new ScaleRung(32, "3/8\"=1'"),
        new ScaleRung(48, "1/4\"=1'"),
        new ScaleRung(64, "3/16\"=1'"),
        new ScaleRung(96, "1/8\"=1'"),
    };

    /// <summary>
    /// Largest rung (finest scale) whose whole tile — annotation bands included — still fits the
    /// drawable area. Null when even the floor will not fit.
    /// </summary>
    public static ScaleRung? Choose(double wModel, double hModel, PlateLayerOptions opt)
    {
        var area = opt.DrawableArea;
        foreach (var rung in Rungs)
        {
            if (rung.Denominator > opt.ScaleFloorDenominator) break;
            var tile = MakeTile("probe", "", "", wModel, hModel, 0, 0, rung, opt);
            if (tile.WPaper <= area.Width && tile.HPaper <= area.Height) return rung;
        }
        return null;
    }

    public static Tile MakeTile(Part part, ScaleRung rung, PlateLayerOptions opt) =>
        MakeTile(part.Id, part.Label, part.Material, part.WModel, part.HModel,
                 part.Outer.Center.X, part.Outer.Center.Y, rung, opt);

    public static Tile MakeTile(string id, string label, string material,
                                double wModel, double hModel, double cx, double cy,
                                ScaleRung rung, PlateLayerOptions opt)
    {
        var partW = wModel * rung.PaperPerModel;
        var partH = hModel * rung.PaperPerModel;

        // The Y dimension's text is horizontal (the drawing's dimension style keeps it upright),
        // centred on a dimension line sitting DimLineOffset outside the part. Half of it therefore
        // sticks out to the left of that line, and the tile has to own that width.
        var yText = TextMetrics.SafeWidth(ArchFormat.Inches(hModel), opt);
        var xText = TextMetrics.SafeWidth(ArchFormat.Inches(wModel), opt);

        // The vertical dimension's text is horizontal and centred on its dimension line, so half of
        // it reaches back toward the part. A fixed offset lets a long string like 11 1/8" sit on top
        // of the part outline; push the line out far enough that it cannot.
        var dimOffsetY = Math.Max(opt.DimLineOffset, yText / 2.0 + opt.TextClearance);
        var dimOffsetX = Math.Max(opt.DimLineOffset, opt.DimTextHeight + opt.TextClearance);

        // A dimension can draw LARGER THAN THE PART it measures: when text or arrowheads will not
        // fit between the extension lines, AutoCAD moves them outside.
        //
        // Whether it does so is decided by DIMATFIT, DIMTIX, DIMTOFL, the text style and — for a
        // stacked fraction — a text block taller than DIMTXT. Predicting that was tried and got it
        // wrong: on a part comfortably taller than text-plus-arrows, AutoCAD still placed the arrows
        // outside and the dimension reached into the title block.
        //
        // So the arrow allowance is unconditional. It costs about a quarter inch per tile edge and
        // it removes a whole class of "AutoCAD decided differently" faults. The text allowance stays
        // conditional, because a part wider than its own dimension text genuinely keeps it inside.
        var arrow = opt.DimArrowSize;
        var arrowRoom = 2 * arrow;

        // A stacked fraction sets as two lines at reduced size: taller than plain text, and it is
        // the height that decides whether a vertical dimension's text fits between its extensions.
        var textBlock = opt.DimTextHeight * (opt.StackedFractions ? 1.5 : 1.0);

        var xOvershoot = arrowRoom + (partW < xText ? xText / 2.0 : 0.0);
        var yOvershoot = arrowRoom + (partH < textBlock ? textBlock : 0.0);

        var labelBand = opt.ShowPartLabels || opt.ShowScaleNotes ? opt.LabelTextHeight : 0.0;

        // Gutters are measured from the tile edge to the viewport edge, and the part sits one
        // margin inside that, so each clearance is reduced by the margin it already has.
        double Gutter(double needed) => Math.Max(0, needed + opt.TextPad - opt.ViewportMargin);

        var gutterLeft = Gutter(Math.Max(dimOffsetY + yText / 2.0, xOvershoot));
        var gutterRight = Gutter(xOvershoot);
        var gutterBottom = Gutter(Math.Max(dimOffsetX + textBlock, yOvershoot));
        var gutterTop = Gutter(Math.Max(yOvershoot, labelBand));

        return new Tile(
            PartId: id,
            Label: label,
            Material: material,
            Rung: rung,
            PartWPaper: partW,
            PartHPaper: partH,
            WModel: wModel,
            HModel: hModel,
            ModelCenterX: cx,
            ModelCenterY: cy,
            Margin: opt.ViewportMargin,
            DimOffsetX: dimOffsetX,
            DimOffsetY: dimOffsetY,
            GutterLeft: gutterLeft,
            GutterRight: gutterRight,
            GutterBottom: gutterBottom,
            GutterTop: gutterTop);
    }
}
