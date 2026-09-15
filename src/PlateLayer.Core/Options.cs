namespace PlateLayer.Core;

public enum DimStrategy { Dimensions, MTextOnly }

/// <summary>
/// How a part's viewport scale is chosen.
/// <para><b>UniformPerPlate</b> (default): every part on a plate shares one scale — the coarsest any
/// part on that plate needs. One scale note per sheet, parts are visually comparable, and packing
/// stays dense.</para>
/// <para><b>PerPartBestFit</b>: each part takes the finest rung that fits the whole drawable area.
/// Measured on the reference cut file this produces single-part plates, because a small part
/// greedily expands to fill a sheet. Kept because OPEN D8-A specified it; not the default.</para>
/// </summary>
public enum ScaleMode { UniformPerPlate, PerPartBestFit }

/// <summary>
/// How hard the planner works at reducing the number of plates.
///
/// Measured on real and synthetic runs, the sheet count is almost always decided by
/// <see cref="PlateLayerOptions.MaxPartsPerPage"/>, not by the scale: coarsening already reaches
/// the fewest sheets the parts-per-plate cap permits, and going coarser still buys nothing while
/// that cap holds. So a policy that genuinely minimises sheets has to relax the cap as well.
/// </summary>
public enum ScalePolicy
{
    /// <summary>Finest rung that fits a part on a sheet. No coarsening; most plates, largest drawings.</summary>
    FinestThatFits,

    /// <summary>
    /// Default. Coarsen while it reduces the plate count, bounded by the parts-per-plate
    /// preference and the legibility floor. Ties go to the finer scale.
    /// </summary>
    Balanced,

    /// <summary>
    /// Fewest sheets **per material**: the parts-per-plate preference is set aside in favour of the
    /// MAXACTVP hard cap, and the scale coarsens to match. Expect crowded plates and small drawings
    /// — on one test case 13 parts went from 2 plates at 1:32 to 1 plate at 1:48.
    ///
    /// It cannot go below one plate per material. With
    /// <see cref="PlateLayerOptions.OneMaterialPerPlate"/> on, a drawing with eight materials
    /// produces eight plates under every policy, and no scale changes that. The run summary says so
    /// when that floor is what is holding.
    /// </summary>
    FewestSheets,
}
public enum ApprovalMode { PerPage, Batch }

public sealed class PlateLayerOptions
{
    // ---- source filtering -------------------------------------------------
    /// <summary>
    /// Layers never treated as part geometry. Trailing '*' is a prefix wildcard.
    /// Defaults come from the real cut file: sheet-group rectangles and their labels
    /// live on 'rOJECTS', the sheet template on the rTTBL_/rGUIDES_ families.
    /// </summary>
    public List<string> ExcludedLayers { get; set; } = new()
    {
        "rOJECTS", "rOBJECT", "Defpoints", "rGUIDES*", "rTITLEBLOCK", "rTTBL_*",
        "rPAPER_EDGE", "rSAFE_EDGE", "rVIEWPORTS", "rDIMS_PAPER", "rATTACHMENTS",
        "0-Paperspace Text", "MD_*", "Info", "DO NOT CUT"
    };

    /// <summary>Gap below which two curves are considered one part. 0 = must overlap.</summary>
    public double GapTolerance { get; set; } = 0.0;

    /// <summary>A loop wholly containing at least this many mutually disjoint loops is a sheet outline, not a part.</summary>
    public int SheetOutlineMinChildren { get; set; } = 3;

    // ---- paper ------------------------------------------------------------
    /// <summary>
    /// Layout tab cloned for each plate. Resolution is forgiving: exact name, then case-insensitive,
    /// then a unique prefix match — a template renamed from "ANSI B (1)" to "ANSI B" should not be a
    /// hard failure, and a wrong guess should name the layouts that do exist rather than just refuse.
    /// </summary>
    public string TemplateLayout { get; set; } = "ANSI B";
    public string TemplatePath { get; set; } = "";

    /// <summary>Usable rectangle in paper units: inside the border, left of the title strip, below the title band.</summary>
    public BBox DrawableArea { get; set; } = new(0.125, 0.125, 15.875, 10.375);

    /// <summary>Space between tiles on a plate.</summary>
    public double Gutter { get; set; } = 0.25;

    /// <summary>
    /// Breathing room between the part's outer edge and the viewport frame, in paper units.
    /// Without it the part outline lands exactly on the clip boundary and on the non-plotting
    /// viewport border, so the part plots with no outline at all.
    /// Costs model-space visibility: margin x scale denominator is how much surrounding model
    /// space the viewport reveals, so keep it well under the spacing between parts in the source.
    /// </summary>
    public double ViewportMargin { get; set; } = 0.125;

    /// <summary>Distance from the part edge out to the dimension line, in paper units.</summary>
    public double DimLineOffset { get; set; } = 0.35;

    /// <summary>
    /// Plotted height of dimension text. Set from the drawing's DIMTXT at run time; the default
    /// matches the reference title block. Tile gutters are sized from this, so a wrong value here
    /// is what pushes dimension text off the sheet.
    /// </summary>
    public double DimTextHeight { get; set; } = 0.1875;

    public double LabelTextHeight { get; set; } = 0.1875;

    /// <summary>
    /// Plotted arrowhead length (DIMASZ), read off the drawing at run time. Needed because when
    /// text and arrows will not fit between the extension lines, AutoCAD puts them outside and the
    /// dimension draws wider than the part it measures — which is how small parts ended up with
    /// their dimensions running into the neighbouring tile.
    /// </summary>
    public double DimArrowSize { get; set; } = 0.125;

    /// <summary>
    /// Write fractions as MText stacks (\S3/4;) so they render the way a hand-typed dimension does,
    /// rather than as flat "3/4". Dimension text is an explicit override here, so AutoCAD's own
    /// DIMFRAC stacking never gets a chance to apply.
    /// </summary>
    public bool StackedFractions { get; set; } = true;

    /// <summary>
    /// Per-part index label above each viewport. Off: on short parts the vertical dimension text
    /// sits at the same height as the label and collides with it, and the label carries no meaning
    /// yet — there is nothing in the cut file to cross-reference it against.
    /// </summary>
    public bool ShowPartLabels { get; set; } = false;

    /// <summary>
    /// Per-viewport scale note. Off, and the scale is not stated anywhere else either: the
    /// dimensions carry the real measurements, so a scale figure adds nothing a reader needs.
    /// </summary>
    public bool ShowScaleNotes { get; set; } = false;

    /// <summary>
    /// Plotted width of one character as a fraction of text height. Measured off the drawing's own
    /// dimension text style at run time; this default is a deliberately fat fallback. Under-
    /// estimating here is what puts dimension text on top of the part it is measuring.
    /// </summary>
    public double TextWidthFactor { get; set; } = 0.85;

    /// <summary>
    /// Hard clear space between dimension text and the part outline, on top of the width estimate.
    /// Exists so that being slightly wrong about the font is not the same as being wrong about the
    /// drawing.
    /// </summary>
    public double TextClearance { get; set; } = 0.0625;

    /// <summary>
    /// Safety band on the width estimate. Tile geometry is laid out as though every string were
    /// this much wider than <see cref="TextWidthFactor"/> says, so the drawing survives the font
    /// being somewhat wider than measured. This is the promise the layout tests check against —
    /// beyond it, text can touch the part, and the honest fix is a better measurement, not a
    /// bigger number.
    /// </summary>
    public double TextWidthSafety { get; set; } = 1.20;

    /// <summary>Clear space between text and the edge of its tile.</summary>
    public double TextPad { get; set; } = 0.10;

    /// <summary>Centre each plate's packed block in the drawable area instead of jamming it top-left.</summary>
    public bool CenterOnPlate { get; set; } = true;

    // ---- scale ------------------------------------------------------------
    public int ScaleFloorDenominator { get; set; } = 96;
    public ScaleMode ScaleMode { get; set; } = ScaleMode.UniformPerPlate;

    /// <summary>
    /// How hard to work at reducing the sheet count. See <see cref="ScalePolicy"/>.
    /// </summary>
    public ScalePolicy Policy { get; set; } = ScalePolicy.Balanced;

    /// <summary>Kept for the older settings key; reads and writes <see cref="Policy"/>.</summary>
    public bool CoarsenForDensity
    {
        get => Policy != ScalePolicy.FinestThatFits;
        set => Policy = value ? ScalePolicy.Balanced : ScalePolicy.FinestThatFits;
    }

    /// <summary>
    /// Legibility floor for coarsening: the <b>typical</b> part in a material must still plot at
    /// least this many paper inches on its shorter side, measured as the median across the
    /// material, ignoring parts that are degenerate on an axis.
    ///
    /// Median, not minimum, and that distinction is the whole point. A cut file holds 2" strips and
    /// scored lines alongside five-foot panels; measured on the smallest part the floor is
    /// unreachable at any scale that fits, so it never operates and coarsening runs away — one
    /// material went to 1:64 and 21 parts on a sheet before this changed. One strip should not
    /// speak for twenty-one parts, and equally a material that is mostly strips should still brake.
    ///
    /// A hairline strip on a sheet shared with large panels is not fixable by scale: the only way
    /// to draw a 2" strip legibly beside a 6' panel is to give it its own sheet.
    /// </summary>
    public double MinTypicalPartPaperSize { get; set; } = 0.5;

    /// <summary>Old name for <see cref="MinTypicalPartPaperSize"/>, kept so saved settings still load.</summary>
    public double MinPartPaperDimension
    {
        get => MinTypicalPartPaperSize;
        set => MinTypicalPartPaperSize = value;
    }

    // ---- pagination -------------------------------------------------------
    public int MaxPartsPerPage { get; set; } = 12;
    /// <summary>MAXACTVP caps active viewports per layout at 64; never plan near it.</summary>
    public const int ViewportHardCap = 60;
    /// <summary>
    /// A plate holds one material. This is the strongest constraint in the planner: it sets a hard
    /// floor of one plate per material that no scale policy can cross, and it is deliberate — a
    /// catalogue sheet you carry to the saw should describe the sheet of stock you are cutting.
    /// Turning it off is the only way below that floor, and costs the grouping the tool exists for.
    /// </summary>
    public bool OneMaterialPerPlate { get; set; } = true;

    /// <summary>
    /// Spread parts evenly across the plates a material already needs, instead of filling early
    /// plates and leaving the remainder alone on the last one. Costs nothing — same plate count,
    /// same scale — and only ever changes which plate a part lands on.
    /// </summary>
    public bool BalancePlates { get; set; } = true;

    /// <summary>
    /// A plate using less than this fraction of the drawable area is named in the run summary.
    /// Not every sparse plate is avoidable — a material holding two small parts cannot fill a
    /// sheet without being split across two — but it should be visible before it is printed.
    ///
    /// A judgement line, not a measured one, calibrated between two known cases: the reference
    /// file's least-full plate and a material holding two small parts, which is as sparse as a plate
    /// can honestly get. Both moved up when tiles gained their arrowhead allowance — 42% and 30%
    /// now — so the line moved with them rather than quietly going deaf.
    /// </summary>
    public double LowFillWarning { get; set; } = 0.35;

    // ---- output -----------------------------------------------------------
    public DimStrategy DimStrategy { get; set; } = DimStrategy.Dimensions;
    public ApprovalMode ApprovalMode { get; set; } = ApprovalMode.PerPage;

    public string ViewportLayer { get; set; } = "rVIEWPORTS";
    public string DimLayer { get; set; } = "rDIMS_PAPER";
    public string LabelLayer { get; set; } = "0-Paperspace Text";

    // ---- title block ------------------------------------------------------
    public string TitleBlockPlateName { get; set; } = "$PLATE_NAME";
    public string TitleBlockPieceName { get; set; } = "$PIECE/PART_NAME";
    public string TitleBlockDate { get; set; } = "#DRAWING_DATE";
    public string TitleBlockPlateNumber { get; set; } = "#";
    public string TitleBlockPlateTotal { get; set; } = "##";

    /// <summary>What every plate calls itself. The material goes in the piece/part field.</summary>
    public string PlateNameText { get; set; } = "CNC Part Catalogue";

    // ---- material naming ---------------------------------------------------

    /// <summary>Stripped from the front of a layer name before parsing. The shop's own convention.</summary>
    public string LayerPrefix { get; set; } = "r";

    /// <summary>
    /// Material layer code to plain name. Extend it with PLLSET -> Materials. An unlisted code is
    /// left as the bare layer name rather than guessed at, and is reported in the run summary.
    ///
    /// Seeded with PLY only. Input to this tool is sheet goods by contract — PartLayer flattens
    /// sheet stock, and stick and board goods (poplar, framing lumber) never reach a plate — so a
    /// code that fails to expand is a signal: either a sheet good that needs adding here, or
    /// geometry that should not have been in the run at all.
    /// </summary>
    public Dictionary<string, string> MaterialCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PLY"] = "Plywood",
    };

    /// <summary>Trailing qualifier on a layer name, e.g. the BEND in rPLY_1-2BEND.</summary>
    public Dictionary<string, string> MaterialQualifiers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BEND"] = "Bending",
    };

    /// <summary>
    /// Parts allowed on one plate. <see cref="ScalePolicy.FewestSheets"/> sets the readability
    /// preference aside and allows up to the MAXACTVP-safe hard cap, because on almost every real
    /// run it is this number — not the scale — that decides how many sheets come out.
    /// </summary>
    public int EffectivePartsPerPage => Policy == ScalePolicy.FewestSheets
        ? ViewportHardCap
        : Math.Min(MaxPartsPerPage, ViewportHardCap);

    public static bool LayerMatches(string layer, string pattern) =>
        pattern.EndsWith('*')
            ? layer.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(layer, pattern, StringComparison.OrdinalIgnoreCase);

    public bool IsExcludedLayer(string layer) => ExcludedLayers.Any(p => LayerMatches(layer, p));
}
