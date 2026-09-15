using System.Text;

namespace PlateLayer.Core;

/// <summary>
/// Turns a material layer name into something a shop floor reads, e.g. <c>rPLY_3-4</c> becomes
/// <c>3/4" Plywood | rPLY_3-4</c>.
///
/// The layer name is always kept alongside the translation. It is the only value that is certainly
/// correct, it is what the cut file actually says, and it is what anyone cross-referencing the two
/// documents will search for.
///
/// An unrecognised code falls back to the bare layer name. A wrong expansion on a cut sheet is
/// worse than no expansion, so unknown codes are never guessed at.
/// </summary>
public static class MaterialNames
{
    /// <summary>
    /// Shape: <c>[prefix]CODE_THICKNESS[QUALIFIER]</c>, thickness as <c>3-4</c> for 3/4 or a bare
    /// integer for whole inches.
    /// </summary>
    public static string Describe(string layer, PlateLayerOptions opt)
    {
        var expanded = Expand(layer, opt);
        return expanded is null ? layer : $"{expanded} | {layer}";
    }

    /// <summary>The human-readable half only, or null when the layer does not parse.</summary>
    public static string? Expand(string layer, PlateLayerOptions opt)
    {
        if (string.IsNullOrWhiteSpace(layer)) return null;

        var body = layer.Trim();
        if (opt.LayerPrefix.Length > 0 && body.StartsWith(opt.LayerPrefix, StringComparison.Ordinal))
            body = body[opt.LayerPrefix.Length..];

        var split = body.IndexOf('_');
        var code = split < 0 ? body : body[..split];
        var rest = split < 0 ? "" : body[(split + 1)..];

        if (!opt.MaterialCodes.TryGetValue(code, out var material)) return null;

        var (thickness, qualifierCode) = ParseThickness(rest);
        var qualifier = qualifierCode.Length > 0
            ? opt.MaterialQualifiers.TryGetValue(qualifierCode, out var q) ? q : qualifierCode
            : "";

        var sb = new StringBuilder();
        if (thickness is not null) sb.Append(thickness).Append(' ');
        if (qualifier.Length > 0) sb.Append(qualifier).Append(' ');
        sb.Append(material);
        return sb.ToString();
    }

    private static (string? Thickness, string Qualifier) ParseThickness(string rest)
    {
        if (rest.Length == 0) return (null, "");

        var i = 0;
        while (i < rest.Length && (char.IsDigit(rest[i]) || rest[i] == '-')) i++;
        var numeric = rest[..i].Trim('-');
        var qualifier = rest[i..];

        if (numeric.Length == 0) return (null, qualifier);

        var parts = numeric.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 when int.TryParse(parts[0], out var whole) => ($"{whole}\"", qualifier),
            2 when int.TryParse(parts[0], out var n) && int.TryParse(parts[1], out var d) && d != 0 =>
                ($"{n}/{d}\"", qualifier),
            // 1-1-2 style: whole plus fraction.
            3 when int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var n2)
                   && int.TryParse(parts[2], out var d2) && d2 != 0 => ($"{w} {n2}/{d2}\"", qualifier),
            _ => (null, rest)
        };
    }
}
