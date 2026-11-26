using System.Numerics;
using Silx.Core.Circuits;
using Silx.Core.Raw;
using Silx.Core.Simulation;
namespace Silx.Etude;

/// <summary>
/// Échantillonnage fréquentiel adaptatif : un balayage grossier (20 points par décade), puis, à chaque passe, des
/// sous-balayages fins sur les intervalles où le gain ou la phase d'une tension varient vite (coupure, pente) ou dévient
/// de leur tendance (résonance très aiguë qui ne laisse qu'une petite marche entre deux points, ex. un quartz). Les
/// points de toutes les passes sont fusionnés en une seule courbe.
/// </summary>
public static class Echantillonnage
{
    public const int PointsParDecadeGrossier = 20;
    const double SeuilGainDb = 3, SeuilPhaseDeg = 20, SeuilEcartDb = 0.003;
    const int PointsParSousBalayage = 20, PassesMax = 12, ZonesParPasse = 48, PointsMax = 40_000;

    public static async Task<SimulationResult> AcAsync(NgspiceRunner runner, Func<Analysis, Circuit> circuit, double f1, double f2, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var journal = new System.Text.StringBuilder();
        f1 = Math.Max(Math.Min(f1, f2), 1e-6); f2 = Math.Max(f1, f2);
        double decades = Math.Log10(f2 / f1);
        int ppd = decades * PointsParDecadeGrossier >= 100 ? PointsParDecadeGrossier : (int)Math.Ceiling(100 / Math.Max(decades, 1e-9));
        var premier = await runner.RunAsync(circuit(new AcAnalysis(AcSweep.Dec, ppd, f1, f2)), ct);
        var raw = premier.Raw;
        journal.AppendLine($"Échantillonnage automatique : balayage grossier {raw.PointCount} points.").AppendLine(premier.Log.Trim());
        for (int passe = 1; passe <= PassesMax && raw.PointCount < PointsMax; passe++)
        {
            var zones = Zones(raw);
            if (zones.Count == 0) break;
            // Tous les sous-balayages d'une passe dans une seule netlist (une analyse .ac par zone) : un seul ngspice.
            var choisies = zones.Take(ZonesParPasse).ToList();
            var c = circuit(new AcAnalysis(AcSweep.Lin, PointsParSousBalayage, choisies[0].A, choisies[0].B));
            string G(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var z in choisies.Skip(1)) c.Directive($".ac lin {PointsParSousBalayage} {G(z.A)} {G(z.B)}");
            var sous = await runner.RunAsync(c, ct);
            raw = Fusionner([raw, .. sous.Plots]);
            journal.AppendLine($"Passe {passe} : {Math.Min(zones.Count, ZonesParPasse)} zone(s) affinée(s), {raw.PointCount} points.");
        }
        return new SimulationResult(premier.RunId, raw, journal.ToString(), sw.Elapsed, 0);
    }

    /// <summary>Intervalles à affiner (bornes exclues), les plus marqués d'abord.</summary>
    public static List<(double A, double B)> Zones(RawFile raw)
    {
        var f = raw.Vector("frequency"); int n = f.Length;
        if (n < 3) return [];
        var note = new double[n - 1];   // score de chaque intervalle [i, i+1]
        var x = f.Select(Math.Log10).ToArray();
        foreach (var v in raw.Variables.Where(v => v.Name.StartsWith("v(", StringComparison.OrdinalIgnoreCase)))
        {
            var re = raw.Real[v.Index]; var im = raw.Imag![v.Index];
            var db = new double[n]; var ph = new double[n];
            for (int i = 0; i < n; i++) { var z = new Complex(re[i], im[i]); db[i] = 20 * Math.Log10(Math.Max(z.Magnitude, 1e-30)); ph[i] = z.Phase * 180 / Math.PI; }
            for (int i = 0; i < n - 1; i++)
            {
                double dph = Math.Abs(ph[i + 1] - ph[i]); dph = Math.Min(dph, 360 - dph);
                note[i] = Math.Max(note[i], Math.Max(Math.Abs(db[i + 1] - db[i]) / SeuilGainDb, dph / SeuilPhaseDeg));
            }
            // Écart à la tendance : changement de pente du gain entre deux intervalles voisins, ramené en dB.
            for (int i = 1; i < n - 1; i++)
            {
                double h0 = x[i] - x[i - 1], h1 = x[i + 1] - x[i];
                // Seulement tant que les points sont espacés (> 1e-4 décade) : il s'agit de débusquer une résonance cachée entre
                // deux points ; une fois la zone résolue, les seuils de gain et de phase suffisent.
                if (h0 <= 0 || h1 <= 0 || Math.Min(h0, h1) < 1e-4) continue;
                double ecart = Math.Abs((db[i + 1] - db[i]) / h1 - (db[i] - db[i - 1]) / h0) * Math.Min(h0, h1) / SeuilEcartDb;
                note[i - 1] = Math.Max(note[i - 1], ecart); note[i] = Math.Max(note[i], ecart);
            }
        }
        // Chaque intervalle marqué est affiné séparément (la résolution y est multipliée par 21 à chaque passe).
        var zones = new List<(double A, double B, double Note)>();
        for (int i = 0; i < n - 1; i++)
            if (note[i] > 1 && f[i + 1] - f[i] > f[i] * 1e-7) zones.Add((f[i], f[i + 1], note[i]));   // sinon : assez fin, ou résolution numérique atteinte
        return zones.OrderByDescending(z => z.Note).Select(z => (z.A + (z.B - z.A) / (PointsParSousBalayage + 1), z.B - (z.B - z.A) / (PointsParSousBalayage + 1))).ToList();
    }

    /// <summary>Fusionne des résultats AC du même circuit : points triés par fréquence, doublons retirés.</summary>
    public static RawFile Fusionner(IReadOnlyList<RawFile> raws)
    {
        var r0 = raws[0]; int iv = r0.IndexOf("frequency");
        var pts = raws.SelectMany(r => Enumerable.Range(0, r.PointCount).Select(p => (R: r, P: p, F: r.Real[iv][p])))
            .OrderBy(t => t.F).ToList();
        var garde = new List<(RawFile R, int P, double F)>();
        foreach (var t in pts) if (garde.Count == 0 || t.F > garde[^1].F * (1 + 1e-12)) garde.Add(t);
        int nv = r0.Variables.Count;
        var re = new double[nv][]; var im = new double[nv][];
        for (int v = 0; v < nv; v++)
        {
            var name = r0.Variables[v].Name;
            re[v] = garde.Select(t => t.R.Real[t.R.IndexOf(name)][t.P]).ToArray();
            im[v] = garde.Select(t => t.R.Imag![t.R.IndexOf(name)][t.P]).ToArray();
        }
        return new RawFile { Title = r0.Title, PlotName = r0.PlotName, IsComplex = true, Variables = r0.Variables, PointCount = garde.Count, Real = re, Imag = im };
    }
}
