using System.Numerics;
using Silx.Core.Raw;
using Silx.Core.Simulation;
using Silx.Mesures;
using Silx.Mesures.Courbes;
using Silx.Mesures.Gabarits;
using Silx.Mesures.Mesures;
namespace Silx.Etude;

public sealed record ResultatCritere(CritereGabarit Critere, ResultatGabarit Resultat);

public sealed record ResultatTrace(GabaritTrace Gabarit, ResultatGabaritGraphique Resultat);

public sealed record ResultatEtude(
    IReadOnlyDictionary<AnalyseKind, RawFile> Resultats,
    IReadOnlyList<ResultatCritere> Criteres,
    IReadOnlyList<ResultatTrace> Traces,
    IReadOnlyList<ErcIssue> Erc,
    string Journal,
    string? Erreur)
{
    public int Nombre => Criteres.Count + Traces.Count;
    public int Respectes => Criteres.Count(c => c.Resultat.Verdict == Verdict.Pass) + Traces.Count(t => t.Resultat.Verdict == Verdict.Pass);
    public Verdict Global
    {
        get
        {
            var v = Criteres.Select(c => c.Resultat.Verdict).Concat(Traces.Select(t => t.Resultat.Verdict)).ToList();
            if (v.Contains(Verdict.Fail)) return Verdict.Fail;
            return v.Count == 0 || v.Contains(Verdict.NonEvaluable) ? Verdict.NonEvaluable : Verdict.Pass;
        }
    }
}

/// <summary>Orchestration : ERC → circuits → ngspice (analyses en parallèle) → mesures → verdicts.</summary>
public static class Verification
{
    /// <summary>Chemin de ngspice : variable SILX_NGSPICE, sinon emplacements Homebrew, sinon PATH.</summary>
    public static string NgspiceExecutable()
    {
        var env = Environment.GetEnvironmentVariable("SILX_NGSPICE");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        foreach (var p in new[] { "/opt/homebrew/bin/ngspice", "/usr/local/bin/ngspice", "/usr/bin/ngspice" })
            if (File.Exists(p)) return p;
        return "ngspice";
    }

    public static async Task<ResultatEtude> RunAsync(Etude circuit, Banc etude, IEnumerable<AnalyseKind> toujours, CancellationToken ct = default)
    {
        var schema = etude.Fusion(circuit.Schema);
        var conn = Connectivity.Build(schema);
        var erc = Erc.Check(schema, conn);
        if (erc.Any(i => i.Severity == Severity.Error))
            return new(new Dictionary<AnalyseKind, RawFile>(), [], [], erc, "", "Simulation bloquée : corriger les erreurs de vérification du schéma.");

        // Un banc = un type d'analyse (temporel ou fréquentiel).
        var kinds = new List<AnalyseKind> { etude.Type };
        // Modèles importés (souvent écrits pour PSpice) : compatibilité PSpice de ngspice (« psa » : tout le fichier, modèles inclus en ligne) ; circuits de base inchangés.
        var runner = new NgspiceRunner { Executable = NgspiceExecutable(), Compatibilite = schema.Components.Any(k => k.Kind == ComponentKind.Spice) ? "psa" : null };
        var raws = new Dictionary<AnalyseKind, RawFile>();
        var journal = new System.Text.StringBuilder();
        try
        {
            var tasks = kinds.Select(async k =>
            {
                var c = CircuitBuilder.Build(schema, conn, CircuitBuilder.AnalysisFor(etude, k), $"Silx {etude.Nom} {k}");
                return (k, await runner.RunAsync(c, ct));
            }).ToList();
            foreach (var (k, r) in await Task.WhenAll(tasks))
            {
                raws[k] = r.Raw;
                journal.AppendLine($"── {k} : {r.Raw.PointCount} points, {r.Elapsed.TotalMilliseconds:0} ms").AppendLine(r.Log.Trim());
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (NgspiceException ex) { return new(raws, [], [], erc, ex.Log, ex.Message); }
        catch (Exception ex) { return new(raws, [], [], erc, journal.ToString(), ex.Message); }

        var criteres = etude.CriteresActifs.Select(g => new ResultatCritere(g, Evaluer(g, raws[g.Info.Analyse]))).ToList();
        var traces = etude.TracesActives.Select(g => new ResultatTrace(g, EvaluerTrace(g, raws[g.Analyse]))).ToList();
        return new(raws, criteres, traces, erc, journal.ToString(), null);
    }

    public static ResultatGabaritGraphique EvaluerTrace(GabaritTrace g, RawFile raw)
    {
        string vec = $"v({g.Noeud})";
        if (!raw.Variables.Any(v => string.Equals(v.Name, vec, StringComparison.OrdinalIgnoreCase)))
            return new(g.Nom, Verdict.NonEvaluable, null, null, null, $"Nœud « {g.Noeud} » absent des résultats.");
        var (x, y) = Courbe(g.Analyse, raw, g.Noeud);
        try { return new GabaritGraphique(g.Nom, g.Limite, g.Points, g.Analyse == AnalyseKind.Ac).Evaluer(x, y); }
        catch (ArgumentException ex) { return new(g.Nom, Verdict.NonEvaluable, null, null, null, ex.Message); }
    }

    /// <summary>Courbe affichée et contrôlée : v(t) en transitoire, |v| en dB en AC.</summary>
    public static (double[] X, double[] Y) Courbe(AnalyseKind k, RawFile raw, string noeud)
    {
        if (k == AnalyseKind.Transitoire) { var c = Temporelle(raw, $"v({noeud})"); return (c.Temps.ToArray(), c.Valeurs.ToArray()); }
        var r = Frequentielle(raw, $"v({noeud})");
        return (r.Frequences.ToArray(), r.Valeurs.ToArray().Select(Frequentiel.Db).ToArray());
    }

    public static ResultatGabarit Evaluer(CritereGabarit g, RawFile raw)
    {
        var info = g.Info;
        var bornes = new Bornes(g.Min, g.Max);
        if (bornes.Min is null && bornes.Max is null)
            return new(info.Nom, Verdict.NonEvaluable, null, info.Unite, bornes, "Aucune borne saisie.");
        string vec = $"v({g.Noeud})";
        if (!raw.Variables.Any(v => string.Equals(v.Name, vec, StringComparison.OrdinalIgnoreCase)))
            return new(info.Nom, Verdict.NonEvaluable, null, info.Unite, bornes, $"Nœud « {g.Noeud} » absent des résultats.");
        IMesure<CourbeTemporelle>? mt = g.Mesure switch
        {
            TypeMesure.Depassement => Catalogue.Depassement,
            TypeMesure.TempsDeMontee => Catalogue.TempsDeMontee,
            TypeMesure.TempsEtablissement => Catalogue.TempsEtablissement2Pct,
            TypeMesure.ValeurFinale => new Mesure<CourbeTemporelle>("Valeur finale", "V", c => Transitoire.ValeurFinale(c)),
            _ => null
        };
        IMesure<ReponseFrequentielle>? mf = g.Mesure switch
        {
            TypeMesure.BandePassante3dB => Catalogue.BandePassante3dB,
            TypeMesure.GainDc => new Mesure<ReponseFrequentielle>("Gain BF", "dB", r => Frequentiel.Db(r.Valeurs[0])),
            TypeMesure.MargeDePhase => Catalogue.MargeDePhase,
            TypeMesure.MargeDeGain => Catalogue.MargeDeGain,
            _ => null
        };
        try
        {
            if (mt != null) return new Gabarit<CourbeTemporelle>(info.Nom, mt, bornes).Evaluer(Temporelle(raw, vec));
            return new Gabarit<ReponseFrequentielle>(info.Nom, mf!, bornes).Evaluer(Frequentielle(raw, vec));
        }
        catch (Exception ex) when (ex is ArgumentException or MesureImpossibleException)
        {
            return new(info.Nom, Verdict.NonEvaluable, null, info.Unite, bornes, ex.Message);
        }
    }

    /// <summary>Courbe v(t). Les points de temps non strictement croissants (points de rupture ngspice) sont écartés.</summary>
    public static CourbeTemporelle Temporelle(RawFile raw, string vec)
    {
        var t = raw.Vector("time"); var v = raw.Vector(vec);
        var tt = new List<double>(t.Length); var vv = new List<double>(t.Length);
        for (int i = 0; i < t.Length; i++)
        {
            var ti = Math.Abs(t[i]); // ngspice peut écrire -0 au premier point
            if (tt.Count > 0 && ti <= tt[^1]) continue;
            tt.Add(ti); vv.Add(v[i]);
        }
        return new CourbeTemporelle(tt, vv);
    }

    public static ReponseFrequentielle Frequentielle(RawFile raw, string vec)
    {
        var f = raw.Vector("frequency"); var re = raw.Vector(vec); var im = raw.Imaginary(vec);
        return new ReponseFrequentielle(f, re.Select((r, i) => new Complex(r, im[i])));
    }
}
