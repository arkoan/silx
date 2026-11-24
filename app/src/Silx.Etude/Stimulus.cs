using System.Globalization;
using System.Text.RegularExpressions;
namespace Silx.Etude;

/// <summary>Forme d'un stimulus (source de tension ou de courant) saisi en clair.</summary>
public enum FormeStimulus { Continu, Impulsion, Sinus, PetitSignal, Expression }

/// <summary>
/// Stimulus saisi par champs (valeurs en clair, notation ingénieur) et sa traduction en expression SPICE.
/// Lecture tolérante des expressions existantes ; ce qui ne se lit pas reste en « Expression » (saisie SPICE libre).
/// </summary>
public sealed class Stimulus
{
    public FormeStimulus Forme { get; set; } = FormeStimulus.Continu;
    /// <summary>Valeurs par nom de champ (voir <see cref="Champs"/>).</summary>
    public Dictionary<string, double> V { get; } = new();
    public string Expression { get; set; } = "";

    public sealed record Champ(string Cle, string Libelle, string Unite, double Defaut);

    /// <summary>Champs d'une forme. Unité « V » remplacée par « A » pour une source de courant.</summary>
    public static IReadOnlyList<Champ> Champs(FormeStimulus f) => f switch
    {
        FormeStimulus.Continu => [new("dc", "Valeur", "V", 5)],
        FormeStimulus.Impulsion => [new("v1", "Niveau bas", "V", 0), new("v2", "Niveau haut", "V", 1), new("td", "Retard", "s", 0.5e-3),
            new("tr", "Montée", "s", 1e-6), new("tf", "Descente", "s", 1e-6), new("pw", "Largeur", "s", 20e-3), new("per", "Période", "s", 40e-3)],
        FormeStimulus.Sinus => [new("vo", "Décalage", "V", 0), new("va", "Amplitude", "V", 1), new("f", "Fréquence", "Hz", 1e3), new("td", "Retard", "s", 0)],
        FormeStimulus.PetitSignal => [new("ac", "Amplitude AC", "V", 1), new("ph", "Phase", "°", 0), new("dc", "Polarisation DC", "V", 0)],
        _ => [],
    };

    public static string Libelle(FormeStimulus f) => f switch
    {
        FormeStimulus.Continu => "Continu", FormeStimulus.Impulsion => "Échelon / impulsion", FormeStimulus.Sinus => "Sinus",
        FormeStimulus.PetitSignal => "Petit signal (AC)", _ => "Expression SPICE"
    };

    /// <summary>Une source en petit signal fait un banc fréquentiel ; les autres formes, un banc temporel.</summary>
    public AnalyseKind Analyse => Forme == FormeStimulus.PetitSignal || (Forme == FormeStimulus.Expression && Regex.IsMatch(Expression, @"(^|\s)AC(\s|$)", RegexOptions.IgnoreCase)) ? AnalyseKind.Ac : AnalyseKind.Transitoire;

    public double this[string cle] { get => V.TryGetValue(cle, out var v) ? v : Champs(Forme).FirstOrDefault(c => c.Cle == cle)?.Defaut ?? 0; set => V[cle] = value; }

    public static Stimulus Nouveau(FormeStimulus f)
    {
        var s = new Stimulus { Forme = f };
        foreach (var c in Champs(f)) s.V[c.Cle] = c.Defaut;
        return s;
    }

    static string N(double d) => d.ToString("G6", CultureInfo.InvariantCulture);

    public string VersSpice() => Forme switch
    {
        FormeStimulus.Continu => $"DC {N(this["dc"])}",
        FormeStimulus.Impulsion => $"PULSE({N(this["v1"])} {N(this["v2"])} {N(this["td"])} {N(this["tr"])} {N(this["tf"])} {N(this["pw"])} {N(this["per"])})",
        FormeStimulus.Sinus => $"SIN({N(this["vo"])} {N(this["va"])} {N(this["f"])} {N(this["td"])})",
        FormeStimulus.PetitSignal => $"DC {N(this["dc"])} AC {N(this["ac"])}" + (this["ph"] != 0 ? $" {N(this["ph"])}" : ""),
        _ => Expression.Trim(),
    };

    /// <summary>Résumé lisible pour le schéma : « 5 V », « 0 → 1 V, 20 ms / 40 ms », « 1 V, 1 kHz », « AC 1 V ».</summary>
    public string Resume(bool courant = false)
    {
        string u = courant ? "A" : "V";
        // Format ingénieur sans zéros inutiles : « 1 V », « 20 ms », « 2,5 kHz ».
        string F(string k, string unite) => Compact(SpiceNumber.Format(this[k], unite));
        return Forme switch
        {
            FormeStimulus.Continu => F("dc", u),
            FormeStimulus.Impulsion => $"{F("v1", "")} → {F("v2", u)}, {F("pw", "s")}",
            FormeStimulus.Sinus => $"{F("va", u)} crête, {F("f", "Hz")}" + (this["vo"] != 0 ? $", +{F("vo", u)}" : ""),
            FormeStimulus.PetitSignal => $"AC {F("ac", u)}" + (this["dc"] != 0 ? $" sur {F("dc", u)}" : ""),
            _ => Expression,
        };
    }

    /// <summary>« 1,00 V » → « 1 V », « 2,50 kHz » → « 2,5 kHz », « 0,00 » → « 0 ».</summary>
    static string Compact(string s)
    {
        var m = Regex.Match(s, @"^(-?\d+)(,\d+)?(.*)$");
        if (!m.Success) return s;
        var dec = m.Groups[2].Value.TrimEnd('0'); if (dec == ",") dec = "";
        return m.Groups[1].Value + dec + m.Groups[3].Value;
    }

    /// <summary>Lit une expression SPICE de source. Les combinaisons non prévues restent en « Expression ».</summary>
    public static Stimulus Lire(string spice)
    {
        var t = spice.Trim();
        double[]? Args(string fn)
        {
            var m = Regex.Match(t, $@"^{fn}\s*\(([^)]*)\)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var parts = m.Groups[1].Value.Split((char[])[' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
            var r = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++) if (!SpiceNumber.TryParse(parts[i], out r[i])) return null;
            return r;
        }
        if (Args("PULSE") is { Length: >= 2 and <= 7 } p)
        {
            var s = Nouveau(FormeStimulus.Impulsion); string[] k = ["v1", "v2", "td", "tr", "tf", "pw", "per"];
            for (int i = 0; i < p.Length; i++) s[k[i]] = p[i];
            return s;
        }
        if (Args("SIN") is { Length: >= 3 and <= 4 } q)
        {
            var s = Nouveau(FormeStimulus.Sinus); string[] k = ["vo", "va", "f", "td"];
            for (int i = 0; i < q.Length; i++) s[k[i]] = q[i];
            return s;
        }
        // « DC x », « x », « AC m [ph] », « DC x AC m [ph] » (dans un ordre quelconque).
        var mots = t.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        double? dc = null, ac = null, ph = null; bool ok = mots.Length > 0;
        for (int i = 0; i < mots.Length && ok; i++)
        {
            if (mots[i].Equals("DC", StringComparison.OrdinalIgnoreCase) && i + 1 < mots.Length && SpiceNumber.TryParse(mots[i + 1], out var v)) { dc = v; i++; }
            else if (mots[i].Equals("AC", StringComparison.OrdinalIgnoreCase) && i + 1 < mots.Length && SpiceNumber.TryParse(mots[i + 1], out var a))
            {
                ac = a; i++;
                if (i + 1 < mots.Length && SpiceNumber.TryParse(mots[i + 1], out var p2)) { ph = p2; i++; }
            }
            else if (i == 0 && SpiceNumber.TryParse(mots[i], out var v0)) dc = v0;
            else ok = false;
        }
        if (ok && ac is { } acv) { var s = Nouveau(FormeStimulus.PetitSignal); s["ac"] = acv; s["ph"] = ph ?? 0; s["dc"] = dc ?? 0; return s; }
        if (ok && dc is { } dcv) { var s = Nouveau(FormeStimulus.Continu); s["dc"] = dcv; return s; }
        return new Stimulus { Forme = FormeStimulus.Expression, Expression = t };
    }
}
