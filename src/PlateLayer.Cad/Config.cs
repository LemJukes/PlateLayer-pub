using Autodesk.AutoCAD.DatabaseServices;
using PlateLayer.Core;

namespace PlateLayer.Cad;

/// <summary>
/// Prototype settings store: per-drawing values in the named object dictionary, everything else
/// from defaults. OPEN D13 asks for per-user defaults with per-drawing overrides; that split is
/// not built yet, and this is the seam it will go through.
/// </summary>
public static class Config
{
    private const string DictName = "PLATELAYER_SETTINGS";

    public static PlateLayerOptions Load(Database db)
    {
        var opt = new PlateLayerOptions();
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (nod.Contains(DictName) &&
            tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead) is Xrecord { Data: not null } xr)
        {
            foreach (var tv in xr.Data)
            {
                var text = tv.Value?.ToString() ?? "";
                var split = text.IndexOf('=');
                if (split <= 0) continue;
                Apply(opt, text[..split], text[(split + 1)..]);
            }
        }
        tr.Commit();
        return opt;
    }

    public static void Save(Database db, PlateLayerOptions opt)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

        var rb = new ResultBuffer(
            Kv("TemplateLayout", opt.TemplateLayout),
            Kv("MaxPartsPerPage", opt.MaxPartsPerPage.ToString()),
            Kv("Gutter", opt.Gutter.ToString("R")),
            Kv("ViewportMargin", opt.ViewportMargin.ToString("R")),
            Kv("DimLineOffset", opt.DimLineOffset.ToString("R")),
            Kv("CenterOnPlate", opt.CenterOnPlate.ToString()),
            Kv("ScaleMode", opt.ScaleMode.ToString()),
            Kv("DimStrategy", opt.DimStrategy.ToString()),
            Kv("ApprovalMode", opt.ApprovalMode.ToString()),
            Kv("ScaleFloor", opt.ScaleFloorDenominator.ToString()),
            Kv("Policy", opt.Policy.ToString()),
            Kv("MinPartPaperDimension", opt.MinPartPaperDimension.ToString("R")),
            Kv("DrawableArea", $"{opt.DrawableArea.MinX},{opt.DrawableArea.MinY},{opt.DrawableArea.MaxX},{opt.DrawableArea.MaxY}"),
            Kv("ShowPartLabels", opt.ShowPartLabels.ToString()),
            Kv("ShowScaleNotes", opt.ShowScaleNotes.ToString()),
            Kv("PlateNameText", opt.PlateNameText),
            Kv("LayerPrefix", opt.LayerPrefix));

        foreach (var kv in opt.MaterialCodes) rb.Add(Kv("Material:" + kv.Key, kv.Value));
        foreach (var kv in opt.MaterialQualifiers) rb.Add(Kv("Qualifier:" + kv.Key, kv.Value));

        var xr = new Xrecord { Data = rb };
        if (nod.Contains(DictName)) nod.Remove(DictName);
        nod.SetAt(DictName, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
        tr.Commit();
    }

    private static TypedValue Kv(string key, string value) => new((int)DxfCode.Text, $"{key}={value}");

    private static void Apply(PlateLayerOptions opt, string key, string value)
    {
        switch (key)
        {
            case "TemplateLayout": opt.TemplateLayout = value; break;
            case "MaxPartsPerPage" when int.TryParse(value, out var n): opt.MaxPartsPerPage = n; break;
            case "ScaleFloor" when int.TryParse(value, out var f): opt.ScaleFloorDenominator = f; break;
            case "Policy" when Enum.TryParse<ScalePolicy>(value, out var pol): opt.Policy = pol; break;
            // Older drawings stored a boolean; honour it rather than silently reverting to default.
            case "CoarsenForDensity" when bool.TryParse(value, out var cd): opt.CoarsenForDensity = cd; break;
            case "MinPartPaperDimension" when double.TryParse(value, out var mp): opt.MinPartPaperDimension = mp; break;
            case "Gutter" when double.TryParse(value, out var g): opt.Gutter = g; break;
            case "ViewportMargin" when double.TryParse(value, out var vm): opt.ViewportMargin = vm; break;
            case "DimLineOffset" when double.TryParse(value, out var dl): opt.DimLineOffset = dl; break;
            case "CenterOnPlate" when bool.TryParse(value, out var cp): opt.CenterOnPlate = cp; break;
            case "ScaleMode" when Enum.TryParse<ScaleMode>(value, out var sm): opt.ScaleMode = sm; break;
            case "DimStrategy" when Enum.TryParse<DimStrategy>(value, out var ds): opt.DimStrategy = ds; break;
            case "ApprovalMode" when Enum.TryParse<ApprovalMode>(value, out var am): opt.ApprovalMode = am; break;
            case "ShowPartLabels" when bool.TryParse(value, out var spl): opt.ShowPartLabels = spl; break;
            case "ShowScaleNotes" when bool.TryParse(value, out var ssn): opt.ShowScaleNotes = ssn; break;
            case "PlateNameText": opt.PlateNameText = value; break;
            case "LayerPrefix": opt.LayerPrefix = value; break;
            case var m when m.StartsWith("Material:", StringComparison.Ordinal):
                opt.MaterialCodes[m["Material:".Length..]] = value; break;
            case var q when q.StartsWith("Qualifier:", StringComparison.Ordinal):
                opt.MaterialQualifiers[q["Qualifier:".Length..]] = value; break;
            case "DrawableArea":
            {
                var p = value.Split(',');
                if (p.Length == 4 && p.All(x => double.TryParse(x, out _)))
                    opt.DrawableArea = new BBox(double.Parse(p[0]), double.Parse(p[1]), double.Parse(p[2]), double.Parse(p[3]));
                break;
            }
        }
    }
}
