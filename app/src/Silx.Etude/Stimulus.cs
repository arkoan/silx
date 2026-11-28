using System.Globalization;
using System.Text.RegularExpressions;
namespace Silx.Etude;

/// <summary>Forme d'un stimulus (source de tension ou de courant) saisi en clair.</summary>
public enum FormeStimulus { Continu, Impulsion, Sinus, Rampe, Pwl, Prbs, Bruit, PetitSignal, BalayageDc, Expression }

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
    /// <summary>Forme PWL : points (temps, valeur), temps croissants.</summary>
    public List<(double T, double V)> Points { get; set; } = new();

    public sealed record Champ(string Cle, string Libelle, string Unite, double Defaut);

    /// <summary>Champs d'une forme. Unité « V » remplacée par « A » pour une source de courant.</summary>
    public static IReadOnlyList<Champ> Champs(FormeStimulus f) => f switch
    {
        FormeStimulus.Continu => [new("dc", "Valeur", "V", 5)],
        FormeStimulus.Impulsion => [new("v1", "Niveau bas", "V", 0), new("v2", "Niveau haut", "V", 1), new("td", "Retard", "s", 0.5e-3),
            new("tr", "Montée", "s", 1e-6), new("tf", "Descente", "s", 1e-6), new("pw", "Largeur", "s", 20e-3), new("per", "Période", "s", 40e-3)],
        FormeStimulus.Sinus => [new("vo", "Décalage", "V", 0), new("va", "Amplitude", "V", 1), new("f", "Fréquence", "Hz", 1e3), new("td", "Retard", "s", 0)],
        FormeStimulus.Rampe => [new("v1", "Valeur de départ", "V", 0), new("v2", "Valeur d'arrivée", "V", 1), new("td", "Retard", "s", 0.5e-3), new("tr", "Durée de la rampe", "s", 5e-3)],
        FormeStimulus.Prbs => [new("v1", "Niveau bas", "V", 0), new("v2", "Niveau haut", "V", 5), new("debit", "Débit", "bit/s", 100e3), new("td", "Retard", "s", 0), new("tr", "Transition", "s", 10e-9)],
        FormeStimulus.Bruit => [new("vo", "Décalage", "V", 0), new("na", "Bruit (efficace)", "V", 10e-3), new("nt", "Pas d'échantillonnage", "s", 1e-6)],
        FormeStimulus.PetitSignal => [new("ac", "Amplitude AC", "V", 1), new("ph", "Phase", "°", 0), new("dc", "Polarisation DC", "V", 0)],
        FormeStimulus.BalayageDc => [new("de", "De", "V", 0), new("a", "À", "V", 5), new("pas", "Pas", "V", 0.01)],
        _ => [],
    };

    public static string Libelle(FormeStimulus f) => f switch
    {
        FormeStimulus.Continu => "Continu", FormeStimulus.Impulsion => "Échelon / impulsion", FormeStimulus.Sinus => "Sinus",
        FormeStimulus.Rampe => "Rampe", FormeStimulus.Pwl => "Points (PWL, fichier CSV)", FormeStimulus.Prbs => "Séquence pseudo-aléatoire (PRBS7)",
        FormeStimulus.Bruit => "Bruit blanc", FormeStimulus.PetitSignal => "Petit signal (AC)", FormeStimulus.BalayageDc => "Balayage DC (caractéristique de transfert)", _ => "Expression SPICE"
    };

    /// <summary>Une source en petit signal fait un banc fréquentiel ; les autres formes, un banc temporel.</summary>
    public AnalyseKind Analyse => Forme == FormeStimulus.BalayageDc ? AnalyseKind.Dc : Forme == FormeStimulus.PetitSignal || (Forme == FormeStimulus.Expression && Regex.IsMatch(Expression, @"(^|\s)AC(\s|$)", RegexOptions.IgnoreCase)) ? AnalyseKind.Ac : AnalyseKind.Transitoire;

    public double this[string cle] { get => V.TryGetValue(cle, out var v) ? v : Champs(Forme).FirstOrDefault(c => c.Cle == cle)?.Defaut ?? 0; set => V[cle] = value; }

    public static Stimulus Nouveau(FormeStimulus f)
    {
        var s = new Stimulus { Forme = f };
        foreach (var c in Champs(f)) s.V[c.Cle] = c.Defaut;
        if (f == FormeStimulus.Pwl) s.Points = [(0, 0), (1e-3, 0), (2e-3, 1), (5e-3, 1), (6e-3, 0)];
        return s;
    }

    static string N(double d) => d.ToString("G6", CultureInfo.InvariantCulture);

    public string VersSpice() => Forme switch
    {
        FormeStimulus.Continu => $"DC {N(this["dc"])}",
        FormeStimulus.Impulsion => $"PULSE({N(this["v1"])} {N(this["v2"])} {N(this["td"])} {N(this["tr"])} {N(this["tf"])} {N(this["pw"])} {N(this["per"])})",
        FormeStimulus.Sinus => $"SIN({N(this["vo"])} {N(this["va"])} {N(this["f"])} {N(this["td"])})",
        FormeStimulus.Rampe => $"PWL(0 {N(this["v1"])} {N(this["td"])} {N(this["v1"])} {N(this["td"] + this["tr"])} {N(this["v2"])})",
        FormeStimulus.Pwl => "PWL(" + string.Join(" ", Points.Select(p => $"{N(p.T)} {N(p.V)}")) + ")",
        // PRBS : écrit en syntaxe Silx (paramètres relisibles) ; la netlist reçoit la séquence développée en PWL.
        FormeStimulus.Prbs => $"PRBS({N(this["v1"])} {N(this["v2"])} {N(this["debit"])} {N(this["td"])} {N(this["tr"])})",
        FormeStimulus.Bruit => $"DC {N(this["vo"])} TRNOISE({N(this["na"])} {N(this["nt"])} 0 0)",
        FormeStimulus.PetitSignal => $"DC {N(this["dc"])} AC {N(this["ac"])}" + (this["ph"] != 0 ? $" {N(this["ph"])}" : ""),
        // Balayage DC : syntaxe Silx (bornes et pas relisibles) ; la netlist reçoit « DC de » et la directive .dc.
        FormeStimulus.BalayageDc => $"DCSWEEP({N(this["de"])} {N(this["a"])} {N(this["pas"])})",
        _ => Expression.Trim(),
    };

    /// <summary>Valeur écrite dans la netlist ngspice (la PRBS y est développée en points PWL).</summary>
    public string VersNetlist() => Forme switch
    {
        FormeStimulus.Prbs => "PWL(" + string.Join(" ", PointsPrbs().Select(p => $"{N(p.T)} {N(p.V)}")) + ")",
        FormeStimulus.BalayageDc => $"DC {N(this["de"])}",
        // Rampe sans retard : pas de point (0, v1) répété, que ngspice 42 refuse (« non-increasing PWL time points »).
        FormeStimulus.Rampe when this["td"] <= 0 => $"PWL(0 {N(this["v1"])} {N(this["tr"])} {N(this["v2"])})",
        _ => VersSpice(),
    };

    /// <summary>Séquence PRBS7 (x⁷ + x⁶ + 1, 127 bits) : paliers v1 / v2 au débit donné, transitions de durée tr.</summary>
    public List<(double T, double V)> PointsPrbs()
    {
        double v1 = this["v1"], v2 = this["v2"], tb = 1 / Math.Max(this["debit"], 1e-30), td = this["td"], tr = Math.Min(this["tr"], tb / 2);
        var pts = new List<(double, double)> { (0, v1) };
        int etat = 0x7F; double prec = v1; double t = td;
        if (td > 0) pts.Add((td, v1));
        for (int i = 0; i < 127; i++)
        {
            int bit = ((etat >> 6) ^ (etat >> 5)) & 1; etat = ((etat << 1) | bit) & 0x7F;
            double v = bit == 1 ? v2 : v1;
            if (v != prec) { pts.Add((t + tr, v)); prec = v; }
            t += tb; pts.Add((t, v));
        }
        // Temps strictement croissants (exigé par PWL).
        var r = new List<(double, double)>();
        foreach (var p in pts) if (r.Count == 0 || p.Item1 > r[^1].Item1) r.Add(p);
        return r;
    }

    /// <summary>
    /// Forme d'onde échantillonnée sur [0, durée] (export CSV) ; null pour le bruit (aléatoire) et les expressions libres.
    /// </summary>
    public List<(double T, double V)>? Echantillons(double duree, int n = 2000)
    {
        switch (Forme)
        {
            case FormeStimulus.Pwl: return Points.ToList();
            case FormeStimulus.Prbs: return PointsPrbs();
            case FormeStimulus.Rampe: return [(0, this["v1"]), (this["td"], this["v1"]), (this["td"] + this["tr"], this["v2"]), (Math.Max(duree, this["td"] + this["tr"]), this["v2"])];
            case FormeStimulus.Continu: return [(0, this["dc"]), (duree, this["dc"])];
            case FormeStimulus.Impulsion or FormeStimulus.Sinus:
                return Enumerable.Range(0, n + 1).Select(i => { double t = duree * i / n; return (t, Valeur(t)); }).ToList();
            default: return null;
        }
    }

    double Valeur(double t)
    {
        if (Forme == FormeStimulus.Sinus)
            return t < this["td"] ? this["vo"] : this["vo"] + this["va"] * Math.Sin(2 * Math.PI * this["f"] * (t - this["td"]));
        double v1 = this["v1"], v2 = this["v2"], td = this["td"], tr = this["tr"], tf = this["tf"], pw = this["pw"], per = this["per"];
        if (t < td) return v1;
        double u = per > 0 ? (t - td) % per : t - td;
        if (u < tr) return v1 + (v2 - v1) * u / Math.Max(tr, 1e-30);
        if (u < tr + pw) return v2;
        if (u < tr + pw + tf) return v2 + (v1 - v2) * (u - tr - pw) / Math.Max(tf, 1e-30);
        return v1;
    }

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
            FormeStimulus.Rampe => $"{F("v1", "")} → {F("v2", u)} en {F("tr", "s")}",
            FormeStimulus.Pwl => $"PWL, {Points.Count} points",
            FormeStimulus.Prbs => $"PRBS7 {F("v1", "")}/{F("v2", u)}, {F("debit", "bit/s")}",
            FormeStimulus.Bruit => $"bruit {F("na", u)} eff." + (this["vo"] != 0 ? $" sur {F("vo", u)}" : ""),
            FormeStimulus.PetitSignal => $"AC {F("ac", u)}" + (this["dc"] != 0 ? $" sur {F("dc", u)}" : ""),
            FormeStimulus.BalayageDc => $"DC {F("de", "")} → {F("a", u)}",
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

    /// <summary>
    /// Lit des points PWL d'un fichier CSV : deux colonnes temps et valeur, séparées par « ; », tabulation, espace ou
    /// virgule ; virgule ou point décimal ; unités et notation ingénieur admises (1 ms ; 3,3 V). Lignes d'en-tête ignorées.
    /// </summary>
    public static List<(double T, double V)> LirePwlCsv(string texte)
    {
        var pts = new List<(double, double)>();
        foreach (var brut in texte.Replace("\r", "").Split('\n'))
        {
            var l = brut.Trim(); if (l.Length == 0 || l.StartsWith('#') || l.StartsWith('*')) continue;
            string[] c = l.Contains(';') ? l.Split(';') : l.Contains('\t') ? l.Split('\t')
                : Regex.IsMatch(l, @"\d\s+-?\d") ? Regex.Split(l, @"\s+") : l.Split(',');
            if (c.Length < 2 || !SpiceNumber.TryParse(c[0].Trim(), out var t) || !SpiceNumber.TryParse(c[1].Trim(), out var v)) continue;
            if (pts.Count > 0 && t <= pts[^1].Item1) continue;   // temps strictement croissants
            pts.Add((t, v));
        }
        return pts;
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
        if (Args("DCSWEEP") is { Length: 3 } dw)
        {
            var s = Nouveau(FormeStimulus.BalayageDc); s["de"] = dw[0]; s["a"] = dw[1]; s["pas"] = dw[2];
            return s;
        }
        if (Args("PRBS") is { Length: 5 } pr)
        {
            var s = Nouveau(FormeStimulus.Prbs); string[] k = ["v1", "v2", "debit", "td", "tr"];
            for (int i = 0; i < 5; i++) s[k[i]] = pr[i];
            return s;
        }
        if (Args("PWL") is { Length: >= 2 } w && w.Length % 2 == 0)
        {
            var pts = Enumerable.Range(0, w.Length / 2).Select(i => (w[2 * i], w[2 * i + 1])).ToList();
            // Rampe : (0, v1) (td, v1) (td + tr, v2).
            if (pts.Count == 3 && pts[0].Item1 == 0 && pts[0].Item2 == pts[1].Item2 && pts[2].Item1 > pts[1].Item1)
            {
                var s = Nouveau(FormeStimulus.Rampe);
                s["v1"] = pts[0].Item2; s["td"] = pts[1].Item1; s["tr"] = pts[2].Item1 - pts[1].Item1; s["v2"] = pts[2].Item2;
                return s;
            }
            return new Stimulus { Forme = FormeStimulus.Pwl, Points = pts };
        }
        if (Regex.Match(t, @"^(?:DC\s+(\S+)\s+)?TRNOISE\s*\(\s*(\S+)\s+(\S+)(?:\s+0\s+0)?\s*\)$", RegexOptions.IgnoreCase) is { Success: true } bm
            && SpiceNumber.TryParse(bm.Groups[2].Value, out var na) && SpiceNumber.TryParse(bm.Groups[3].Value, out var nt))
        {
            var s = Nouveau(FormeStimulus.Bruit); s["na"] = na; s["nt"] = nt;
            s["vo"] = bm.Groups[1].Success && SpiceNumber.TryParse(bm.Groups[1].Value, out var vo) ? vo : 0;
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
