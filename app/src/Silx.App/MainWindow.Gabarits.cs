using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ScottPlot.Avalonia;
using Silx.Etude;
using Silx.Mesures.Gabarits;
using EtudeModel = Silx.Etude.Etude;
namespace Silx.App;

/// <summary>Plan de test : critères et gabarits dessinés, édition et réévaluation.</summary>
public sealed partial class MainWindow
{
    // ───────────────────────── gabarits ─────────────────────────
    /// <summary>Contrainte dont la ligne est dépliée pour édition (critère) ou mise en avant (gabarit).</summary>
    object? _ligneOuverte;
    readonly TextBlock _resumePlan = new() { FontSize = 12, Foreground = Ui.Muted, Margin = new Thickness(0, -4, 0, 4) };
    public object? ContrainteMiseEnAvant => _ligneOuverte;

    void RefreshGabarits()
    {
        _gabarits.Children.Clear(); _traces.Children.Clear();
        if (_banc == null) return;
        int n = Banc.CriteresActifs.Count() + Banc.TracesActives.Count();
        _resumePlan.Text = n == 0 ? "Aucune contrainte : ajoutez un critère ou dessinez un gabarit sur les courbes."
            : ResumeCampagne() is { } rc ? rc
            : _last is { Erreur: null } r && r.Nombre > 0 ? $"{r.Respectes}/{r.Nombre} respectée(s) · banc {(Banc.Type == AnalyseKind.Ac ? "fréquentiel" : "temporel")}"
            : $"{n} contrainte(s) · pas encore simulé";
        // Signaux contrôlables : nœuds du schéma et courants sondés dans les graphiques du banc.
        var nodes = Connectivity.Build(SchemaCourant).AllNodes.Concat(_panes.SelectMany(p => p.Signaux).Where(Signaux.EstCourant).Distinct()).ToList();
        foreach (var g in Banc.CriteresActifs.ToList())
        {
            var res = _last?.Criteres.FirstOrDefault(x => x.Critere == g)?.Resultat;
            var unit = g.Info.Unite;
            string bornes = (g.Min, g.Max) switch
            {
                (double a, double b) => $"{SpiceNumber.Format(a, unit)} … {SpiceNumber.Format(b, unit)}",
                (double a, null) => $"≥ {SpiceNumber.Format(a, unit)}", (null, double b) => $"≤ {SpiceNumber.Format(b, unit)}", _ => "sans borne"
            };
            string mesure = res == null ? "pas encore simulé" : res.Valeur is { } v ? $"mesuré {SpiceNumber.Format(v, unit)}" + Marge(g, v) : res.Detail ?? "non évaluable";
            var ligne = LignePlan(VerdictPlan(g, res?.Verdict), $"{g.Info.Nom} · {Signaux.Nom(g.Noeud, SchemaCourant)}", $"{bornes} · {mesure}{StatCritere(g)}", g, () => { Banc.Gabarits.Remove(g); Invalidate(); }, "Retirer ce critère");
            _gabarits.Children.Add(ligne);
            if (_ligneOuverte == g) _gabarits.Children.Add(EditeurCritere(g, nodes));
        }
        foreach (var g in Banc.TracesActives.ToList())
        {
            var res = _last?.Traces.FirstOrDefault(t => t.Gabarit == g)?.Resultat;
            string detail = res == null ? $"{g.Points.Count} points · pas encore simulé" : res.Marge is { } m
                ? (m >= 0 ? $"marge {SpiceNumber.Format(m, g.Unite)} à {Fx(g.Analyse, res.XPire!.Value)}" : $"franchi de {SpiceNumber.Format(-m, g.Unite)} à {Fx(g.Analyse, res.XPire!.Value)}")
                : res.Detail ?? "non évaluable";
            var titre = $"Gabarit {(g.Limite == Limite.Haute ? "haut" : "bas")} · {(g.Analyse == AnalyseKind.Ac ? $"|{Signaux.Nom(g.Noeud, SchemaCourant)}|" : Signaux.Nom(g.Noeud, SchemaCourant))}";
            if (g.Relatif) detail += $" · suit le front (t0 = {SpiceNumber.Format(Banc.InstantFront, "s")})";
            detail += StatTrace(g);
            _gabarits.Children.Add(LignePlan(VerdictPlan(g, res?.Verdict), titre, detail, g, () => { Banc.GabaritsTraces.Remove(g); _ligneOuverte = null; ReevaluateTraces(); RefreshGabarits(); RefreshPlot(keepLimits: true); }, "Retirer ce gabarit"));
            if (_ligneOuverte == g && g.Analyse == AnalyseKind.Transitoire) _gabarits.Children.Add(EditeurTrace(g));
        }
    }

    /// <summary>Gabarit temporel déplié : position absolue, ou accrochée au front du stimulus.</summary>
    Control EditeurTrace(GabaritTrace g)
    {
        var sp = new StackPanel { Spacing = 4, Margin = new Thickness(8, 2, 0, 8) };
        var seg = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (rel, txt) in new[] { (false, "Temps absolu"), (true, "Relatif au front") })
        {
            bool on = g.Relatif == rel;
            var b = new Button { Content = txt, FontSize = Ui.Aide, Padding = new Thickness(10, 3), BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
                Background = on ? Ui.AccentBrush : Ui.Surface, Foreground = on ? Brushes.White : Ui.Ink,
                CornerRadius = rel ? new CornerRadius(0, 6, 6, 0) : new CornerRadius(6, 0, 0, 6) };
            var r = rel; b.Click += (_, _) => RendreRelatif(g, r);
            seg.Children.Add(b);
        }
        sp.Children.Add(seg);
        sp.Children.Add(Ui.Hint(g.Relatif
            ? $"Les points suivent le front du stimulus (t0 = {SpiceNumber.Format(Banc.InstantFront, "s")}) : si son retard change, le gabarit se décale d'autant."
            : "Les points restent aux mêmes instants, quel que soit le retard du stimulus."));
        return sp;
    }

    /// <summary>Accroche (ou décroche) un gabarit temporel au front du stimulus, sans le déplacer.</summary>
    public void RendreRelatif(GabaritTrace g, bool relatif)
    {
        g.Relatif = relatif; g.Ancrage = Banc.InstantFront;
        RefreshGabarits(); RefreshPlot(keepLimits: true);
    }

    static string Marge(CritereGabarit g, double v)
    {
        double? m = (g.Min, g.Max) switch { (double a, double b) => Math.Min(v - a, b - v), (double a, null) => v - a, (null, double b) => b - v, _ => null };
        return m is { } x && x >= 0 ? $" · marge {SpiceNumber.Format(x, g.Info.Unite)}" : "";
    }

    /// <summary>Ligne du plan de test : verdict, intitulé, bornes et mesure ; clic pour déplier ou mettre en avant.</summary>
    Control LignePlan(Verdict? v, string titre, string detail, object cle, Action retirer, string tipRetirer)
    {
        var g = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        var badge = Ui.Badge(v); badge.VerticalAlignment = VerticalAlignment.Top; badge.Margin = new Thickness(0, 2, 8, 0); g.Children.Add(badge);
        var txt = new StackPanel { Spacing = 1 };
        txt.Children.Add(new TextBlock { Text = titre, FontWeight = FontWeight.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        txt.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Foreground = v == Verdict.Fail ? Ui.FailBrush : Ui.Muted, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(txt, 1); g.Children.Add(txt);
        var del = new Button { Content = Icones.Make(Icones.Croix, 10, Ui.Muted), Background = Brushes.Transparent, Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Top };
        ToolTip.SetTip(del, tipRetirer); del.Click += (_, e) => { e.Handled = true; retirer(); };
        Grid.SetColumn(del, 2); g.Children.Add(del);
        bool ouverte = _ligneOuverte == cle;
        var b = new Button
        {
            Content = g, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 6),
            Background = ouverte ? Ui.ActiveBg : Ui.Surface, BorderBrush = v == Verdict.Fail ? Ui.FailBrush : ouverte ? Ui.AccentBrush : Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6)
        };
        ToolTip.SetTip(b, cle is CritereGabarit ? "Cliquer pour modifier ; le signal s'affiche sur les courbes" : "Cliquer pour mettre ce gabarit en avant sur les courbes");
        b.Click += (_, _) => { _ligneOuverte = ouverte ? null : cle; if (!ouverte) MontrerContrainte(cle); RefreshGabarits(); RefreshPlot(keepLimits: true); };
        return b;
    }

    /// <summary>Met une contrainte en avant : son signal est affiché (ajouté au graphique actif s'il n'y est pas).</summary>
    void MontrerContrainte(object cle)
    {
        var noeud = cle switch { CritereGabarit c => c.Noeud, GabaritTrace t => t.Noeud, _ => null };
        if (noeud == null || _banc == null) return;
        if (_panes.FirstOrDefault(p => p.Signaux.Contains(noeud)) is { } pane) Activate(pane);
        else if (_active != null) { _active.Signaux.Add(noeud); var i = _panes.IndexOf(_active); RebuildPanes(); Activate(_panes[i]); }
        if (_disposition == Disposition.Schema) Disposer(Disposition.Mixte);
    }

    Control EditeurCritere(CritereGabarit g, List<string> nodes)
    {
        var card = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0, 0, 6) };
        var choix = TypesMesure.Tous.Where(i => i.Analyse == Banc.Type).ToList();
        var mesure = new ComboBox { ItemsSource = choix.Select(i => i.Nom).ToList(), SelectedIndex = choix.FindIndex(i => i.Type == g.Mesure), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        mesure.SelectionChanged += (_, _) => { if (mesure.SelectedIndex >= 0 && choix[mesure.SelectedIndex].Type != g.Mesure) { g.Mesure = choix[mesure.SelectedIndex].Type; Invalidate(); } };
        card.Children.Add(Ui.Labeled("Mesure", mesure));
        var row = new Grid { ColumnDefinitions = new("*,6,*,6,*") };
        var nodeItems = nodes.Contains(g.Noeud, StringComparer.OrdinalIgnoreCase) ? nodes : nodes.Append(g.Noeud).ToList();
        var node = new ComboBox { ItemsSource = nodeItems, SelectedItem = nodeItems.First(n => n.Equals(g.Noeud, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
        node.SelectionChanged += (_, _) => { if (node.SelectedItem is string n && n != g.Noeud) { g.Noeud = n; Invalidate(); } };
        var unit = g.Info.Unite;
        row.Children.Add(Ui.Labeled("Signal", node));
        var min = Ui.Field($"Min ({unit})", g.Min is { } a ? SpiceNumber.Format(a, unit) : "", v => { var x = SpiceNumber.ParseOrNull(v); if (x != g.Min) { g.Min = x; Invalidate(); } }, "—");
        Grid.SetColumn(min, 2); row.Children.Add(min);
        var max = Ui.Field($"Max ({unit})", g.Max is { } b ? SpiceNumber.Format(b, unit) : "", v => { var x = SpiceNumber.ParseOrNull(v); if (x != g.Max) { g.Max = x; Invalidate(); } }, "—");
        Grid.SetColumn(max, 4); row.Children.Add(max);
        card.Children.Add(row);
        return card;
    }

    static string Fx(AnalyseKind k, double x) => k == AnalyseKind.Ac ? SpiceNumber.Format(x, "Hz") : SpiceNumber.Format(x, "s");

    /// <summary>Un critère a changé : les résultats affichés ne correspondent plus.</summary>
    void Invalidate()
    {
        // Un critère a changé : on le réévalue sur les résultats déjà calculés (pas besoin de resimuler).
        if (_banc != null && _last is { Erreur: null } r)
        {
            var crit = Banc.CriteresActifs.Where(g => r.Resultats.ContainsKey(g.Info.Analyse))
                .Select(g => new ResultatCritere(g, Verification.Evaluer(g, r.Resultats[g.Info.Analyse], SchemaCourant))).ToList();
            _last = r with { Criteres = crit };
            SetVerdict(_last.Nombre == 0 ? null : _last.Global);
        }
        else SetVerdict(null);
        RefreshGabarits(); RefreshBenchBar(); RefreshPlot(keepLimits: true);
    }
}
