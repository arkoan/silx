using System.Globalization;
namespace Silx.Etude;

/// <summary>Nombres en notation ingénieur (10k, 4,7u, 2meg, 1e-3) ; unité finale tolérée (Hz, s, V, °, %).</summary>
public static class SpiceNumber
{
    static readonly (string Suffix, double Factor)[] Suffixes =
        [("meg", 1e6), ("t", 1e12), ("g", 1e9), ("k", 1e3), ("m", 1e-3), ("u", 1e-6), ("µ", 1e-6), ("n", 1e-9), ("p", 1e-12), ("f", 1e-15)];

    public static bool TryParse(string? text, out double value)
    {
        value = double.NaN;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().Replace(',', '.').Replace(" ", "");
        foreach (var unit in new[] { "hz", "s", "v", "a", "°", "%", "db" })
            if (s.Length > unit.Length && s.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            { s = s[..^unit.Length]; break; }
        int end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] is '.' or '-' or '+' || (s[end] is 'e' or 'E' && end + 1 < s.Length && (char.IsDigit(s[end + 1]) || s[end + 1] is '-' or '+')))) end++;
        if (!double.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return false;
        var rest = s[end..].ToLowerInvariant();
        if (rest.Length == 0) { value = num; return true; }
        foreach (var (suf, f) in Suffixes)
            if (rest.StartsWith(suf)) { value = num * f; return true; }
        return false;
    }

    public static double? ParseOrNull(string? text) => TryParse(text, out var v) ? v : null;

    static readonly (double Factor, string Prefix)[] Prefixes =
        [(1e9, "G"), (1e6, "M"), (1e3, "k"), (1, ""), (1e-3, "m"), (1e-6, "µ"), (1e-9, "n"), (1e-12, "p"), (1e-15, "f")];

    /// <summary>Affichage ingénieur à 3 chiffres significatifs, virgule décimale : 2,20 ms ; 159 Hz.</summary>
    public static string Format(double v, string unit = "")
    {
        if (double.IsNaN(v)) return "—";
        if (Math.Abs(v) < 5e-15 || (unit is "%" or "°" or "dB" or "V/V" or "ratio" && Math.Abs(v) < 0.005)) v = 0;   // pas de « -0,00 »
        if (unit is "%" or "°" or "dB" or "V/V" or "ratio" || v == 0)
            return v.ToString(Math.Abs(v) >= 100 ? "0" : Math.Abs(v) >= 10 ? "0.0" : "0.00", CultureInfo.GetCultureInfo("fr-FR")) + (unit.Length > 0 ? " " + unit : "");
        var a = Math.Abs(v);
        var (f, p) = Prefixes.FirstOrDefault(x => a >= x.Factor * 0.9995, Prefixes[^1]);
        var m = v / f;
        var fmt = Math.Abs(m) >= 100 ? "0" : Math.Abs(m) >= 10 ? "0.0" : "0.00";
        return m.ToString(fmt, CultureInfo.GetCultureInfo("fr-FR")) + " " + p + unit;
    }
}
