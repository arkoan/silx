using Silx.Core.Raw;
namespace Silx.Etude;

/// <summary>
/// Signaux tracés : une tension de nœud (« OUT ») ou un courant de broche (« I(R1:2) » : courant qui entre dans la
/// broche 2 de R1). Les courants sont lus dans les vecteurs que ngspice enregistre avec « .options savecurrents ».
/// </summary>
public static class Signaux
{
    public static bool EstCourant(string s) => s.StartsWith("I(", StringComparison.Ordinal) && s.EndsWith(')');

    /// <summary>Courant entrant dans la broche <paramref name="broche"/> (0 = première) d'un composant.</summary>
    public static string Courant(PlacedComponent k, int broche) => $"I({k.Ref}:{broche + 1})";

    static (string Ref, int Broche)? Lire(string s)
    {
        if (!EstCourant(s)) return null;
        var c = s[2..^1]; int i = c.LastIndexOf(':');
        return i > 0 && int.TryParse(c[(i + 1)..], out var b) ? (c[..i], b - 1) : (c, 0);
    }

    /// <summary>Nom affiché : « V(OUT) », « I(R1) », « I(R1) broche 2 », « I(Q1, C) ».</summary>
    public static string Nom(string s, Schematic? f = null)
    {
        if (Lire(s) is not { } x) return $"V({s})";
        var k = f?.Components.FirstOrDefault(c => string.Equals(c.Ref, x.Ref, StringComparison.OrdinalIgnoreCase));
        if (k?.Kind == ComponentKind.Spice && k.Definition?.Symbole.Broches.ElementAtOrDefault(x.Broche) is { } b)
            return $"I({x.Ref}, {k.Definition.NomBroche(b)})";
        return x.Broche == 0 ? $"I({x.Ref})" : $"I({x.Ref}) broche {x.Broche + 1}";
    }

    public static string Unite(string s) => EstCourant(s) ? "A" : "V";

    /// <summary>Vecteur ngspice d'un signal et signe à lui appliquer (courant entrant dans la broche). Null si indisponible.</summary>
    public static (string Vecteur, double Signe)? Vecteur(string s, Schematic? f)
    {
        if (Lire(s) is not { } x) return ($"v({s})", 1);
        var k = f?.Components.FirstOrDefault(c => string.Equals(c.Ref, x.Ref, StringComparison.OrdinalIgnoreCase));
        if (k == null) return null;
        var r = k.Ref.ToLowerInvariant();
        double deux = x.Broche == 0 ? 1 : -1;   // dipôle : le courant entre par la broche 1 et sort par la 2
        switch (k.Kind)
        {
            case ComponentKind.Resistor or ComponentKind.Capacitor or ComponentKind.Inductor: return ($"i(@{r}[i])", deux);
            case ComponentKind.VoltageSource: return ($"i({r})", deux);
            case ComponentKind.CurrentSource: return ($"i(@{r}[current])", deux);
            case ComponentKind.Diode: return ($"i(@{r}[id])", deux);
            case ComponentKind.Spice when k.Definition is { } d && d.Symbole.Broches.ElementAtOrDefault(x.Broche)?.BrocheModele is { } bm:
                var role = d.Symbole.Broches[x.Broche].Role ?? bm;
                return d.Lettre switch
                {
                    "D" => ($"i(@{r}[id])", role.Equals("K", StringComparison.OrdinalIgnoreCase) ? -1 : 1),
                    "Q" or "M" or "J" => ($"i(@{r}[i{role.ToLowerInvariant()}])", 1),   // ic, ib, ie ; id, ig, is, ib
                    _ => null,   // sous-circuit, composant logique : pas de courant de broche
                };
            default: return null;
        }
    }

    public static bool Disponible(RawFile raw, string s, Schematic? f) =>
        Vecteur(s, f) is { } v && raw.Variables.Any(x => string.Equals(x.Name, v.Vecteur, StringComparison.OrdinalIgnoreCase));
}
