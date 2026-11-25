using System.Globalization;
using System.Text;
using Avalonia.Platform.Storage;
using Silx.Etude;
using Silx.Mesures.Gabarits;
using EtudeModel = Silx.Etude.Etude;
namespace Silx.App;

/// <summary>Rapport de test : un fichier Markdown (lisible tel quel, convertible en PDF) et une image de courbes par banc.</summary>
public sealed partial class MainWindow
{
    public partial async Task ExporterRapportAsync()
    {
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter le rapport de test", SuggestedFileName = $"Rapport - {_projet?.Nom ?? "Silx"}.md", DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }]
        });
        if (f?.TryGetLocalPath() is not { } chemin) return;
        await ExporterRapportVers(chemin);
    }

    /// <summary>Simule les bancs qui ne le sont pas encore, puis écrit le rapport et ses images.</summary>
    public async Task<string> ExporterRapportVers(string chemin)
    {
        foreach (var b in _etude.Bancs.Where(b => !_results.ContainsKey(b)).ToList())
            _results[b] = await Verification.RunAsync(_etude, b, []);
        var dossier = Path.Combine(Path.GetDirectoryName(chemin)!, Path.GetFileNameWithoutExtension(chemin) + "_courbes");
        var md = GenererRapport(_etude, _projet?.Nom ?? "Silx", _results, dossier, Path.GetFileName(dossier));
        await File.WriteAllTextAsync(chemin, md);
        _status.Text = $"Rapport exporté : {chemin}";
        RefreshBenchBar(); if (_banc == null) RefreshSide();
        return md;
    }

    static string Fmt(double? v, string u) => v is { } x ? SpiceNumber.Format(x, u) : "—";

    /// <summary>Contenu du rapport. Les images sont écrites dans <paramref name="dossierImages"/>.</summary>
    public static string GenererRapport(EtudeModel e, string nom, IReadOnlyDictionary<Banc, ResultatEtude> res, string dossierImages, string lienImages)
    {
        Directory.CreateDirectory(dossierImages);
        var sb = new StringBuilder();
        sb.AppendLine($"# Rapport de test — {nom}").AppendLine();
        sb.AppendLine($"Généré par Silx le {DateTime.Now.ToString("d MMMM yyyy à HH:mm", CultureInfo.GetCultureInfo("fr-FR"))}.").AppendLine();
        // Synthèse
        sb.AppendLine("## Synthèse").AppendLine();
        sb.AppendLine("| Banc | Type | Contraintes respectées | Verdict |").AppendLine("| --- | --- | --- | --- |");
        foreach (var b in e.Bancs)
        {
            var r = res.GetValueOrDefault(b);
            sb.AppendLine($"| {b.Nom} | {(b.Type == AnalyseKind.Ac ? "fréquentiel" : "temporel")} | {(r == null || r.Erreur != null ? "—" : $"{r.Respectes}/{r.Nombre}")} | {TexteVerdict(r)} |");
        }
        sb.AppendLine();
        int i = 0;
        foreach (var b in e.Bancs)
        {
            var r = res.GetValueOrDefault(b);
            sb.AppendLine($"## Banc « {b.Nom} »").AppendLine();
            var stim = b.Calque.Components.Where(k => k.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource).Select(k => $"{k.Ref} : {Stimulus.Lire(k.Value).Resume(k.Kind == ComponentKind.CurrentSource)}");
            sb.AppendLine($"Analyse {(b.Type == AnalyseKind.Ac ? $"fréquentielle de {SpiceNumber.Format(b.Ac.FDebut, "Hz")} à {SpiceNumber.Format(b.Ac.FFin, "Hz")}" : $"temporelle sur {SpiceNumber.Format(b.Transitoire.Duree, "s")}")}. Stimuli : {string.Join(" ; ", stim.DefaultIfEmpty("aucun"))}.").AppendLine();
            if (r?.Erreur is { } err) { sb.AppendLine($"Simulation impossible : {err}").AppendLine(); continue; }
            if (r != null && r.Nombre > 0)
            {
                sb.AppendLine("| Contrainte | Signal | Bornes | Mesuré / marge | Verdict |").AppendLine("| --- | --- | --- | --- | --- |");
                foreach (var c in r.Criteres)
                {
                    var u = c.Critere.Info.Unite;
                    var bornes = (c.Critere.Min, c.Critere.Max) switch { (double a, double x) => $"{Fmt(a, u)} … {Fmt(x, u)}", (double a, null) => $"≥ {Fmt(a, u)}", (null, double x) => $"≤ {Fmt(x, u)}", _ => "—" };
                    sb.AppendLine($"| {c.Critere.Info.Nom} | V({c.Critere.Noeud}) | {bornes} | {Fmt(c.Resultat.Valeur, u)} | {TexteVerdict(c.Resultat.Verdict)} |");
                }
                foreach (var t in r.Traces)
                {
                    var m = t.Resultat.Marge is { } mm ? (mm >= 0 ? $"marge {SpiceNumber.Format(mm, t.Gabarit.Unite)}" : $"franchi de {SpiceNumber.Format(-mm, t.Gabarit.Unite)}") : "—";
                    sb.AppendLine($"| Gabarit {(t.Gabarit.Limite == Limite.Haute ? "haut" : "bas")} ({t.Gabarit.Points.Count} points) | V({t.Gabarit.Noeud}) | dessiné | {m} | {TexteVerdict(t.Resultat.Verdict)} |");
                }
                sb.AppendLine();
            }
            if (r != null && r.Resultats.TryGetValue(b.Type, out var raw))
            {
                var fichier = $"banc-{++i}.png";
                DessinerCourbes(e, b, raw, r, Path.Combine(dossierImages, fichier));
                sb.AppendLine($"![Courbes du banc {b.Nom}]({Uri.EscapeDataString(lienImages)}/{fichier})").AppendLine();
            }
        }
        return sb.ToString();
    }

    static string TexteVerdict(ResultatEtude? r) => r == null ? "non simulé" : r.Erreur != null ? "erreur" : r.Nombre == 0 ? "—" : TexteVerdict(r.Global);
    static string TexteVerdict(Verdict v) => v switch { Silx.Mesures.Gabarits.Verdict.Pass => "✓ conforme", Silx.Mesures.Gabarits.Verdict.Fail => "✕ non conforme", _ => "non évaluable" };

    /// <summary>Image des courbes d'un banc (signaux de son premier graphique, gabarits dessinés en rouge).</summary>
    static void DessinerCourbes(EtudeModel e, Banc b, Silx.Core.Raw.RawFile raw, ResultatEtude r, string png)
    {
        var p = new ScottPlot.Plot();
        bool ac = b.Type == AnalyseKind.Ac;
        double X(double x) => ac ? Math.Log10(x) : x * 1e3;
        var conn = Connectivity.Build(b.Fusion(e.Schema));
        var signaux = (b.Graphiques.FirstOrDefault() is { Count: > 0 } g ? g : conn.NamedNodes.ToList())
            .Where(n => raw.Variables.Any(v => string.Equals(v.Name, $"v({n})", StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var n in signaux)
        {
            var (xs, ys) = Verification.Courbe(b.Type, raw, n);
            var sc = p.Add.ScatterLine(xs.Select(X).ToArray(), ys); sc.LegendText = ac ? $"|V({n})| (dB)" : $"V({n})"; sc.LineWidth = 2;
        }
        foreach (var t in b.TracesActives.Where(t => signaux.Contains(t.Noeud)))
        {
            var l = p.Add.ScatterLine(t.Points.Select(q => X(q.X)).ToArray(), t.Points.Select(q => q.Y).ToArray());
            l.Color = ScottPlot.Color.FromHex("#b91c1c"); l.LineWidth = 2; l.LinePattern = ScottPlot.LinePattern.Dashed; l.LegendText = $"gabarit {(t.Limite == Limite.Haute ? "haut" : "bas")}";
        }
        if (ac)
            p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(), IntegerTicksOnly = true, LabelFormatter = x => SpiceNumber.Format(Math.Pow(10, x), "Hz") };
        p.Axes.Bottom.Label.Text = ac ? "Fréquence" : "Temps (ms)"; p.Axes.Left.Label.Text = ac ? "Gain (dB)" : "Tension (V)";
        p.Title(b.Nom); p.ShowLegend(ScottPlot.Alignment.LowerRight);
        p.SavePng(png, 900, 420);
    }
}
