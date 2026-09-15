using System.Text;

namespace PlateLayer.Core;

/// <summary>
/// Architectural feet-inches, matching the reference drawing's UNITS (LUNITS=4).
/// Used for dimension text, which is written explicitly rather than left to DIMLFAC —
/// a locked viewport plus a literal string cannot silently disagree about a part's real size.
/// </summary>
public static class ArchFormat
{
    /// <summary>MText stacked-fraction wrapper: \S numerator / denominator ;</summary>
    private const string StackOpen = "\\S";
    private const char StackClose = ';';

    /// <summary>Plain text, e.g. <c>3'-5 3/4"</c>. Used for reports, logs and width estimates.</summary>
    public static string Inches(double value, int denominator = 16) => Format(value, denominator, stacked: false);

    /// <summary>
    /// MText form, e.g. <c>3'-5\S3/4;"</c>, which AutoCAD renders with the fraction stacked exactly
    /// as it does for one typed by hand.
    /// </summary>
    public static string InchesMText(double value, int denominator = 16) => Format(value, denominator, stacked: true);

    public static string Inches(double value, PlateLayerOptions opt) =>
        Format(value, 16, opt.StackedFractions);

    private static string Format(double value, int denominator, bool stacked)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "?";
        var neg = value < 0;
        value = Math.Abs(value);

        var totalSixteenths = (long)Math.Round(value * denominator, MidpointRounding.AwayFromZero);
        var whole = totalSixteenths / denominator;
        var num = totalSixteenths % denominator;
        var den = denominator;
        while (num != 0 && num % 2 == 0) { num /= 2; den /= 2; }

        var feet = whole / 12;
        var inch = whole % 12;

        var sb = new StringBuilder();
        if (neg) sb.Append('-');
        if (feet > 0) sb.Append(feet).Append("'-");

        // Architectural units drop the leading zero: half an inch reads 1/2", not 0 1/2".
        var showInches = inch != 0 || feet > 0 || num == 0;
        if (showInches) sb.Append(inch);

        if (num != 0)
        {
            if (stacked)
            {
                // No space before a stacked fraction — it sits tight against the inches, the way
                // AutoCAD sets one typed at the keyboard.
                sb.Append(StackOpen).Append(num).Append('/').Append(den).Append(StackClose);
            }
            else
            {
                if (showInches) sb.Append(' ');
                sb.Append(num).Append('/').Append(den);
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    public static string Size(double w, double h) => $"{Inches(w)} x {Inches(h)}";
}
