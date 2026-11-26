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
    /// <summary>Chemin de ngspice : variable SILX_NGSPICE, sinon ngspice embarqué dans l'application, sinon Homebrew, sinon PATH.</summary>
    public static string NgspiceExecutable()
    {
        if (NgspiceForce.Value is { } force) return force;
        var env = Environment.GetEnvironmentVariable("SILX_NGSPICE");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        if (NgspiceEmbarque.Executable is { } embarque) return embarque;   // application installée : son propre ngspice
        foreach (var p in new[] { "/opt/homebrew/bin/ngspice", "/usr/local/bin/ngspice", "/usr/bin/ngspice" })
            if (File.Exists(p)) return p;
        return "ngspice";
    }

    /// <param name="variante">Modifie le schéma fusionné (copie) avant la simulation : valeurs d'une campagne.</param>
    /// <param name="directives">Directives ajoutées à la netlist (ex. « .options temp=85 »).</param>
    /// <summary>Durée maximale d'une simulation (SILX_DELAI_SIMULATION en secondes pour la changer).</summary>
    public static TimeSpan DelaiMax => DelaiForce.Value ?? (int.TryParse(Environment.GetEnvironmentVariable("SILX_DELAI_SIMULATION"), out var s) && s > 0 ? TimeSpan.FromSeconds(s) : TimeSpan.FromMinutes(5));
    /// <summary>Remplacements limités au flux asynchrone courant (tests) : exécutable ngspice et durée maximale.</summary>
    public static readonly AsyncLocal<string?> NgspiceForce = new();
    public static readonly AsyncLocal<TimeSpan?> DelaiForce = new();

    public static async Task<ResultatEtude> RunAsync(Etude circuit, Banc etude, IEnumerable<AnalyseKind> toujours, CancellationToken ct = default,
        Action<Schematic>? variante = null, IReadOnlyList<string>? directives = null)
    {
        if (variante == null) etude.RecalerGabarits();   // gabarits relatifs : suivent le front du stimulus (fait une fois avant une campagne)
        var schema = etude.Fusion(circuit.Schema);
        variante?.Invoke(schema);
        var conn = Connectivity.Build(schema);
        var erc = Erc.Check(schema, conn);
        if (erc.Any(i => i.Severity == Severity.Error))
            return new(new Dictionary<AnalyseKind, RawFile>(), [], [], erc, "", "Simulation bloquée : corriger les erreurs de vérification du schéma.");

        // Un banc = un type d'analyse (temporel ou fréquentiel).
        var kinds = new List<AnalyseKind> { etude.Type };
        // Modèles importés (souvent écrits pour PSpice) : compatibilité PSpice de ngspice (« psa » : tout le fichier, modèles inclus en ligne) ; circuits de base inchangés.
        var exe = NgspiceExecutable();
        var runner = new NgspiceRunner { Executable = exe, Environnement = NgspiceEmbarque.Environnement(exe), Compatibilite = schema.Components.Any(k => k.Kind == ComponentKind.Spice) ? "psa" : null };
        var raws = new Dictionary<AnalyseKind, RawFile>();
        var journal = new System.Text.StringBuilder();
        // Garde-fou : un circuit qui fait tourner ngspice sans fin est arrêté (processus tué) au bout de DelaiMax.
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(ct);
        delai.CancelAfter(DelaiMax);
        var ctUtilisateur = ct; ct = delai.Token;
        try
        {
            var tasks = kinds.Select(async k =>
            {
                Silx.Core.Circuits.Circuit Construire(Silx.Core.Circuits.Analysis a)
                {
                    var c = CircuitBuilder.Build(schema, conn, a, $"Silx {etude.Nom} {k}");
                    foreach (var d in directives ?? []) c.Directive(d);
                    return c;
                }
                // Fréquentiel automatique : balayage grossier puis affinage là où la courbe varie (résonances, coupures).
                if (k == AnalyseKind.Ac && etude.Ac.Auto) return (k, await Echantillonnage.AcAsync(runner, Construire, etude.Ac.FDebut, etude.Ac.FFin, ct));
                return (k, await runner.RunAsync(Construire(CircuitBuilder.AnalysisFor(etude, k)), ct));
            }).ToList();
            foreach (var (k, r) in await Task.WhenAll(tasks))
            {
                raws[k] = r.Raw;
                journal.AppendLine($"── {k} : {r.Raw.PointCount} points, {r.Elapsed.TotalMilliseconds:0} ms").AppendLine(r.Log.Trim());
            }
        }
        catch (OperationCanceledException) when (!ctUtilisateur.IsCancellationRequested && delai.IsCancellationRequested)
        { return new(raws, [], [], erc, journal.ToString(), $"Simulation arrêtée au bout de {(DelaiMax.TotalSeconds < 120 ? $"{DelaiMax.TotalSeconds:0} s" : $"{DelaiMax.TotalMinutes:0} min")} : réduisez la durée ou le nombre de points, ou cherchez une instabilité du circuit."); }
        catch (OperationCanceledException) { throw; }
        catch (NgspiceException ex) { return new(raws, [], [], erc, ex.Log, ex.Message); }
        catch (System.ComponentModel.Win32Exception ex)
        { return new(raws, [], [], erc, journal.ToString(), $"ngspice introuvable ({runner.Executable}) : installez-le (brew install ngspice) ou indiquez son chemin dans la variable SILX_NGSPICE. Détail : {ex.Message}"); }
        catch (Exception ex) { return new(raws, [], [], erc, journal.ToString(), ex.Message); }

        // Une analyse qui ne produit qu'un point (plage trop étroite, trop peu de points) ne se trace ni ne se mesure.
        if (raws.Values.FirstOrDefault(r => r.PointCount < 2) is { } court)
            return new(raws, [], [], erc, journal.ToString(), $"L'analyse n'a produit que {court.PointCount} point : élargissez la plage ou augmentez le nombre de points (bande étroite : balayage linéaire).");
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
    public static (double[] X, double[] Y) Courbe(AnalyseKind k, RawFile raw, string noeud) => Courbe(k, raw, noeud, null);

    /// <summary>Courbe d'un signal (tension de nœud, ou courant de broche résolu sur le schéma fusionné <paramref name="f"/>).</summary>
    public static (double[] X, double[] Y) Courbe(AnalyseKind k, RawFile raw, string signal, Schematic? f)
    {
        var (vec, signe) = Signaux.Vecteur(signal, f) ?? throw new ArgumentException($"Signal « {signal} » indisponible.");
        if (k == AnalyseKind.Transitoire) { var c = Temporelle(raw, vec); return (c.Temps.ToArray(), c.Valeurs.ToArray().Select(v => v * signe).ToArray()); }
        var r = Frequentielle(raw, vec);
        return (r.Frequences.ToArray(), r.Valeurs.ToArray().Select(Frequentiel.Db).ToArray());
    }

    public static ResultatGabarit Evaluer(CritereGabarit g, RawFile raw)
    {
        var info = g.Info;
        var bornes = new Bornes(g.Min, g.Max);
        string vec = $"v({g.Noeud})";
        if (!raw.Variables.Any(v => string.Equals(v.Name, vec, StringComparison.OrdinalIgnoreCase)))
            return new(info.Nom, Verdict.NonEvaluable, null, info.Unite, bornes, $"Nœud « {g.Noeud} » absent des résultats.");
        IMesure<CourbeTemporelle>? mt = g.Mesure switch
        {
            TypeMesure.Depassement => Catalogue.Depassement,
            TypeMesure.TempsDeMontee => Catalogue.TempsDeMontee,
            TypeMesure.TempsEtablissement => Catalogue.TempsEtablissement2Pct,
            TypeMesure.ValeurFinale => new Mesure<CourbeTemporelle>("Valeur finale", "V", c => Transitoire.ValeurFinale(c)),
            TypeMesure.Frequence => new Mesure<CourbeTemporelle>("Fréquence", "Hz", MesuresEtablies.Frequence),
            TypeMesure.AmplitudeCreteACrete => new Mesure<CourbeTemporelle>("Amplitude crête à crête", "V", MesuresEtablies.CreteACrete),
            _ => null
        };
        IMesure<ReponseFrequentielle>? mf = g.Mesure switch
        {
            TypeMesure.BandePassante3dB => Catalogue.BandePassante3dB,
            TypeMesure.GainDc => new Mesure<ReponseFrequentielle>("Gain BF", "dB", r => Frequentiel.Db(r.Valeurs[0])),
            TypeMesure.MargeDePhase => Catalogue.MargeDePhase,
            TypeMesure.MargeDeGain => Catalogue.MargeDeGain,
            TypeMesure.GainMax => new Mesure<ReponseFrequentielle>("Gain maximal", "dB", r => r.Valeurs.ToArray().Max(z => Frequentiel.Db(z))),
            TypeMesure.LargeurBande3dB => new Mesure<ReponseFrequentielle>("Largeur de bande à −3 dB", "Hz", MesuresEtablies.LargeurBande3dB),
            _ => null
        };
        if (bornes.Min is null && bornes.Max is null)
        {
            // Sans borne : on mesure quand même, pour aider à choisir les bornes.
            try
            {
                double v = mt != null ? mt.Mesurer(Temporelle(raw, vec)) : mf!.Mesurer(Frequentielle(raw, vec));
                return new(info.Nom, Verdict.NonEvaluable, double.IsFinite(v) ? v : null, info.Unite, bornes, "Aucune borne saisie.");
            }
            catch (Exception ex) when (ex is ArgumentException or MesureImpossibleException) { return new(info.Nom, Verdict.NonEvaluable, null, info.Unite, bornes, "Aucune borne saisie."); }
        }
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
