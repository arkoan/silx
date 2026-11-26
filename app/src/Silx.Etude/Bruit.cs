using Silx.Core.Circuits;
namespace Silx.Etude;

/// <summary>
/// Bruit d'un nœud sur la plage du banc fréquentiel : densités spectrales en sortie et rapportée à l'entrée (V/√Hz,
/// ou A/√Hz pour une source de courant), et bruits totaux intégrés sur la plage (V efficaces).
/// </summary>
public sealed record CourbeBruit(string Noeud, double[] F, double[] Sortie, double[] Entree, double TotalSortie, double TotalEntree)
{
    /// <summary>Densité de sortie à une fréquence (interpolation log-log), null hors plage.</summary>
    public double? SortieA(double f) => Interp(F, Sortie, f);
    static double? Interp(double[] x, double[] y, double v)
    {
        if (x.Length == 0 || v < x[0] || v > x[^1]) return null;
        int i = Array.FindIndex(x, a => a >= v); if (i <= 0) return y[0];
        double t = (Math.Log(v) - Math.Log(x[i - 1])) / (Math.Log(x[i]) - Math.Log(x[i - 1]));
        return Math.Exp(Math.Log(Math.Max(y[i - 1], 1e-300)) + t * (Math.Log(Math.Max(y[i], 1e-300)) - Math.Log(Math.Max(y[i - 1], 1e-300))));
    }
}

public static class Bruit
{
    /// <summary>Points par décade de l'analyse de bruit.</summary>
    public const int PointsParDecade = 20;

    /// <summary>Source d'entrée du banc pour le bruit rapporté à l'entrée : la source petit signal (premier stimulus AC).</summary>
    public static PlacedComponent? Entree(Banc b) => b.Calque.Components.FirstOrDefault(k => k.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource
        && Stimulus.Lire(k.Value).Analyse == AnalyseKind.Ac);

    /// <summary>
    /// Calcule le bruit de chaque nœud (tension ; les courants sont ignorés) : une analyse .noise par nœud, en parallèle,
    /// sur la plage fréquentielle du banc. Erreur si le banc n'est pas fréquentiel ou n'a pas de source petit signal.
    /// </summary>
    public static async Task<(IReadOnlyDictionary<string, CourbeBruit> Courbes, string? Erreur)> CalculerAsync(Etude circuit, Banc banc, IEnumerable<string> noeuds, CancellationToken ct = default)
    {
        var vide = new Dictionary<string, CourbeBruit>(StringComparer.OrdinalIgnoreCase);
        if (banc.Type != AnalyseKind.Ac || Entree(banc) is not { } src) return (vide, "Le bruit se calcule sur un banc fréquentiel ayant une source petit signal (AC) comme entrée.");
        var schema = banc.Fusion(circuit.Schema);
        var conn = Connectivity.Build(schema);
        if (Erc.Check(schema, conn).Any(i => i.Severity == Severity.Error)) return (vide, "Corrigez d'abord les erreurs du schéma.");
        var liste = noeuds.Where(n => !Signaux.EstCourant(n) && n != "0" && conn.AllNodes.Contains(n, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var exe = Verification.NgspiceExecutable();
        var runner = new Silx.Core.Simulation.NgspiceRunner { Executable = exe, Environnement = NgspiceEmbarque.Environnement(exe), Compatibilite = schema.Components.Any(k => k.Kind == ComponentKind.Spice) ? "psa" : null };
        double f1 = Math.Min(banc.Ac.FDebut, banc.Ac.FFin), f2 = Math.Max(banc.Ac.FDebut, banc.Ac.FFin);
        try
        {
            var res = await Task.WhenAll(liste.Select(async n =>
            {
                var c = CircuitBuilder.Build(schema, conn, new NoiseAnalysis(n, src.Ref, AcSweep.Dec, PointsParDecade, f1, f2), $"Silx bruit {n}");
                var r = await runner.RunAsync(c, ct);
                var dens = r.Plots.First(p => p.Variables.Any(v => v.Name.Equals("onoise_spectrum", StringComparison.OrdinalIgnoreCase)));
                var tot = r.Plots.FirstOrDefault(p => p.Variables.Any(v => v.Name.Contains("onoise_total", StringComparison.OrdinalIgnoreCase)));
                double Total(string nom) => tot?.Variables.FirstOrDefault(v => v.Name.Contains(nom, StringComparison.OrdinalIgnoreCase)) is { } v ? tot.Vector(v.Name)[0] : double.NaN;
                return new CourbeBruit(n, dens.Vector("frequency"), dens.Vector("onoise_spectrum"), dens.Vector("inoise_spectrum"), Total("onoise_total"), Total("inoise_total"));
            }));
            foreach (var c in res) vide[c.Noeud] = c;
            return (vide, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (vide, "Analyse de bruit : " + ex.Message); }
    }
}
