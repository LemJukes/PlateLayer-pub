using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using PlateLayer.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(PlateLayer.Cad.Commands))]

namespace PlateLayer.Cad;

public sealed class Commands
{
    /// <summary>
    /// Read from the assembly so the build is the single source of truth. Set the number in
    /// Directory.Build.props; nothing here carries a second copy to fall out of step.
    /// </summary>
    public static readonly string Version =
        $"PlateLayer Alpha {typeof(Commands).Assembly.GetName().Version?.ToString(2) ?? "?"}";

    [CommandMethod("PLATELAYER", CommandFlags.Modal)]
    public void PlateLayerLong() => Run();

    [CommandMethod("PLL", CommandFlags.Modal)]
    public void PlateLayerShort() => Run();

    [CommandMethod("PLLSET", CommandFlags.Modal)]
    public void Settings()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var opt = Config.Load(doc.Database);

        ed.WriteMessage($"\n{Version}");
        ed.WriteMessage($"\n  Template layout : {opt.TemplateLayout}");
        ed.WriteMessage($"\n  Drawable area   : {opt.DrawableArea}");
        ed.WriteMessage($"\n  Scale mode      : {opt.ScaleMode}   floor 1:{opt.ScaleFloorDenominator}");
        ed.WriteMessage($"\n  Coarsen         : {opt.Policy}   typical part >= {opt.MinTypicalPartPaperSize:0.###}\" on paper" +
                        (opt.Policy == ScalePolicy.FewestSheets ? $"   (parts/plate raised to {PlateLayerOptions.ViewportHardCap})" : ""));
        ed.WriteMessage($"\n  Dims            : {opt.DimStrategy}   text {opt.DimTextHeight:0.####}\"");
        ed.WriteMessage($"\n  Viewport margin : {opt.ViewportMargin:0.####}\"   centred: {opt.CenterOnPlate}");
        ed.WriteMessage($"\n  Approval        : {opt.ApprovalMode}   max {opt.MaxPartsPerPage} parts/plate");
        ed.WriteMessage($"\n  Part labels     : {opt.ShowPartLabels}   scale notes: {opt.ShowScaleNotes}");
        ed.WriteMessage($"\n  Plate name      : {opt.PlateNameText}");
        ed.WriteMessage($"\n  Materials       : {string.Join(", ", opt.MaterialCodes.Select(m => $"{m.Key}={m.Value}"))}");

        var pko = new PromptKeywordOptions("\nChange") { AllowNone = true };
        AddKeywords(pko, nameof(Prompts.Settings), Prompts.Settings, "Quit");

        while (true)
        {
            var r = ed.GetKeywords(pko);
            if (r.Status != PromptStatus.OK || r.StringResult == "Quit") break;
            switch (r.StringResult)
            {
                case "Template":
                {
                    var s = ed.GetString(new PromptStringOptions($"\nTemplate layout <{opt.TemplateLayout}>: ") { AllowSpaces = true });
                    if (s.Status == PromptStatus.OK && s.StringResult.Length > 0) opt.TemplateLayout = s.StringResult;
                    break;
                }
                case "Parts":
                {
                    var n = ed.GetInteger(new PromptIntegerOptions($"\nMax parts per plate <{opt.MaxPartsPerPage}>: ")
                        { LowerLimit = 1, UpperLimit = PlateLayerOptions.ViewportHardCap });
                    if (n.Status == PromptStatus.OK) opt.MaxPartsPerPage = n.Value;
                    break;
                }
                case "Scalemode":
                    opt.ScaleMode = Toggle(ed, "Scale", opt.ScaleMode, ScaleMode.UniformPerPlate, ScaleMode.PerPartBestFit);
                    break;
                case "Dims":
                    opt.DimStrategy = Toggle(ed, "Dims", opt.DimStrategy, DimStrategy.Dimensions, DimStrategy.MTextOnly);
                    break;
                case "Labels":
                {
                    opt.ShowPartLabels = !opt.ShowPartLabels;
                    opt.ShowScaleNotes = opt.ShowPartLabels;
                    ed.WriteMessage($"\nPer-part labels and scale notes: {opt.ShowPartLabels}");
                    break;
                }
                case "Names":
                {
                    ed.WriteMessage("\nLayer codes currently recognised:");
                    foreach (var m in opt.MaterialCodes.OrderBy(m => m.Key))
                        ed.WriteMessage($"\n  {opt.LayerPrefix}{m.Key}_3-4  ->  {MaterialNames.Describe(opt.LayerPrefix + m.Key + "_3-4", opt)}");

                    var codeIn = ed.GetString(new PromptStringOptions("\nCode to add or change (Enter to skip): ") { AllowSpaces = false });
                    if (codeIn.Status != PromptStatus.OK || codeIn.StringResult.Length == 0) break;

                    var nameIn = ed.GetString(new PromptStringOptions($"\nMaterial name for '{codeIn.StringResult}': ") { AllowSpaces = true });
                    if (nameIn.Status == PromptStatus.OK && nameIn.StringResult.Length > 0)
                    {
                        opt.MaterialCodes[codeIn.StringResult] = nameIn.StringResult;
                        ed.WriteMessage($"\n  {opt.LayerPrefix}{codeIn.StringResult}_3-4  ->  " +
                                        $"{MaterialNames.Describe(opt.LayerPrefix + codeIn.StringResult + "_3-4", opt)}");
                    }
                    break;
                }
                case "Coarsen":
                {
                    var pkp = new PromptKeywordOptions("\nScale policy");
                    AddKeywords(pkp, "ScalePolicy", Prompts.Policies, Prompts.PolicyKeyword(opt.Policy));
                    var pr = ed.GetKeywords(pkp);
                    if (pr.Status == PromptStatus.OK && Prompts.PolicyFor(pr.StringResult) is { } chosen)
                        opt.Policy = chosen;

                    ed.WriteMessage($"\nScale policy: {opt.Policy}");
                    if (opt.Policy == ScalePolicy.FewestSheets)
                        ed.WriteMessage($"\n  Parts per plate is set aside in favour of the {PlateLayerOptions.ViewportHardCap} " +
                                        $"viewport hard cap. Expect crowded plates and small drawings.");

                    if (opt.Policy != ScalePolicy.FinestThatFits)
                    {
                        var d = ed.GetDouble(new PromptDoubleOptions(
                            $"\nSmallest the TYPICAL part may plot on its short side, paper inches <{opt.MinTypicalPartPaperSize:0.###}>: ")
                            { AllowNegative = false, AllowZero = false });
                        if (d.Status == PromptStatus.OK) opt.MinTypicalPartPaperSize = d.Value;
                    }
                    break;
                }
                case "Margin":
                {
                    var d = ed.GetDouble(new PromptDoubleOptions($"\nViewport margin, paper inches <{opt.ViewportMargin:0.####}>: ")
                        { AllowNegative = false, AllowZero = true });
                    if (d.Status == PromptStatus.OK) opt.ViewportMargin = d.Value;
                    break;
                }
                case "Approval":
                    opt.ApprovalMode = Toggle(ed, "Approval", opt.ApprovalMode, ApprovalMode.PerPage, ApprovalMode.Batch);
                    break;
            }
        }

        Config.Save(doc.Database, opt);
        ed.WriteMessage("\nSaved to this drawing.");
    }

    /// <summary>
    /// Adds a keyword set to a prompt, refusing any set where two keywords answer to the same
    /// keystroke. AutoCAD accepts such a set silently and makes the second keyword unreachable.
    /// </summary>
    private static void AddKeywords(PromptKeywordOptions pko, string setName, IReadOnlyList<string> keywords, string @default)
    {
        Prompts.Validate(setName, keywords);
        foreach (var k in keywords) pko.Keywords.Add(k);
        pko.Keywords.Default = @default;
    }

    private static T Toggle<T>(Editor ed, string label, T current, T a, T b) where T : struct, Enum
    {
        var pko = new PromptKeywordOptions($"\n{label}");
        AddKeywords(pko, label, new[] { a.ToString(), b.ToString() }, current.ToString());
        var r = ed.GetKeywords(pko);
        return r.Status == PromptStatus.OK && Enum.TryParse<T>(r.StringResult, out var picked) ? picked : current;
    }

    // -----------------------------------------------------------------------

    private static void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;
        var ed = doc.Editor;
        var db = doc.Database;

        ed.WriteMessage($"\n{Version}");

        var opt = Config.Load(db);

        // Gutter widths are computed from the plotted text height. Read it off the drawing rather
        // than guessing — a wrong value here is exactly what pushed dimension text off the sheet.
        if (db.Dimtxt > 0) opt.DimTextHeight = db.Dimtxt;
        if (db.Dimasz > 0) opt.DimArrowSize = db.Dimasz;
        opt.LabelTextHeight = opt.DimTextHeight;

        var measured = MeasureTextWidthFactor(db, opt.DimTextHeight);
        if (measured > 0)
        {
            opt.TextWidthFactor = measured;
            ed.WriteMessage($"\nDimension text: {opt.DimTextHeight:0.####}\" high, {measured:0.###} wide per character, " +
                            $"arrows {opt.DimArrowSize:0.####}\".");
        }
        else
        {
            ed.WriteMessage($"\nCould not measure the dimension text style; assuming {opt.TextWidthFactor:0.###} width per character.");
        }

        // OPEN D15: INSUNITS is the block-insert unit, not an authoritative statement about the
        // drawing. 0 (Unitless) is common on drawings that are plainly in inches, so it is not a
        // warning — only a positive non-inch value is.
        var insunits = (short)AcadApp.GetSystemVariable("INSUNITS");
        if (insunits != 0 && insunits != 1)
        {
            ed.WriteMessage($"\nINSUNITS is {insunits}, not inches. The scale ladder assumes inches.");
            if (!Confirm(ed, "Continue anyway?", false)) return;
        }

        var ids = PromptForGeometry(ed);
        if (ids is null) return;

        List<Curve2D> curves;
        int skipped;
        using (var tr = doc.TransactionManager.StartTransaction())
        {
            curves = CurveReader.Read(tr, ids, ed, out skipped);
            tr.Commit();
        }
        if (skipped > 0) ed.WriteMessage($"\n{skipped} selected object(s) were not part geometry.");

        var grouped = Grouping.Group(curves, opt);
        if (grouped.Parts.Count == 0)
        {
            ed.WriteMessage("\nNo parts found. Nothing changed.");
            return;
        }

        var plan = PlanBuilder.Build(grouped.Parts, opt, grouped.Warnings);
        ed.WriteMessage("\n" + PlanBuilder.ToReport(plan));

        // Resolve the template before asking to proceed. A missing or renamed layout should stop the
        // run with the drawing untouched, not half way through cloning plate one.
        var writer = new PlateWriter(doc, opt);
        string template;
        try
        {
            template = writer.ResolveTemplateLayout();
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n{ex.Message}");
            ed.WriteMessage("\nNothing changed.");
            return;
        }

        ed.WriteMessage($"\nTemplate layout: '{template}'.");
        if (writer.LastTemplateNote is not null) ed.WriteMessage($"\n{writer.LastTemplateNote}");

        var pko = new PromptKeywordOptions("\nProceed") { AllowNone = true };
        AddKeywords(pko, nameof(Prompts.Proceed), Prompts.Proceed, "Yes");
        var proceed = ed.GetKeywords(pko);
        if (proceed.Status != PromptStatus.OK || proceed.StringResult != "Yes")
        {
            ed.WriteMessage("\nDry run only. Nothing changed.");
            return;
        }

        WritePlates(writer, ed, opt, plan);
    }

    private static void WritePlates(PlateWriter writer, Editor ed, PlateLayerOptions opt, RunPlan plan)
    {
        var made = new List<string>();
        var batch = opt.ApprovalMode == ApprovalMode.Batch;
        var maxPerPage = opt.MaxPartsPerPage;

        var queue = new Queue<PagePlan>(plan.Pages);
        while (queue.Count > 0)
        {
            var page = queue.Dequeue();
            string layoutName;
            try
            {
                layoutName = writer.WritePage(page, plan.TotalPages);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPlate {page.Index} failed: {ex.Message}");
                ed.WriteMessage($"\n{made.Count} plate(s) already written are kept.");
                break;
            }

            if (batch) { made.Add(layoutName); continue; }

            var pko = new PromptKeywordOptions(
                $"\nPlate {page.Index} of {plan.TotalPages} [{page.Material}, {page.Tiles.Count} parts]") { AllowNone = true };
            AddKeywords(pko, nameof(Prompts.Approval), Prompts.Approval, "Accept");

            var answer = ed.GetKeywords(pko);
            var choice = answer.Status == PromptStatus.OK ? answer.StringResult : "eXit";

            switch (choice)
            {
                case "Accept":
                    made.Add(layoutName);
                    break;

                case "aLl":
                    made.Add(layoutName);
                    batch = true;
                    break;

                case "Skip":
                    writer.DeleteLayout(layoutName);
                    break;

                case "Redo":
                {
                    writer.DeleteLayout(layoutName);
                    var n = ed.GetInteger(new PromptIntegerOptions($"\nParts per plate <{maxPerPage}>: ")
                        { LowerLimit = 1, UpperLimit = PlateLayerOptions.ViewportHardCap });
                    if (n.Status == PromptStatus.OK) maxPerPage = n.Value;

                    // Re-plan everything still outstanding, this page included, at the new density.
                    var remaining = new List<PagePlan> { page };
                    remaining.AddRange(queue);
                    var parts = remaining.SelectMany(p => p.Tiles)
                        .Select(t => new Part(t.PartId, t.Material, System.Array.Empty<Curve2D>(),
                            new BBox(t.ModelCenterX - t.WModel / 2, t.ModelCenterY - t.HModel / 2,
                                     t.ModelCenterX + t.WModel / 2, t.ModelCenterY + t.HModel / 2),
                            t.Label))
                        .ToList();

                    var reOpt = Clone(opt, maxPerPage);
                    var rePlan = PlanBuilder.Build(parts, reOpt);
                    queue = new Queue<PagePlan>(rePlan.Pages);
                    ed.WriteMessage($"\nRe-planned the remaining parts onto {rePlan.TotalPages} plate(s).");
                    break;
                }

                default:
                    writer.DeleteLayout(layoutName);
                    ed.WriteMessage($"\nStopped. {made.Count} plate(s) kept.");
                    queue.Clear();
                    break;
            }
        }

        if (made.Count > 0)
        {
            writer.StampPageNumbers(made);      // 'nn of NN' once the real count is known
            ed.WriteMessage($"\n{made.Count} plate(s) written: {string.Join(", ", made)}");
        }

        // These are the shop's own convention layers. Having to invent one means the drawing was
        // not set up from the usual template, so its colour and plot settings are this plugin's
        // guesses rather than the shop's.
        if (writer.CreatedLayers.Count > 0)
            ed.WriteMessage($"\nCreated missing layer(s): {string.Join(", ", writer.CreatedLayers)}. " +
                            $"Check their colour and plot settings against your template.");
        foreach (var e in plan.Excluded)
            ed.WriteMessage($"\nEXCLUDED {e.Material} {e.Label}: {e.Reason}");
    }

    /// <summary>
    /// Width of one character of dimension text, as a fraction of its height, measured off the
    /// drawing's own dimension text style. Gutters and clearances are built on this, and the first
    /// test plot put text off the sheet precisely because it was a guess. Returns 0 if the style
    /// cannot be measured, and the caller keeps its fallback.
    /// </summary>
    private static double MeasureTextWidthFactor(Database db, double height)
    {
        const string sample = "0123456789'-/\" ";
        if (height <= 0) return 0;

        try
        {
            using var tr = db.TransactionManager.StartTransaction();

            var styleId = db.Textstyle;
            if (tr.GetObject(db.Dimstyle, OpenMode.ForRead) is DimStyleTableRecord dimStyle && !dimStyle.Dimtxsty.IsNull)
                styleId = dimStyle.Dimtxsty;

            using var probe = new DBText { TextString = sample, Height = height, TextStyleId = styleId };
            var ext = probe.GeometricExtents;
            tr.Commit();

            var width = ext.MaxPoint.X - ext.MinPoint.X;
            var factor = width / (sample.Length * height);

            // A nonsense measurement is worse than the fallback.
            return factor is > 0.2 and < 2.0 ? factor : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static PlateLayerOptions Clone(PlateLayerOptions src, int maxPerPage) => new()
    {
        ExcludedLayers = src.ExcludedLayers,
        GapTolerance = src.GapTolerance,
        SheetOutlineMinChildren = src.SheetOutlineMinChildren,
        TemplateLayout = src.TemplateLayout,
        TemplatePath = src.TemplatePath,
        DrawableArea = src.DrawableArea,
        Gutter = src.Gutter,
        ViewportMargin = src.ViewportMargin,
        DimLineOffset = src.DimLineOffset,
        DimTextHeight = src.DimTextHeight,
        LabelTextHeight = src.LabelTextHeight,
        TextPad = src.TextPad,
        TextWidthFactor = src.TextWidthFactor,
        TextClearance = src.TextClearance,
        Policy = src.Policy,
        MinTypicalPartPaperSize = src.MinTypicalPartPaperSize,
        ShowPartLabels = src.ShowPartLabels,
        ShowScaleNotes = src.ShowScaleNotes,
        PlateNameText = src.PlateNameText,
        LayerPrefix = src.LayerPrefix,
        MaterialCodes = src.MaterialCodes,
        MaterialQualifiers = src.MaterialQualifiers,
        CenterOnPlate = src.CenterOnPlate,
        ScaleFloorDenominator = src.ScaleFloorDenominator,
        ScaleMode = src.ScaleMode,
        MaxPartsPerPage = maxPerPage,
        OneMaterialPerPlate = src.OneMaterialPerPlate,
        DimStrategy = src.DimStrategy,
        ApprovalMode = src.ApprovalMode,
        ViewportLayer = src.ViewportLayer,
        DimLayer = src.DimLayer,
        LabelLayer = src.LabelLayer,
    };

    /// <summary>OPEN D2-C: prompt, but pre-filter, so a sloppy window over the title block still works.</summary>
    private static List<ObjectId>? PromptForGeometry(Editor ed)
    {
        var filter = new SelectionFilter(new[]
        {
            new TypedValue((int)DxfCode.Operator, "<OR"),
            new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
            new TypedValue((int)DxfCode.Start, "POLYLINE"),
            new TypedValue((int)DxfCode.Start, "LINE"),
            new TypedValue((int)DxfCode.Start, "ARC"),
            new TypedValue((int)DxfCode.Start, "CIRCLE"),
            new TypedValue((int)DxfCode.Start, "ELLIPSE"),
            new TypedValue((int)DxfCode.Start, "SPLINE"),
            new TypedValue((int)DxfCode.Operator, "OR>"),
        });

        var opts = new PromptSelectionOptions
        {
            MessageForAdding = "\nSelect part geometry (Enter for everything in model space)"
        };

        var res = ed.GetSelection(opts, filter);
        if (res.Status == PromptStatus.Cancel) return null;
        if (res.Status != PromptStatus.OK)
        {
            res = ed.SelectAll(filter);
            if (res.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return null; }
        }
        return res.Value.GetObjectIds().ToList();
    }

    private static bool Confirm(Editor ed, string message, bool defaultYes)
    {
        var pko = new PromptKeywordOptions($"\n{message}") { AllowNone = true };
        AddKeywords(pko, nameof(Prompts.YesNo), Prompts.YesNo, defaultYes ? "Yes" : "No");
        var r = ed.GetKeywords(pko);
        return r.Status == PromptStatus.OK && r.StringResult == "Yes";
    }
}
