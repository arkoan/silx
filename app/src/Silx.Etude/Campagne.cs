using Silx.Mesures.Gabarits;
namespace Silx.Etude;

public enum ModeCampagne { Aucune, Balayage, MonteCarlo, Coins }
public enum EchelleBalayage { Lineaire, Logarithmique, Liste }
public enum LoiTirage { Gauss, Uniforme }

/// <summary>
/// Campagne d'un banc : répond à « mon circuit reste-t-il conforme avec des composants réels ? ».
/// Balayage d'un paramètre, tirages Monte-Carlo dans les tolérances, ou coins (combinaisons extrêmes des tolérances).
/// </summary>
public sealed class Campagne
{
    public ModeCampagne Mode { get; set; }
    // Balayage
    /// <summary>Référence du composant balayé (R1, C2, VCC…) ou « TEMP » pour la température.</summary>
    public string Cible { get; set; } = "";
    public double Debut { get; set; } = 1e3;
    public double Fin { get; set; } = 10e3;
    public int Points { get; set; } = 5;
    public EchelleBalayage Echelle { get; set; } = EchelleBalayage.Lineaire;
    /// <summary>Valeurs du balayage en liste (« 1k 2,2k 4,7k »).</summary>
    public string Liste { get; set; } = "";
    // Monte-Carlo
    public int Tirages { get; set; } = 100;
    public LoiTirage Loi { get; set; } = LoiTirage.Gauss;
    public int Graine { get; set; } = 1;
    // Coins
    /// <summary>Coins : ajouter la température (mini et maxi) aux combinaisons.</summary>
    public bool CoinsTemperature { get; set; }
    public double TMin { get; set; } = -40;
    public double TMax { get; set; } = 85;
}

/// <summary>Un tirage : valeurs imposées aux composants (référence → valeur), température éventuelle, paramètre balayé.</summary>
public sealed record Tirage(int Index, IReadOnlyDictionary<string, double> Valeurs, double? Temperature, double? Parametre, string Libelle);

public sealed record ResultatTirage(
    Tirage Tirage,
    Verdict Global,
    IReadOnlyDictionary<CritereGabarit, ResultatGabarit> Criteres,
    IReadOnlyDictionary<GabaritTrace, ResultatGabaritGraphique> Traces,
    IReadOnlyDictionary<string, (double[] X, double[] Y)> Courbes,
    string? Erreur)
{
    public bool Conforme => Erreur == null && Global == Verdict.Pass;
    public int Echecs => Criteres.Values.Count(r => r.Verdict == Verdict.Fail) + Traces.Values.Count(r => r.Verdict == Verdict.Fail);
    /// <summary>Plus petite marge relative des contraintes (négative = franchie) : sert à désigner le pire cas.</summary>
    public double MargeRelative
    {
        get
        {
            var m = new List<double>();
            foreach (var (c, r) in Criteres)
            {
                if (r.Valeur is not { } v) continue;
                double echelle = c.Min is { } a && c.Max is { } b ? Math.Abs(b - a) : Math.Abs(c.Max ?? c.Min ?? 1);
                double marge = Math.Min(c.Min is { } lo ? v - lo : double.PositiveInfinity, c.Max is { } hi ? hi - v : double.PositiveInfinity);
                if (double.IsFinite(marge)) m.Add(marge / Math.Max(echelle, 1e-15));
            }
            foreach (var (g, r) in Traces)
                if (r.Marge is { } mg) { var ys = g.Points.Select(p => p.Y).ToList(); m.Add(mg / Math.Max(ys.Count > 0 ? Math.Max(ys.Max() - ys.Min(), Math.Abs(ys.Max())) : 1, 1e-15)); }
            return m.Count == 0 ? 0 : m.Min();
        }
    }
}

/// <summary>Statistiques d'une mesure sur la campagne : moyenne, écart-type, extrêmes, Cpk (si le critère a des bornes).</summary>
public sealed record StatMesure(CritereGabarit Critere, int N, double Moyenne, double EcartType, double Min, double Max, double? Cpk, int Echecs);

public sealed record ResultatCampagne(ModeCampagne Mode, string Cible, IReadOnlyList<ResultatTirage> Tirages, IReadOnlyList<StatMesure> Stats, string? Avertissement)
{
    public int Conformes => Tirages.Count(t => t.Conforme);
    public double Taux => Tirages.Count == 0 ? 0 : (double)Conformes / Tirages.Count;
    /// <summary>Tirage le plus défavorable : le plus de contraintes franchies, puis la plus petite marge relative.</summary>
    public ResultatTirage? PireCas => Tirages.Where(t => t.Erreur == null).OrderByDescending(t => t.Echecs).ThenBy(t => t.MargeRelative).FirstOrDefault();
}

public static class Campagnes
{
    /// <summary>Tolérance par défaut d'un type de composant en % (réglée dans les Paramètres de l'application).</summary>
    public static Func<ComponentKind, double?> ToleranceParDefaut { get; set; } = k => k switch
    {
        ComponentKind.Resistor => 1, ComponentKind.Capacitor => 10, ComponentKind.Inductor => 10, _ => null
    };

    /// <summary>Points gardés par courbe et par tirage (seuls les signaux sondés sont gardés).</summary>
    public const int PointsParCourbe = 1500;

    /// <summary>Valeur numérique d'un composant : R, C, L, ou source continue (DC). Null si la valeur n'est pas un nombre.</summary>
    public static double? ValeurNominale(PlacedComponent k) => k.Kind switch
    {
        ComponentKind.Resistor or ComponentKind.Capacitor or ComponentKind.Inductor => SpiceNumber.ParseOrNull(k.Value),
        ComponentKind.VoltageSource or ComponentKind.CurrentSource when Stimulus.Lire(k.Value) is { Forme: FormeStimulus.Continu } s => s["dc"],
        _ => null
    };

    public static bool Variable(PlacedComponent k) => ValeurNominale(k) != null;

    /// <summary>Tolérance effective : celle du composant, sinon celle de son type ; null ou 0 = pas de dispersion.</summary>
    public static double? Tolerance(PlacedComponent k) => !Variable(k) ? null : (k.Tolerance ?? ToleranceParDefaut(k.Kind)) is > 0 and var t ? t : null;

    /// <summary>Écrit une valeur dans un composant (nombre, ou « DC v » pour une source).</summary>
    public static void Imposer(PlacedComponent k, double v)
    {
        var g = System.Globalization.CultureInfo.InvariantCulture;
        if (k.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource) { var s = Stimulus.Lire(k.Value); s["dc"] = v; k.Value = s.VersSpice(); }
        else k.Value = v.ToString("G6", g);
    }

    /// <summary>Valeurs du paramètre balayé.</summary>
    public static IReadOnlyList<double> ValeursBalayage(Campagne c)
    {
        if (c.Echelle == EchelleBalayage.Liste)
            return c.Liste.Split([' ', ';', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(SpiceNumber.ParseOrNull).OfType<double>().ToList();
        int n = Math.Clamp(c.Points, 1, 1000);
        if (n == 1) return [c.Debut];
        if (c.Echelle == EchelleBalayage.Logarithmique && c.Debut > 0 && c.Fin > 0)
            return Enumerable.Range(0, n).Select(i => c.Debut * Math.Pow(c.Fin / c.Debut, (double)i / (n - 1))).ToList();
        return Enumerable.Range(0, n).Select(i => c.Debut + (c.Fin - c.Debut) * i / (n - 1)).ToList();
    }

    /// <summary>Plan des tirages d'une campagne sur le schéma fusionné (circuit + banc).</summary>
    public static IReadOnlyList<Tirage> Plan(Campagne c, Schematic fusion, out string? avertissement)
    {
        avertissement = null;
        var tolerants = fusion.Components.Where(k => Tolerance(k) != null).OrderBy(k => k.Ref, StringComparer.Ordinal).ToList();
        string Fmt(double v) => SpiceNumber.Format(v);
        switch (c.Mode)
        {
            case ModeCampagne.Balayage:
            {
                bool temp = c.Cible.Trim().Equals("TEMP", StringComparison.OrdinalIgnoreCase);
                var cible = temp ? null : fusion.Components.FirstOrDefault(k => string.Equals(k.Ref, c.Cible.Trim(), StringComparison.OrdinalIgnoreCase));
                if (!temp && (cible == null || !Variable(cible))) { avertissement = $"Paramètre balayé « {c.Cible} » introuvable ou sans valeur numérique (R, C, L, source continue, ou TEMP)."; return []; }
                return ValeursBalayage(c).Select((v, i) => new Tirage(i,
                    temp ? new Dictionary<string, double>() : new Dictionary<string, double> { [cible!.Ref] = v },
                    temp ? v : null, v, $"{(temp ? "T" : cible!.Ref)} = {(temp ? $"{v:0.#} °C" : Fmt(v))}")).ToList();
            }
            case ModeCampagne.MonteCarlo:
            {
                if (tolerants.Count == 0) { avertissement = "Aucun composant n'a de tolérance : rien à disperser."; return []; }
                var rnd = new Random(c.Graine);
                double Ecart(double tol) => c.Loi == LoiTirage.Uniforme ? (rnd.NextDouble() * 2 - 1) * tol
                    : Math.Clamp(Gauss(rnd) * tol / 3, -tol, tol);   // gaussienne : ± tolérance = 3 σ, tronquée à ± tolérance
                return Enumerable.Range(0, Math.Clamp(c.Tirages, 1, 100_000)).Select(i =>
                {
                    var v = tolerants.ToDictionary(k => k.Ref, k => ValeurNominale(k)!.Value * (1 + Ecart(Tolerance(k)!.Value) / 100));
                    return new Tirage(i, v, null, null, $"Tirage {i + 1}");
                }).ToList();
            }
            case ModeCampagne.Coins:
            {
                var axes = tolerants.Select(k => (Nom: k.Ref, Bas: ValeurNominale(k)!.Value * (1 - Tolerance(k)!.Value / 100), Haut: ValeurNominale(k)!.Value * (1 + Tolerance(k)!.Value / 100))).ToList();
                if (axes.Count == 0 && !c.CoinsTemperature) { avertissement = "Aucun composant n'a de tolérance : pas de coin à simuler."; return []; }
                int n = axes.Count + (c.CoinsTemperature ? 1 : 0);
                var combos = new List<bool[]>();
                if (n <= 10) for (int m = 0; m < 1 << n; m++) combos.Add(Enumerable.Range(0, n).Select(b => (m >> b & 1) == 1).ToArray());
                else
                {
                    // Trop de combinaisons (2^n) : tout au mini, tout au maxi, puis chaque paramètre seul à ses extrêmes.
                    avertissement = $"{n} paramètres : {1 << Math.Min(n, 30)} combinaisons, trop pour toutes les simuler ; coins réduits (tout mini, tout maxi, puis un paramètre à la fois).";
                    combos.Add(new bool[n]); combos.Add(Enumerable.Repeat(true, n).ToArray());
                    for (int b = 0; b < n; b++) foreach (var haut in new[] { false, true }) { var x = Enumerable.Repeat(!haut, n).ToArray(); x[b] = haut; combos.Add(x); }
                }
                return combos.Select((x, i) =>
                {
                    var v = axes.Select((a, j) => (a.Nom, V: x[j] ? a.Haut : a.Bas)).ToDictionary(p => p.Nom, p => p.V);
                    double? t = c.CoinsTemperature ? (x[^1] ? c.TMax : c.TMin) : null;
                    var lib = string.Join(" ", axes.Select((a, j) => a.Nom + (x[j] ? "+" : "−"))) + (t is { } tt ? $" T{tt:0}°C" : "");
                    return new Tirage(i, v, t, null, lib);
                }).ToList();
            }
            default: return [];
        }
    }

    static double Gauss(Random r) { double u1 = 1 - r.NextDouble(), u2 = r.NextDouble(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }

    /// <summary>
    /// Lance la campagne : une simulation par tirage, en parallèle sur les cœurs ; garde les mesures, les verdicts et
    /// les seules courbes des signaux sondés (sous-échantillonnées). <paramref name="progression"/> reçoit le nombre de tirages faits.
    /// </summary>
    public static async Task<ResultatCampagne> RunAsync(Etude circuit, Banc banc, IReadOnlyCollection<string> signaux, IProgress<int>? progression = null, CancellationToken ct = default)
    {
        banc.RecalerGabarits();
        var c = banc.Campagne;
        var plan = Plan(c, banc.Fusion(circuit.Schema), out var avert);
        var resultats = new ResultatTirage[plan.Count];
        int faits = 0;
        using var porte = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount));
        var taches = plan.Select(async t =>
        {
            await porte.WaitAsync(ct);
            try
            {
                var r = await Verification.RunAsync(circuit, banc, [], ct,
                    s => { foreach (var k in s.Components) if (t.Valeurs.TryGetValue(k.Ref, out var v)) Imposer(k, v); },
                    t.Temperature is { } temp ? [$".options temp={temp.ToString(System.Globalization.CultureInfo.InvariantCulture)}"] : null);
                var courbes = new Dictionary<string, (double[], double[])>();
                if (r.Erreur == null && r.Resultats.TryGetValue(banc.Type, out var raw))
                    foreach (var n in signaux.Distinct(StringComparer.OrdinalIgnoreCase))
                        if (raw.Variables.Any(v => string.Equals(v.Name, $"v({n})", StringComparison.OrdinalIgnoreCase)))
                            courbes[n] = Reduire(Verification.Courbe(banc.Type, raw, n));
                resultats[t.Index] = new ResultatTirage(t, r.Nombre == 0 ? Verdict.Pass : r.Global,
                    r.Criteres.ToDictionary(x => x.Critere, x => x.Resultat), r.Traces.ToDictionary(x => x.Gabarit, x => x.Resultat), courbes, r.Erreur);
            }
            finally { porte.Release(); progression?.Report(Interlocked.Increment(ref faits)); }
        }).ToList();
        await Task.WhenAll(taches);
        return new ResultatCampagne(c.Mode, c.Cible, resultats, Statistiques(banc, resultats), avert);
    }

    static (double[], double[]) Reduire((double[] X, double[] Y) c)
    {
        if (c.X.Length <= PointsParCourbe) return c;
        int pas = (int)Math.Ceiling(c.X.Length / (double)PointsParCourbe);
        var idx = Enumerable.Range(0, c.X.Length).Where(i => i % pas == 0 || i == c.X.Length - 1).ToArray();
        return (idx.Select(i => c.X[i]).ToArray(), idx.Select(i => c.Y[i]).ToArray());
    }

    public static IReadOnlyList<StatMesure> Statistiques(Banc banc, IReadOnlyList<ResultatTirage> tirages)
    {
        var l = new List<StatMesure>();
        foreach (var cr in banc.CriteresActifs)
        {
            var v = tirages.Where(t => t.Criteres.TryGetValue(cr, out var r) && r.Valeur != null).Select(t => t.Criteres[cr].Valeur!.Value).ToList();
            int echecs = tirages.Count(t => t.Criteres.TryGetValue(cr, out var r) && r.Verdict == Verdict.Fail);
            if (v.Count == 0) { l.Add(new(cr, 0, double.NaN, double.NaN, double.NaN, double.NaN, null, echecs)); continue; }
            double mu = v.Average(), sd = v.Count > 1 ? Math.Sqrt(v.Sum(x => (x - mu) * (x - mu)) / (v.Count - 1)) : 0;
            double? cpk = sd <= 0 ? null : (cr.Min, cr.Max) switch
            {
                (double a, double b) => Math.Min(b - mu, mu - a) / (3 * sd),
                (double a, null) => (mu - a) / (3 * sd),
                (null, double b) => (b - mu) / (3 * sd),
                _ => null
            };
            l.Add(new(cr, v.Count, mu, sd, v.Min(), v.Max(), cpk, echecs));
        }
        return l;
    }
}
