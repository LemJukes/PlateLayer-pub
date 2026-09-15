namespace PlateLayer.Core.Tests;

/// <summary>
/// An invented cut file, written for this test suite. Nothing here is measured off a real
/// drawing: the parts are round numbers on a 96 x 48 sheet, arranged the way a PartLayer run
/// arranges them — three material columns, each enclosed by a sheet-group rectangle on
/// <c>rOJECTS</c>, with one part carrying a cutout.
///
/// It exists so the layout and planning invariants can be asserted against a cut file without a
/// real job's geometry being part of the test data. Where a test needs a specific number, that
/// number describes THIS file and nothing else.
/// </summary>
public static class SampleCutFile
{
    public const int ExpectedParts = 13;

    static Curve2D P(string id, string layer, double x, double y, double w, double h) =>
        new(id, layer, CurveKind.Polyline, new BBox(x, y, x + w, y + h), true);

    public static readonly IReadOnlyList<Curve2D> Curves = new Curve2D[]
    {
        // Sheet-group rectangles: one per material column, not parts.
        P("s1", "rOJECTS",   0, 0, 100, 150),
        P("s2", "rOJECTS", 120, 0, 100, 110),
        P("s3", "rOJECTS", 240, 0, 100,  30),

        // rPLY_3-4 — six parts, largest first down the column.
        P("a1", "rPLY_3-4",  4,   4, 90, 40),
        P("a2", "rPLY_3-4",  4,  50, 90, 20),
        P("a3", "rPLY_3-4",  4,  76, 44, 24),
        P("a4", "rPLY_3-4", 54,  76, 44, 24),
        P("a5", "rPLY_3-4",  4, 106, 30, 18),
        P("a6", "rPLY_3-4",  4, 130, 30, 18),
        // ...and a cutout inside a1, which must join its parent rather than count as a part.
        P("a1h", "rPLY_3-4", 20, 16, 10, 10),

        // rPLY_1-2 — five parts.
        P("b1", "rPLY_1-2", 124,   4, 72, 36),
        P("b2", "rPLY_1-2", 124,  46, 72, 12),
        P("b3", "rPLY_1-2", 124,  64, 36, 24),
        P("b4", "rPLY_1-2", 166,  64, 36, 24),
        P("b5", "rPLY_1-2", 124,  94, 24, 12),

        // rPLY_1-4 — two parts, the sparse material.
        P("c1", "rPLY_1-4", 244,  4, 90, 10),
        P("c2", "rPLY_1-4", 244, 18, 90, 10),
    };
}
