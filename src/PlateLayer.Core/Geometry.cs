namespace PlateLayer.Core;

public readonly record struct Pt(double X, double Y);

/// <summary>Axis-aligned bounding box in model units. Immutable.</summary>
public readonly record struct BBox(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width  => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double Area   => Width * Height;
    public Pt Center     => new((MinX + MaxX) / 2.0, (MinY + MaxY) / 2.0);

    public static BBox FromPoints(IEnumerable<Pt> pts)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        var any = false;
        foreach (var p in pts)
        {
            any = true;
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        if (!any) throw new ArgumentException("No points supplied.", nameof(pts));
        return new BBox(minX, minY, maxX, maxY);
    }

    public BBox Union(BBox o) =>
        new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));

    /// <summary>True when the boxes overlap, or sit within <paramref name="tol"/> of each other on both axes.</summary>
    public bool Touches(BBox o, double tol) =>
        MinX - tol <= o.MaxX && o.MinX - tol <= MaxX &&
        MinY - tol <= o.MaxY && o.MinY - tol <= MaxY;

    /// <summary>True when <paramref name="o"/> lies wholly inside this box.</summary>
    public bool Contains(BBox o, double tol = 0.0) =>
        o.MinX >= MinX - tol && o.MaxX <= MaxX + tol &&
        o.MinY >= MinY - tol && o.MaxY <= MaxY + tol;

    public override string ToString() =>
        $"[{MinX:0.###},{MinY:0.###} .. {MaxX:0.###},{MaxY:0.###}]";
}

public enum CurveKind { Line, Arc, Circle, Polyline, Ellipse, Spline, Other }

/// <summary>
/// One source curve, already reduced to what Core needs. Deliberately free of any AutoCAD type:
/// the Cad shell is responsible for tessellation and for handing over a trustworthy <see cref="Extents"/>.
/// </summary>
public sealed record Curve2D(
    string Id,
    string Layer,
    CurveKind Kind,
    BBox Extents,
    bool IsClosed,
    IReadOnlyList<Pt>? Sample = null);
