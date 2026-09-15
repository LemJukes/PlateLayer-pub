using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using PlateLayer.Core;

namespace PlateLayer.Cad;

/// <summary>Model-space entities -> Core's <see cref="Curve2D"/>. The only place AutoCAD geometry is read.</summary>
public static class CurveReader
{
    private const double ZTolerance = 1e-6;

    public static List<Curve2D> Read(Transaction tr, IEnumerable<ObjectId> ids, Editor ed, out int skipped)
    {
        var curves = new List<Curve2D>();
        var skippedCount = 0;

        foreach (var id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) { skippedCount++; continue; }

            var kind = Classify(ent);
            if (kind is null) { skippedCount++; continue; }

            if (!TryExtents(ent, out var ext)) { skippedCount++; continue; }

            if (Math.Abs(ext.MinPoint.Z) > ZTolerance || Math.Abs(ext.MaxPoint.Z) > ZTolerance)
            {
                ed.WriteMessage($"\n  skipped {ent.GetType().Name} on '{ent.Layer}': not on Z=0.");
                skippedCount++;
                continue;
            }

            curves.Add(new Curve2D(
                Id: id.Handle.ToString(),
                Layer: ent.Layer,
                Kind: kind.Value,
                Extents: new BBox(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y),
                IsClosed: ent is Curve c && c.Closed));
        }

        skipped = skippedCount;
        return curves;
    }

    private static CurveKind? Classify(Entity ent) => ent switch
    {
        Line => CurveKind.Line,
        Arc => CurveKind.Arc,
        Circle => CurveKind.Circle,
        Polyline or Polyline2d or Polyline3d => CurveKind.Polyline,
        Ellipse => CurveKind.Ellipse,
        Spline => CurveKind.Spline,
        _ => null   // TEXT, MTEXT, DIMENSION, INSERT, 3DSOLID, IMAGE: not part candidates
    };

    /// <summary>
    /// GeometricExtents throws on degenerate entities, and on splines it can report the
    /// control-polygon hull rather than the curve. Sample the curve when that matters.
    /// </summary>
    private static bool TryExtents(Entity ent, out Extents3d ext)
    {
        ext = default;
        try
        {
            if (ent is Spline or Ellipse && ent is Curve curve && TrySampledExtents(curve, out ext))
                return true;
            ext = ent.GeometricExtents;
            return true;
        }
        catch
        {
            return ent is Curve c && TrySampledExtents(c, out ext);
        }
    }

    private static bool TrySampledExtents(Curve curve, out Extents3d ext, int samples = 64)
    {
        ext = default;
        try
        {
            var start = curve.StartParam;
            var end = curve.EndParam;
            if (end <= start) return false;

            var acc = new Extents3d(curve.GetPointAtParameter(start), curve.GetPointAtParameter(start));
            for (var i = 1; i <= samples; i++)
                acc.AddPoint(curve.GetPointAtParameter(start + (end - start) * i / samples));

            ext = acc;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
