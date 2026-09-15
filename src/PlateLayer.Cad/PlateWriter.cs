using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PlateLayer.Core;

namespace PlateLayer.Cad;

/// <summary>
/// RunPlan -> real plates. One transaction per page, committed before the operator is asked about it:
/// a page that exists only inside an open transaction cannot be looked at.
/// </summary>
public sealed class PlateWriter
{
    private readonly Document _doc;
    private readonly Database _db;
    private readonly PlateLayerOptions _opt;

    public PlateWriter(Document doc, PlateLayerOptions opt)
    {
        _doc = doc;
        _db = doc.Database;
        _opt = opt;
    }

    /// <summary>Creates one plate. Returns the layout name so it can be deleted again on Redo/Skip.</summary>
    public string WritePage(PagePlan page, int totalPages)
    {
        var lm = LayoutManager.Current;
        var template = ResolveTemplateLayout();

        var layoutName = UniqueLayoutName(page.SheetName);
        lm.CloneLayout(template, layoutName, page.Index + 1);

        // Viewport.On only takes effect while its layout is current. This is also why the write
        // loop is per-page rather than one pass over every page.
        lm.CurrentLayout = layoutName;

        using var tr = _doc.TransactionManager.StartTransaction();

        EnsureLayer(tr, _opt.ViewportLayer, plottable: false);
        EnsureLayer(tr, _opt.DimLayer, plottable: true);
        EnsureLayer(tr, _opt.LabelLayer, plottable: true);

        var layout = (Layout)tr.GetObject(lm.GetLayoutId(layoutName), OpenMode.ForRead);
        var space = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);

        PurgeClonedContent(tr, space);

        foreach (var tile in page.Tiles)
        {
            CreateViewport(tr, space, tile);
            Annotate(tr, space, tile);
        }

        FillTitleBlock(tr, space, page, totalPages);

        tr.Commit();
        return layoutName;
    }

    /// <summary>
    /// After all pages are accepted, stamp 'nn of NN'. Done in a final sweep because Skip and
    /// Redo renumber the run — writing NN during page creation would mean rewriting every earlier
    /// plate's title block each time the operator drops a page.
    /// </summary>
    public void StampPageNumbers(IReadOnlyList<string> layoutNames)
    {
        var lm = LayoutManager.Current;
        using var tr = _doc.TransactionManager.StartTransaction();
        for (var i = 0; i < layoutNames.Count; i++)
        {
            var layout = (Layout)tr.GetObject(lm.GetLayoutId(layoutNames[i]), OpenMode.ForRead);
            var space = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            SetAttributes(tr, space, TitleBlock.PageNumberValues(i + 1, layoutNames.Count, _opt));
        }
        tr.Commit();
    }

    /// <summary>
    /// Existing layout matching <see cref="PlateLayerOptions.TemplateLayout"/>: exact, then
    /// case-insensitive, then a unique prefix match in either direction. Throws naming every layout
    /// in the drawing, because "not found" without the list is a guessing game.
    /// </summary>
    public string ResolveTemplateLayout()
    {
        var wanted = _opt.TemplateLayout?.Trim() ?? "";
        var names = LayoutNames();

        var hit = names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.Ordinal))
               ?? names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;

        var near = names.Where(n =>
                n.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) ||
                wanted.StartsWith(n, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (near.Count == 1)
        {
            LastTemplateNote = $"Template layout '{wanted}' not found; using '{near[0]}'.";
            return near[0];
        }

        var listed = string.Join(", ", names.Select(n => $"'{n}'"));
        throw new InvalidOperationException(near.Count > 1
            ? $"Template layout '{wanted}' is ambiguous — {string.Join(", ", near.Select(n => $"'{n}'"))} all match. Pick one with PLLSET."
            : $"Template layout '{wanted}' is not in this drawing. Layouts present: {listed}. Set it with PLLSET.");
    }

    /// <summary>Set when the template was resolved by anything other than an exact match.</summary>
    public string? LastTemplateNote { get; private set; }

    private List<string> LayoutNames()
    {
        var names = new List<string>();
        using var tr = _doc.TransactionManager.StartTransaction();
        var dict = (DBDictionary)tr.GetObject(_db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (var entry in dict)
            if (!string.Equals(entry.Key, "Model", StringComparison.OrdinalIgnoreCase))
                names.Add(entry.Key);
        tr.Commit();
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public void DeleteLayout(string layoutName)
    {
        // Deterministic undo of one page. Undo marks issued mid-command fight the command's own
        // undo record; deleting the layout does not.
        try { LayoutManager.Current.DeleteLayout(layoutName); } catch { /* already gone */ }
    }

    // ---------------------------------------------------------------- viewport

    private void CreateViewport(Transaction tr, BlockTableRecord space, Tile tile)
    {
        var vp = new Viewport { Layer = _opt.ViewportLayer };
        space.AppendEntity(vp);
        tr.AddNewlyCreatedDBObject(vp, true);

        // The window is the part plus a margin on every side. Sized to the part exactly, the part's
        // outline lands on the clip boundary and on the non-plotting frame, and plots as nothing.
        var centre = tile.ViewportCenter;
        vp.Width = tile.ViewportW;
        vp.Height = tile.ViewportH;
        vp.CenterPoint = new Point3d(centre.X, centre.Y, 0);

        vp.On = true;                                   // layout is current; see WritePage
        vp.CustomScale = tile.Rung.PaperPerModel;       // paper units per model unit
        vp.ViewCenter = new Point2d(tile.ModelCenterX, tile.ModelCenterY);
        vp.Locked = true;                               // closes the "unlock, rescale, dims now lie" hole
    }

    // ---------------------------------------------------------------- annotation

    private void Annotate(Transaction tr, BlockTableRecord space, Tile tile)
    {
        // Dimensions measure the PART, not the viewport frame, so they anchor to the part rect —
        // which is inset from the frame by the margin.
        var part = tile.PartRect;
        var frameTop = tile.ViewportOrigin.Y + tile.ViewportH;
        var labelY = frameTop + _opt.TextPad * 0.4;

        if (_opt.ShowPartLabels)
            AddText(tr, space, tile.Label,
                new Point3d(tile.ViewportOrigin.X, labelY, 0),
                _opt.LabelTextHeight, AttachmentPoint.BottomLeft, _opt.LabelLayer);

        if (_opt.ShowScaleNotes)
            AddText(tr, space, $"SCALE {tile.Rung.Name}",
                new Point3d(tile.ViewportOrigin.X + tile.ViewportW, labelY, 0),
                _opt.LabelTextHeight * 0.72, AttachmentPoint.BottomRight, _opt.LabelLayer);

        if (_opt.DimStrategy == DimStrategy.MTextOnly)
        {
            AddText(tr, space, ArchFormat.Inches(tile.WModel, _opt),
                new Point3d(part.Center.X, part.MinY - tile.DimOffsetX, 0),
                _opt.DimTextHeight, AttachmentPoint.TopCenter, _opt.DimLayer);
            AddText(tr, space, ArchFormat.Inches(tile.HModel, _opt),
                new Point3d(part.MinX - tile.DimOffsetY, part.Center.Y, 0),
                _opt.DimTextHeight, AttachmentPoint.MiddleCenter, _opt.DimLayer);
            return;
        }

        // Real DIMENSION entities carrying an explicit text override holding the true model
        // measurement. Native look; the number cannot drift from the part, because it is not
        // derived from the paper distance at all.
        AddDimension(tr, space, rotation: 0,
            new Point3d(part.MinX, part.MinY, 0),
            new Point3d(part.MaxX, part.MinY, 0),
            new Point3d(part.Center.X, part.MinY - tile.DimOffsetX, 0),
            ArchFormat.Inches(tile.WModel, _opt));

        AddDimension(tr, space, rotation: Math.PI / 2,
            new Point3d(part.MinX, part.MinY, 0),
            new Point3d(part.MinX, part.MaxY, 0),
            new Point3d(part.MinX - tile.DimOffsetY, part.Center.Y, 0),
            ArchFormat.Inches(tile.HModel, _opt));
    }

    private void AddDimension(Transaction tr, BlockTableRecord space, double rotation,
                              Point3d p1, Point3d p2, Point3d dimLine, string text)
    {
        var dim = new RotatedDimension(rotation, p1, p2, dimLine, text, _db.Dimstyle)
        {
            Layer = _opt.DimLayer,
            Dimscale = 1.0          // paper space: the dimension is already at plot size
        };
        space.AppendEntity(dim);
        tr.AddNewlyCreatedDBObject(dim, true);
    }

    private void AddText(Transaction tr, BlockTableRecord space, string contents, Point3d at,
                         double height, AttachmentPoint attach, string layer, double rotation = 0)
    {
        var mt = new MText
        {
            Contents = contents,
            Location = at,
            TextHeight = height,
            Attachment = attach,
            Rotation = rotation,
            Layer = layer
        };
        space.AppendEntity(mt);
        tr.AddNewlyCreatedDBObject(mt, true);
    }

    // ---------------------------------------------------------------- title block

    private void FillTitleBlock(Transaction tr, BlockTableRecord space, PagePlan page, int totalPages) =>
        SetAttributes(tr, space, TitleBlock.Values(page, totalPages, _opt, DateTime.Now));

    private static void SetAttributes(Transaction tr, BlockTableRecord space, IDictionary<string, string> values)
    {
        foreach (var id in space)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br) continue;
            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (tr.GetObject(attId, OpenMode.ForRead) is not AttributeReference att) continue;
                if (!values.TryGetValue(att.Tag, out var value)) continue;
                att.UpgradeOpen();
                att.TextString = value;
            }
        }
    }

    // ---------------------------------------------------------------- plumbing

    private void PurgeClonedContent(Transaction tr, BlockTableRecord space)
    {
        // A cloned layout carries the template's own viewports, dims and notes. Keep the block
        // references (border and title block live inside one) and the overall paper-space viewport;
        // erase the rest so the plate starts empty.
        var ids = space.Cast<ObjectId>().ToList();   // snapshot: erasing while enumerating is not safe

        // The overall paper-space viewport must survive or the layout stops displaying. It is
        // Number 1 once the layout has been activated; before that, fall back to the largest.
        ObjectId keepViewport = ObjectId.Null;
        var bestArea = -1.0;
        foreach (var id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Viewport vp) continue;
            if (vp.Number == 1) { keepViewport = id; break; }
            var area = vp.Width * vp.Height;
            if (area > bestArea) { bestArea = area; keepViewport = id; }
        }

        foreach (var id in ids)
        {
            if (id == keepViewport) continue;
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
            if (ent is BlockReference && !string.Equals(ent.Layer, _opt.DimLayer, StringComparison.OrdinalIgnoreCase)) continue;

            ent.UpgradeOpen();
            ent.Erase();
        }
    }

    /// <summary>
    /// Layers the plugin had to create because the drawing did not define them. Reported at the end
    /// of a run: these are the shop's own convention layers, so having to invent one means the
    /// drawing was not set up from the usual template and its colour and plot settings are guesses.
    /// </summary>
    public IReadOnlyList<string> CreatedLayers => _createdLayers;
    private readonly List<string> _createdLayers = new();

    private void EnsureLayer(Transaction tr, string name, bool plottable)
    {
        var lt = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(name)) return;            // never modify a layer the drawing already defines

        lt.UpgradeOpen();
        var ltr = new LayerTableRecord
        {
            Name = name,
            IsPlottable = plottable,
            Color = Color.FromColorIndex(ColorMethod.ByAci, 7)
        };
        lt.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);

        if (!_createdLayers.Contains(name, StringComparer.OrdinalIgnoreCase)) _createdLayers.Add(name);
    }

    private string UniqueLayoutName(string preferred)
    {
        var names = LayoutNames();
        if (!names.Contains(preferred, StringComparer.OrdinalIgnoreCase)) return preferred;
        for (var i = 2; ; i++)
        {
            var candidate = $"{preferred} ({i})";
            if (!names.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
    }
}
