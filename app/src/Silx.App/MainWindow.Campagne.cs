using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ScottPlot.Avalonia;
using Silx.Etude;
using Silx.Mesures.Gabarits;
namespace Silx.App;

/// <summary>
/// Campagnes d'un banc (balayage, Monte-Carlo, coins) : réglages dans la roue crantée du banc, exécution parallèle avec
/// progression et annulation, taux de conformité dans le plan de test, courbes superposées et vue « Campagne » du tiroir.
/// </summary>
public partial class MainWindow
{
    readonly Dictionary<Banc, ResultatCampagne> _campagnes = new();
    readonly ProgressBar _progCampagne = new() { Width = 160, Height = 6, Minimum = 0, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _annulerCampagne = new() { Content = "Annuler", FontSize = Ui.Aide, Padding = new Thickness(8, 1), IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    ResultatTirage? _tirageChoisi;

    /// <summary>Résultat de la campagne du banc actif (null : pas de campagne, ou résultats périmés).</summary>
    public ResultatCampagne? ResultatCampagne => _banc != null && _campagnes.TryGetValue(_banc, out var c) ? c : null;
    public ResultatTirage? TirageChoisi => _tirageChoisi;

    static string NomMode(ModeCampagne m) => m switch
    {
        ModeCampagne.Balayage => "Balayage", ModeCampagne.MonteCarlo => "Monte-Carlo", ModeCampagne.Coins => "Coins (pire cas)", _ => "Valeur nominale"
    };

    void OublierCampagne() { if (_banc != null && _campagnes.Remove(_banc)) { _tirageChoisi = null; if (_detailOuvert == Detail.Campagne) OuvrirDetail(Detail.Campagne); } }

    /// <summary>Lance la campagne du banc actif (après la simulation nominale), sur tous les cœurs, annulable.</summary>
    public async Task LancerCampagneAsync(CancellationToken ct)
    {
        if (_banc == null || Banc.Campagne.Mode == ModeCampagne.Aucune) return;
        var banc = Banc;
        var plan = Campagnes.Plan(banc.Campagne, banc.Fusion(_etude.Schema), out var avert);
        if (plan.Count == 0) { _status.Text = "⚠ " + (avert ?? "Campagne vide."); return; }
        var signaux = (banc.Graphiques.Count > 0 ? banc.Graphiques.SelectMany(g => g) : _panes.SelectMany(p => p.Signaux))
            .Concat(banc.CriteresActifs.Select(c => c.Noeud)).Concat(banc.TracesActives.Select(t => t.Noeud)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _progCampagne.Maximum = plan.Count; _progCampagne.Value = 0; _progCampagne.IsVisible = _annulerCampagne.IsVisible = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool fini = false;   // les dernières notifications de progression arrivent parfois après la fin
        var prog = new Progress<int>(n => { if (fini) return; _progCampagne.Value = n; _status.Text = $"{NomMode(banc.Campagne.Mode)} : {n}/{plan.Count} simulation(s)…"; });
        try
        {
            var r = await Campagnes.RunAsync(_etude, banc, signaux, prog, ct);
            fini = true;
            _campagnes[banc] = r; _tirageChoisi = null;
            _status.Text = $"{NomMode(r.Mode)} : {r.Tirages.Count} simulation(s) en {sw.Elapsed.TotalSeconds:0.0} s · {r.Taux * 100:0.#} % conforme ({r.Conformes}/{r.Tirages.Count})"
                + (r.PireCas is { } p ? $" · pire cas : {p.Tirage.Libelle}" : "") + (r.Avertissement != null ? " · ⚠ " + r.Avertissement : "");
            if (banc == _banc) SetVerdict(r.Conformes == r.Tirages.Count ? Verdict.Pass : Verdict.Fail);
        }
        catch (OperationCanceledException) { fini = true; _status.Text = $"{NomMode(banc.Campagne.Mode)} annulé."; }
        finally { _progCampagne.IsVisible = _annulerCampagne.IsVisible = false; }
        RefreshGabarits(); RefreshPlot(keepLimits: true);
        if (_detailOuvert == Detail.Campagne) OuvrirDetail(Detail.Campagne);
    }

    /// <summary>Sélectionne un tirage : ses courbes sont mises en avant, ses valeurs affichées.</summary>
    public void ChoisirTirage(ResultatTirage? t)
    {
        _tirageChoisi = t;
        if (t != null) _status.Text = $"{t.Tirage.Libelle} · {(t.Conforme ? "conforme" : $"{t.Echecs} contrainte(s) franchie(s)")} · " + ValeursTirage(t);
        RefreshPlot(keepLimits: true);
        if (_detailOuvert == Detail.Campagne) OuvrirDetail(Detail.Campagne);
    }

    static string ValeursTirage(ResultatTirage t)
    {
        var parts = t.Tirage.Valeurs.OrderBy(k => k.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} = {SpiceNumber.Format(kv.Value)}").ToList();
        if (t.Tirage.Temperature is { } temp) parts.Add($"T = {temp:0.#} °C");
        return string.Join(" · ", parts);
    }

    // ───────────────────────── réglages (roue crantée du banc) ─────────────────────────
    Control EditeurCampagne()
    {
        var sp = new StackPanel { Spacing = 6 };
        var c = Banc.Campagne;
        var seg = new WrapPanel();
        var champs = new StackPanel { Spacing = 6 };
        foreach (var m in Enum.GetValues<ModeCampagne>())
        {
            bool on = c.Mode == m;
            var b = new Button { Content = m == ModeCampagne.Aucune ? "Nominal" : m == ModeCampagne.Coins ? "Coins" : NomMode(m), FontSize = Ui.Aide, Padding = new Thickness(9, 3), Margin = new Thickness(0, 0, 0, 2),
                BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Background = on ? Ui.AccentBrush : Ui.Surface, Foreground = on ? Brushes.White : Ui.Ink,
                CornerRadius = m == ModeCampagne.Aucune ? new CornerRadius(6, 0, 0, 6) : m == ModeCampagne.Coins ? new CornerRadius(0, 6, 6, 0) : new CornerRadius(0) };
            var mm = m; b.Click += (_, _) => { c.Mode = mm; OublierCampagne(); Remplir(); };
            seg.Children.Add(b);
        }
        sp.Children.Add(seg);
        sp.Children.Add(champs);
        void Remplir()
        {
            foreach (var (b, m) in seg.Children.OfType<Button>().Zip(Enum.GetValues<ModeCampagne>()))
            { bool on = c.Mode == m; b.Background = on ? Ui.AccentBrush : Ui.Surface; b.Foreground = on ? Brushes.White : Ui.Ink; }
            champs.Children.Clear();
            void Grille(params Control[] cs)
            {
                var g = new Grid { ColumnDefinitions = new(string.Join(",8,", cs.Select(_ => "*"))) };
                for (int i = 0; i < cs.Length; i++) { Grid.SetColumn(cs[i], 2 * i); g.Children.Add(cs[i]); }
                champs.Children.Add(g);
            }
            Control Nombre(string lib, double v, string unite, Action<double> ecrire) =>
                Ui.Field(lib, SpiceNumber.Format(v, unite), s => { if (SpiceNumber.ParseOrNull(s.Replace("°C", "")) is { } x) { ecrire(x); OublierCampagne(); } });
            switch (c.Mode)
            {
                case ModeCampagne.Aucune:
                    champs.Children.Add(Ui.Hint("« Simuler » calcule le circuit avec ses valeurs nominales."));
                    break;
                case ModeCampagne.Balayage:
                {
                    var cibles = Banc.Fusion(_etude.Schema).Components.Where(Campagnes.Variable).Select(k => k.Ref).OrderBy(r => r, StringComparer.Ordinal).Append("TEMP").ToList();
                    if (!cibles.Contains(c.Cible, StringComparer.OrdinalIgnoreCase)) c.Cible = cibles[0];
                    var cb = new ComboBox { ItemsSource = cibles.Select(x => x == "TEMP" ? "Température (°C)" : x).ToList(), SelectedIndex = cibles.FindIndex(x => x.Equals(c.Cible, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch };
                    cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) { c.Cible = cibles[cb.SelectedIndex]; OublierCampagne(); } };
                    champs.Children.Add(Ui.Labeled("Paramètre balayé", cb));
                    var ech = new ComboBox { ItemsSource = new[] { "Linéaire", "Logarithmique", "Liste de valeurs" }, SelectedIndex = (int)c.Echelle, HorizontalAlignment = HorizontalAlignment.Stretch };
                    ech.SelectionChanged += (_, _) => { if (ech.SelectedIndex >= 0 && (int)c.Echelle != ech.SelectedIndex) { c.Echelle = (EchelleBalayage)ech.SelectedIndex; OublierCampagne(); Remplir(); } };
                    champs.Children.Add(Ui.Labeled("Échelle", ech));
                    if (c.Echelle == EchelleBalayage.Liste)
                        champs.Children.Add(Ui.Field("Valeurs (séparées par des espaces)", c.Liste, s => { c.Liste = s; OublierCampagne(); }, "1k 2,2k 4,7k"));
                    else
                    {
                        var pts = new NumericUpDown { Value = c.Points, Minimum = 2, Maximum = 1000, Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };
                        pts.ValueChanged += (_, _) => { c.Points = (int)(pts.Value ?? 5); OublierCampagne(); };
                        Grille(Nombre("De", c.Debut, "", x => c.Debut = x), Nombre("À", c.Fin, "", x => c.Fin = x), Ui.Labeled("Points", pts));
                    }
                    champs.Children.Add(Ui.Hint("Une simulation par valeur. Les courbes se superposent en dégradé ; la vue « Campagne » montre chaque mesure selon le paramètre."));
                    break;
                }
                case ModeCampagne.MonteCarlo:
                {
                    var n = new NumericUpDown { Value = c.Tirages, Minimum = 2, Maximum = 100000, Increment = 50, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };
                    n.ValueChanged += (_, _) => { c.Tirages = (int)(n.Value ?? 100); OublierCampagne(); };
                    var loi = new ComboBox { ItemsSource = new[] { "Gaussienne (± tol. = 3σ)", "Uniforme" }, SelectedIndex = (int)c.Loi, HorizontalAlignment = HorizontalAlignment.Stretch };
                    loi.SelectionChanged += (_, _) => { if (loi.SelectedIndex >= 0) { c.Loi = (LoiTirage)loi.SelectedIndex; OublierCampagne(); } };
                    var graine = new NumericUpDown { Value = c.Graine, Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };
                    graine.ValueChanged += (_, _) => { c.Graine = (int)(graine.Value ?? 1); OublierCampagne(); };
                    Grille(Ui.Labeled("Tirages", n), Ui.Labeled("Graine", graine));
                    champs.Children.Add(Ui.Labeled("Loi", loi));
                    champs.Children.Add(Ui.Hint(ResumeTolerances() + " Même graine, mêmes tirages."));
                    break;
                }
                case ModeCampagne.Coins:
                {
                    var t = new CheckBox { Content = "Inclure la température", IsChecked = c.CoinsTemperature, FontSize = Ui.Aide };
                    t.IsCheckedChanged += (_, _) => { c.CoinsTemperature = t.IsChecked == true; OublierCampagne(); Remplir(); };
                    champs.Children.Add(t);
                    if (c.CoinsTemperature) Grille(Nombre("T mini (°C)", c.TMin, "", x => c.TMin = x), Nombre("T maxi (°C)", c.TMax, "", x => c.TMax = x));
                    var plan = Campagnes.Plan(c, Banc.Fusion(_etude.Schema), out var avert);
                    champs.Children.Add(Ui.Hint($"{ResumeTolerances()} {plan.Count} combinaison(s) extrême(s) à simuler.{(avert != null ? " " + avert : "")}"));
                    break;
                }
            }
        }
        Remplir();
        return sp;
    }

    string ResumeTolerances()
    {
        var t = Banc.Fusion(_etude.Schema).Components.Where(k => Campagnes.Tolerance(k) != null).Select(k => $"{k.Ref} ±{Campagnes.Tolerance(k):0.##} %").ToList();
        return t.Count == 0 ? "Aucun composant n'a de tolérance (réglez-la dans la bulle du composant, ou les défauts dans Paramètres)." : "Tolérances : " + string.Join(", ", t.Take(8)) + (t.Count > 8 ? "…" : "") + ".";
    }

    // ───────────────────────── plan de test ─────────────────────────
    string? ResumeCampagne() => ResultatCampagne is { } r
        ? $"{NomMode(r.Mode)} : {r.Taux * 100:0.#} % conforme ({r.Conformes}/{r.Tirages.Count})" : null;

    string StatCritere(CritereGabarit g)
    {
        if (ResultatCampagne?.Stats.FirstOrDefault(s => s.Critere == g) is not { N: > 0 } s) return "";
        var u = g.Info.Unite;
        if (ResultatCampagne!.Mode == ModeCampagne.Balayage)
            return $"\nBalayage : de {SpiceNumber.Format(s.Min, u)} à {SpiceNumber.Format(s.Max, u)}" + (s.Echecs > 0 ? $" · {s.Echecs} échec(s) sur {ResultatCampagne.Tirages.Count}" : " · toujours conforme");
        return $"\n{NomMode(ResultatCampagne!.Mode)} : μ {SpiceNumber.Format(s.Moyenne, u)} · σ {SpiceNumber.Format(s.EcartType, u)} · {SpiceNumber.Format(s.Min, u)} … {SpiceNumber.Format(s.Max, u)}"
            + (s.Cpk is { } k ? $" · Cpk {Cpk(k)}" : "") + (s.Echecs > 0 ? $" · {s.Echecs} échec(s)" : "");
    }

    public static string Cpk(double k) => k > 99 ? "> 99" : k.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Verdict affiché sur une ligne du plan : avec une campagne, non conforme dès qu'un tirage franchit la contrainte.</summary>
    Verdict? VerdictPlan(object cle, Verdict? nominal)
    {
        if (ResultatCampagne is not { } r) return nominal;
        bool echec = cle switch
        {
            CritereGabarit c => r.Tirages.Any(t => t.Criteres.TryGetValue(c, out var x) && x.Verdict == Verdict.Fail),
            GabaritTrace g => r.Tirages.Any(t => t.Traces.TryGetValue(g, out var x) && x.Verdict == Verdict.Fail),
            _ => false
        };
        return echec ? Verdict.Fail : nominal;
    }

    string StatTrace(GabaritTrace g)
    {
        if (ResultatCampagne is not { } r) return "";
        int e = r.Tirages.Count(t => t.Traces.TryGetValue(g, out var x) && x.Verdict == Verdict.Fail);
        return $"\n{NomMode(r.Mode)} : {(e == 0 ? "respecté par tous les tirages" : $"{e} échec(s) sur {r.Tirages.Count}")}";
    }

    // ───────────────────────── courbes superposées ─────────────────────────
    /// <summary>Courbes des tirages sous les courbes nominales : dégradé pour un balayage, gris (rouge si non conforme) sinon.</summary>
    (double Min, double Max) DessinerCampagne(ScottPlot.Plot p, Pane pane, IReadOnlyList<string> nodes, Func<double, double> X, ScottPlot.Color[] couleurs)
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        if (ResultatCampagne is not { } r || VueFft) return (lo, hi);
        var tirages = r.Tirages.Where(t => t.Erreur == null).Take(400).ToList();
        var cmap = new ScottPlot.Colormaps.Viridis();
        var rouge = Parametres.VersPlot(Parametres.Courant.NonConforme);
        for (int i = 0; i < tirages.Count; i++)
        {
            var t = tirages[i];
            for (int j = 0; j < nodes.Count && j < couleurs.Length; j++)
            {
                if (!t.Courbes.TryGetValue(nodes[j], out var c) && !t.Courbes.TryGetValue(nodes[j].ToUpperInvariant(), out c)) continue;
                var sc = p.Add.ScatterLine(c.X.Select(X).ToArray(), c.Y);
                bool choisi = t == _tirageChoisi;
                sc.Color = choisi ? Parametres.VersPlot(((ISolidColorBrush)Ui.AccentBrush).Color)
                    : r.Mode == ModeCampagne.Balayage ? cmap.GetColor(tirages.Count == 1 ? 0 : (double)i / (tirages.Count - 1)).WithAlpha(0.85)
                    : t.Conforme ? couleurs[j].WithAlpha(0.16) : rouge.WithAlpha(0.45);
                sc.LineWidth = choisi ? 2.5f : r.Mode == ModeCampagne.Balayage ? 1.4f : 1f;
                lo = Math.Min(lo, c.Y.Min()); hi = Math.Max(hi, c.Y.Max());
            }
        }
        return (lo, hi);
    }

    // ───────────────────────── vue « Campagne » du tiroir ─────────────────────────
    Control VueCampagne()
    {
        if (ResultatCampagne is not { } r)
            return new TextBlock { Text = _banc == null ? "Sélectionnez un banc." : Banc.Campagne.Mode == ModeCampagne.Aucune
                ? "Pas de campagne : choisissez Balayage, Monte-Carlo ou Coins dans la roue crantée du banc, puis « Simuler »."
                : "Campagne réglée mais pas encore lancée : « Simuler ».", Margin = new Thickness(12), Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap };
        var g = new Grid { ColumnDefinitions = new("*,330") };
        var graphes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 4) };
        foreach (var s in r.Stats) graphes.Children.Add(GrapheMesure(r, s));
        if (r.Stats.Count == 0) graphes.Children.Add(new TextBlock { Text = "Aucun critère chiffré : ajoutez un critère (temps de montée, fc…) pour voir sa dispersion.", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center });
        g.Children.Add(new ScrollViewer { Content = graphes, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        // Liste des tirages : non conformes d'abord, puis du plus défavorable au plus sûr.
        var liste = new StackPanel { Spacing = 1, Margin = new Thickness(4) };
        liste.Children.Add(new TextBlock { Text = $"{r.Tirages.Count} tirage(s) · {r.Taux * 100:0.#} % conforme · clic : ses courbes et ses valeurs", FontSize = Ui.Aide, Foreground = Ui.Muted, Margin = new Thickness(4, 0, 0, 4) });
        foreach (var t in r.Tirages.OrderBy(t => t.Conforme).ThenByDescending(t => t.Echecs).ThenBy(t => t.MargeRelative).Take(300))
        {
            bool sel = t == _tirageChoisi, pire = t == r.PireCas;
            var row = new StackPanel { Spacing = 0 };
            row.Children.Add(new TextBlock { Text = $"{(t.Erreur != null ? "⚠" : t.Conforme ? "✓" : "✕")} {t.Tirage.Libelle}{(pire ? "  · pire cas" : "")}", FontSize = 12, FontWeight = pire ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = t.Erreur != null ? Ui.WarnBrush : t.Conforme ? Ui.PassBrush : Ui.FailBrush });
            var v = t.Erreur ?? ValeursTirage(t);
            if (v.Length > 0) row.Children.Add(new TextBlock { Text = v, FontSize = 10.5, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 2), Background = sel ? Ui.ActiveBg : Brushes.Transparent, CornerRadius = new CornerRadius(4) };
            var tt = t; b.Click += (_, _) => ChoisirTirage(sel ? null : tt);
            liste.Children.Add(b);
        }
        var droite = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = liste } };
        Grid.SetColumn(droite, 1); g.Children.Add(droite);
        return g;
    }

    /// <summary>Histogramme d'une mesure (Monte-Carlo, coins) ou mesure selon le paramètre (balayage), bornes du critère en rouge.</summary>
    Control GrapheMesure(ResultatCampagne r, StatMesure s)
    {
        var ap = new AvaPlot { Width = 330, Height = 170 };
        StylerGraphique(ap.Plot);
        RemplirGraphe(ap.Plot, r, s, Banc.Campagne.Echelle);
        ap.Refresh();
        return new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = ap, ClipToBounds = true };
    }

    /// <summary>Contenu d'un graphique de dispersion (partagé avec le rapport de test).</summary>
    public static void RemplirGraphe(ScottPlot.Plot p, ResultatCampagne r, StatMesure s, EchelleBalayage echelle)
    {
        var u = s.Critere.Info.Unite;
        var rouge = Parametres.VersPlot(Parametres.Courant.NonConforme);
        var accent = Parametres.VersPlot(((ISolidColorBrush)Ui.AccentBrush).Color);
        var pts = r.Tirages.Where(t => t.Criteres.TryGetValue(s.Critere, out var x) && x.Valeur != null).Select(t => (T: t, V: t.Criteres[s.Critere].Valeur!.Value)).ToList();
        p.Title($"{s.Critere.Info.Nom} · V({s.Critere.Noeud})", size: 11);
        if (r.Mode == ModeCampagne.Balayage)
        {
            bool log = echelle == EchelleBalayage.Logarithmique;
            double Px(double x) => log ? Math.Log10(x) : x;
            var sc = p.Add.ScatterLine(pts.Select(q => Px(q.T.Tirage.Parametre ?? 0)).ToArray(), pts.Select(q => q.V).ToArray());
            sc.Color = accent; sc.MarkerSize = 6; sc.LineWidth = 1.5f;
            foreach (var q in pts.Where(q => q.T.Criteres[s.Critere].Verdict == Verdict.Fail)) { var m = p.Add.Marker(Px(q.T.Tirage.Parametre ?? 0), q.V); m.Color = rouge; m.Size = 9; }
            foreach (var b in new[] { s.Critere.Min, s.Critere.Max }.OfType<double>()) { var hl = p.Add.HorizontalLine(b); hl.Color = rouge; hl.LinePattern = ScottPlot.LinePattern.Dashed; hl.LineWidth = 1.5f; }
            p.Axes.Bottom.Label.Text = r.Cible.Equals("TEMP", StringComparison.OrdinalIgnoreCase) ? "Température (°C)" : r.Cible;
            if (log) p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { LabelFormatter = x => SpiceNumber.Format(Math.Pow(10, x)) };
            else p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { LabelFormatter = x => SpiceNumber.Format(x) };
            p.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { LabelFormatter = y => SpiceNumber.Format(y, u) };
        }
        else
        {
            // Histogramme : ~√N classes (entre 5 et 30), bornes du critère et moyenne.
            double lo = s.Min, hi = s.Max; if (!(hi > lo)) { lo -= Math.Abs(lo) * 0.01 + 1e-15; hi += Math.Abs(hi) * 0.01 + 1e-15; }
            int nb = Math.Clamp((int)Math.Sqrt(pts.Count), 5, 30); double w = (hi - lo) / nb;
            var comptes = new int[nb];
            foreach (var q in pts) comptes[Math.Clamp((int)((q.V - lo) / w), 0, nb - 1)]++;
            var barres = Enumerable.Range(0, nb).Select(i =>
            {
                double c = lo + (i + 0.5) * w;
                bool hors = (s.Critere.Min is { } a && c < a) || (s.Critere.Max is { } b && c > b);
                return new ScottPlot.Bar { Position = c, Value = comptes[i], Size = w * 0.92, FillColor = hors ? rouge.WithAlpha(0.8) : accent.WithAlpha(0.7), LineWidth = 0 };
            }).ToList();
            p.Add.Bars(barres);
            foreach (var b in new[] { s.Critere.Min, s.Critere.Max }.OfType<double>()) { var vl = p.Add.VerticalLine(b); vl.Color = rouge; vl.LinePattern = ScottPlot.LinePattern.Dashed; vl.LineWidth = 1.5f; }
            var mu = p.Add.VerticalLine(s.Moyenne); mu.Color = Parametres.VersPlot(((ISolidColorBrush)Ui.Muted).Color); mu.LineWidth = 1;
            p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { LabelFormatter = x => SpiceNumber.Format(x, u) };
            p.Axes.Bottom.Label.Text = $"μ {SpiceNumber.Format(s.Moyenne, u)} · σ {SpiceNumber.Format(s.EcartType, u)}" + (s.Cpk is { } k ? $" · Cpk {Cpk(k)}" : "");
            p.Axes.Margins(0.05, 0.1);
        }
        p.Axes.Bottom.Label.FontSize = 11; p.Axes.Left.Label.FontSize = 11;
    }
}
