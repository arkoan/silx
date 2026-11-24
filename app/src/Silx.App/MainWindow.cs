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

/// <summary>
/// Fenêtre principale. Organisation : palette de composants (gauche), schéma / netlist (centre),
/// propriétés + analyse + gabarits (droite), courbes / vérifications / journal (bas).
/// </summary>
public sealed class MainWindow : Window
{
    EtudeModel _etude = EtudeModel.Demo();
    readonly SchematicCanvas _canvas = new();
    readonly TextBox _netlist = Mono();
    readonly TextBox _journal = Mono();
    readonly StackPanel _ercList = new() { Spacing = 4, Margin = new Thickness(10) };
    readonly AvaPlot _plot = new();
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted, FontSize = 12 };
    readonly Border _verdict = new() { CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly StackPanel _props = new() { Spacing = 6 };
    readonly StackPanel _gabarits = new() { Spacing = 8 };
    readonly StackPanel _palette = new() { Spacing = 2 };
    readonly TabControl _bottomTabs = new();
    readonly TabItem _ercTab = new();
    readonly Button _simulate;
    readonly RadioButton _showTran = new() { Content = "Transitoire", GroupName = "aff", IsChecked = true };
    readonly RadioButton _showAc = new() { Content = "AC (Bode)", GroupName = "aff" };
    readonly Dictionary<ComponentKind, Button> _paletteButtons = new();
    Button _toolSelect = null!, _toolWire = null!;
    ResultatEtude? _last;
    CancellationTokenSource? _cts;

    AnalyseKind Shown => _showAc.IsChecked == true ? AnalyseKind.Ac : AnalyseKind.Transitoire;

    public MainWindow()
    {
        Title = "Silx — étude sans titre"; Width = 1440; Height = 900; MinWidth = 1000; MinHeight = 640;
        Background = Ui.Window;

        _simulate = new Button
        {
            Content = "▶  Simuler et vérifier", Background = Ui.AccentBrush, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(14, 6), VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(_simulate, "Lance les analyses nécessaires et évalue les gabarits (F5 ou ⌘↵)");
        _simulate.Click += async (_, _) => await SimulateAsync();

        var root = new DockPanel();
        var top = BuildToolbar(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var status = new Border { Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 4), Child = _status };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var left = BuildPalette(); DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        var right = BuildSidePanel(); DockPanel.SetDock(right, Dock.Right); root.Children.Add(right);
        root.Children.Add(BuildCenter());
        Content = root;

        _canvas.Changed += OnSchemaChanged;
        _canvas.SelectionChanged += RefreshProperties;
        _canvas.ToolChanged += RefreshToolButtons;
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5 || (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Meta))) { e.Handled = true; await SimulateAsync(); }
        };
        _showTran.IsCheckedChanged += (_, _) => { if (_showTran.IsChecked == true) RefreshPlot(); };
        _showAc.IsCheckedChanged += (_, _) => { if (_showAc.IsChecked == true) RefreshPlot(); };

        LoadEtude(EtudeModel.Demo());
        _status.Text = "Clic : placer / sélectionner · glisser : déplacer · clic droit ou molette : vue · ⌘R : pivoter · W : fil · Suppr · ⌘Z / ⇧⌘Z";
    }

    public EtudeModel EtudeCourante => _etude;
    public void ShowAnalysis(AnalyseKind k) { (k == AnalyseKind.Ac ? _showAc : _showTran).IsChecked = true; RefreshPlot(); }

    void LoadEtude(EtudeModel e)
    {
        _etude = e; _last = null; _canvas.Load(e.Schema); RefreshGabarits(); RefreshPlot(); SetVerdict(null);
    }

    // ───────────────────────── barre d'outils ─────────────────────────
    Control BuildToolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 6) };
        var logo = new TextBlock { Text = "Silx", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Ui.AccentBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 14, 0) };
        bar.Children.Add(logo);
        bar.Children.Add(Ui.ToolButton("Nouveau", "Schéma vide", () => LoadEtude(new EtudeModel())));
        bar.Children.Add(Ui.ToolButton("Exemple RC", "Recharger le filtre RC de démonstration", () => LoadEtude(EtudeModel.Demo())));
        bar.Children.Add(Ui.Separator());
        _toolSelect = Ui.ToolButton("↖ Sélection", "Sélectionner et déplacer (Échap)", () => _canvas.Tool = Tool.Select);
        _toolWire = Ui.ToolButton("╱ Fil", "Tracer un fil (W) — double-clic ou arrivée sur une broche pour finir", () => _canvas.Tool = Tool.Wire);
        bar.Children.Add(_toolSelect); bar.Children.Add(_toolWire);
        bar.Children.Add(Ui.ToolButton("⟳ Pivoter", "Pivoter la sélection ou le composant à placer (⌘R)", () => _canvas.RotateSelected()));
        bar.Children.Add(Ui.ToolButton("✕ Supprimer", "Supprimer la sélection (Suppr)", () => _canvas.DeleteSelected()));
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Ui.ToolButton("↶", "Annuler (⌘Z)", () => _canvas.Undo()));
        bar.Children.Add(Ui.ToolButton("↷", "Rétablir (⇧⌘Z)", () => _canvas.Redo()));

        var rightSide = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(10, 6), HorizontalAlignment = HorizontalAlignment.Right };
        rightSide.Children.Add(_verdict); rightSide.Children.Add(_simulate);
        var g = new Grid { ColumnDefinitions = new("*,Auto") };
        g.Children.Add(bar); g.Children.Add(rightSide); Grid.SetColumn(rightSide, 1);
        return new Border { Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
    }

    void RefreshToolButtons()
    {
        Ui.SetActive(_toolSelect, _canvas.Tool == Tool.Select);
        Ui.SetActive(_toolWire, _canvas.Tool == Tool.Wire);
        foreach (var (k, b) in _paletteButtons) Ui.SetActive(b, _canvas.Tool == Tool.Place && _canvas.PlaceKind == k);
    }

    // ───────────────────────── palette ─────────────────────────
    Control BuildPalette()
    {
        var search = new TextBox { PlaceholderText = "Rechercher un composant…", Margin = new Thickness(0, 0, 0, 8) };
        var groups = new (string Titre, (ComponentKind K, string Nom, string Touche)[] Items)[]
        {
            ("Passifs", [(ComponentKind.Resistor, "Résistance", "R"), (ComponentKind.Capacitor, "Condensateur", "C"), (ComponentKind.Inductor, "Inductance", "L")]),
            ("Sources", [(ComponentKind.VoltageSource, "Source de tension", "V"), (ComponentKind.CurrentSource, "Source de courant", "I")]),
            ("Semi-conducteurs", [(ComponentKind.Diode, "Diode", "D")]),
            ("Connexions", [(ComponentKind.Ground, "Masse", "G"), (ComponentKind.Label, "Étiquette de nœud", "N")]),
        };
        var headers = new List<(TextBlock H, List<Button> Bs)>();
        foreach (var (titre, items) in groups)
        {
            var h = Ui.Section(titre); h.Margin = new Thickness(2, 10, 0, 4); _palette.Children.Add(h);
            var bs = new List<Button>();
            foreach (var (k, nom, touche) in items)
            {
                var row = new Grid { ColumnDefinitions = new("28,*,Auto") };
                var ico = new TextBlock { Text = Schematic.Prefix(k) == "GND" ? "⏚" : Schematic.Prefix(k) == "NET" ? "⚑" : Schematic.Prefix(k), FontWeight = FontWeight.Bold, Foreground = Ui.AccentBrush, VerticalAlignment = VerticalAlignment.Center };
                var lbl = new TextBlock { Text = nom, VerticalAlignment = VerticalAlignment.Center };
                var key = new Border { Child = new TextBlock { Text = touche, FontSize = 10, Foreground = Ui.Muted }, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0) };
                row.Children.Add(ico); row.Children.Add(lbl); Grid.SetColumn(lbl, 1); row.Children.Add(key); Grid.SetColumn(key, 2);
                var b = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, Padding = new Thickness(6, 5), Tag = nom };
                ToolTip.SetTip(b, $"Placer : {nom} (touche {touche}). Maj+clic pour en placer plusieurs.");
                var kind = k; b.Click += (_, _) => _canvas.BeginPlace(kind);
                _paletteButtons[k] = b; bs.Add(b); _palette.Children.Add(b);
            }
            headers.Add((h, bs));
        }
        search.TextChanged += (_, _) =>
        {
            var q = (search.Text ?? "").Trim();
            foreach (var (h, bs) in headers)
            {
                foreach (var b in bs) b.IsVisible = q.Length == 0 || ((string)b.Tag!).Contains(q, StringComparison.CurrentCultureIgnoreCase);
                h.IsVisible = bs.Any(b => b.IsVisible);
            }
        };
        var hint = new TextBlock
        {
            Text = "Bibliothèques fabricant, éditeur de symboles et transistors : à venir.", TextWrapping = TextWrapping.Wrap,
            FontSize = 11, Foreground = Ui.Muted, Margin = new Thickness(2, 16, 0, 0)
        };
        var stack = new StackPanel { Margin = new Thickness(10) };
        stack.Children.Add(Ui.Title("Composants")); stack.Children.Add(search); stack.Children.Add(_palette); stack.Children.Add(hint);
        return new Border { Width = 230, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = stack } };
    }

    // ───────────────────────── centre ─────────────────────────
    Control BuildCenter()
    {
        var tabs = new TabControl { Padding = new Thickness(0) };
        tabs.Items.Add(new TabItem { FontSize = 15, Header = "Schéma", Content = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = _canvas } });
        tabs.Items.Add(new TabItem { FontSize = 15, Header = "Netlist", Content = _netlist });

        _plot.Plot.FigureBackground.Color = ScottPlot.Colors.White;
        _ercTab.Header = "Vérifications"; _ercTab.FontSize = 15;
        _bottomTabs.Items.Add(new TabItem { FontSize = 15, Header = "Courbes", Content = _plot });
        _bottomTabs.Items.Add(_ercTab); _ercTab.Content = new ScrollViewer { Content = _ercList };
        _bottomTabs.Items.Add(new TabItem { FontSize = 15, Header = "Journal ngspice", Content = _journal });

        var g = new Grid { RowDefinitions = new("3*,6,2*") };
        g.Children.Add(tabs);
        var split = new GridSplitter { Background = Ui.Line, ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(split, 1); g.Children.Add(split);
        var bottom = new Border { Background = Ui.Panel, Child = _bottomTabs }; Grid.SetRow(bottom, 2); g.Children.Add(bottom);
        return g;
    }

    // ───────────────────────── panneau droit ─────────────────────────
    Control BuildSidePanel()
    {
        var s = new StackPanel { Spacing = 6, Margin = new Thickness(12) };
        s.Children.Add(Ui.Title("Propriétés")); s.Children.Add(_props);
        s.Children.Add(Ui.Rule());
        s.Children.Add(Ui.Title("Analyses"));
        s.Children.Add(BuildAnalysisPanel());
        s.Children.Add(Ui.Rule());
        var gh = new Grid { ColumnDefinitions = new("*,Auto") };
        gh.Children.Add(Ui.Title("Gabarits"));
        var add = Ui.ToolButton("+ Critère", "Ajouter un critère pass/fail", () =>
        {
            var n = Connectivity.Build(_etude.Schema).NamedNodes.FirstOrDefault() ?? "OUT";
            _etude.Gabarits.Add(new CritereGabarit { Mesure = TypeMesure.BandePassante3dB, Noeud = n }); RefreshGabarits();
        });
        Grid.SetColumn(add, 1); gh.Children.Add(add);
        s.Children.Add(gh); s.Children.Add(_gabarits);
        return new Border { Width = 350, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = s } };
    }

    Control BuildAnalysisPanel()
    {
        var s = new StackPanel { Spacing = 6 };
        var tran = new Grid { ColumnDefinitions = new("*,8,*") };
        tran.Children.Add(Ui.Field("Pas", SpiceNumber.Format(_etude.Transitoire.Pas, "s"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) _etude.Transitoire.Pas = x; }));
        var dur = Ui.Field("Durée", SpiceNumber.Format(_etude.Transitoire.Duree, "s"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) _etude.Transitoire.Duree = x; });
        Grid.SetColumn(dur, 2); tran.Children.Add(dur);
        var ac = new Grid { ColumnDefinitions = new("*,8,*,8,70") };
        ac.Children.Add(Ui.Field("De", SpiceNumber.Format(_etude.Ac.FDebut, "Hz"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) _etude.Ac.FDebut = x; }));
        var to = Ui.Field("À", SpiceNumber.Format(_etude.Ac.FFin, "Hz"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) _etude.Ac.FFin = x; });
        Grid.SetColumn(to, 2); ac.Children.Add(to);
        var pts = Ui.Field("Pts/déc.", _etude.Ac.PointsParDecade.ToString(), v => { if (int.TryParse(v, out var x) && x > 0) _etude.Ac.PointsParDecade = x; });
        Grid.SetColumn(pts, 4); ac.Children.Add(pts);

        s.Children.Add(Ui.SubTitle("Transitoire (.tran)")); s.Children.Add(tran);
        s.Children.Add(Ui.SubTitle("Petit signal (.ac, décade)")); s.Children.Add(ac);
        var show = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        show.Children.Add(new TextBlock { Text = "Tracer :", VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted });
        show.Children.Add(_showTran); show.Children.Add(_showAc);
        s.Children.Add(show);
        return s;
    }

    void RefreshProperties()
    {
        _props.Children.Clear();
        var c = _canvas.Selected;
        if (c == null)
        {
            _props.Children.Add(new TextBlock { Text = "Sélectionnez un composant sur le schéma pour modifier sa référence et sa valeur.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, FontSize = 12 });
            return;
        }
        var kindName = c.Kind switch
        {
            ComponentKind.Resistor => "Résistance", ComponentKind.Capacitor => "Condensateur", ComponentKind.Inductor => "Inductance",
            ComponentKind.VoltageSource => "Source de tension", ComponentKind.CurrentSource => "Source de courant",
            ComponentKind.Diode => "Diode", ComponentKind.Ground => "Masse", _ => "Étiquette de nœud"
        };
        _props.Children.Add(new TextBlock { Text = kindName, FontWeight = FontWeight.SemiBold });
        if (c.Kind == ComponentKind.Ground) { _props.Children.Add(Ui.Hint("Nœud 0 de référence. Toutes les masses sont reliées.")); return; }

        var refBox = new TextBox { Text = c.Ref };
        Control valueEditor;
        Func<string> value;
        if (c.Kind == ComponentKind.Diode)
        {
            var cb = new ComboBox { ItemsSource = DiodeModels.All.Keys.ToList(), SelectedItem = DiodeModels.All.Keys.FirstOrDefault(k => k.Equals(c.Value, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch };
            cb.SelectionChanged += (_, _) => _canvas.EditSelected(refBox.Text ?? c.Ref, cb.SelectedItem as string ?? c.Value);
            valueEditor = cb; value = () => cb.SelectedItem as string ?? c.Value;
        }
        else
        {
            var vb = new TextBox { Text = c.Value };
            vb.KeyDown += (_, e) => { if (e.Key == Key.Enter) _canvas.EditSelected(refBox.Text ?? c.Ref, vb.Text ?? ""); };
            vb.LostFocus += (_, _) => _canvas.EditSelected(refBox.Text ?? c.Ref, vb.Text ?? "");
            valueEditor = vb; value = () => vb.Text ?? "";
        }
        refBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) _canvas.EditSelected(refBox.Text ?? c.Ref, value()); };
        refBox.LostFocus += (_, _) => _canvas.EditSelected(refBox.Text ?? c.Ref, value());

        if (c.Kind != ComponentKind.Label) { _props.Children.Add(Ui.Label("Référence")); _props.Children.Add(refBox); }
        _props.Children.Add(Ui.Label(c.Kind switch { ComponentKind.Label => "Nom du nœud", ComponentKind.Diode => "Modèle", _ => "Valeur" }));
        _props.Children.Add(valueEditor);
        _props.Children.Add(Ui.Hint(c.Kind switch
        {
            ComponentKind.Resistor => "Ex. 4,7k · 10meg · 220",
            ComponentKind.Capacitor => "Ex. 100n · 4,7u · 22p",
            ComponentKind.Inductor => "Ex. 10u · 1m",
            ComponentKind.VoltageSource => "Syntaxe SPICE : DC 5 · AC 1 · PULSE(v1 v2 td tr tf pw per) · SIN(vo va f). « AC 1 » sert à l'analyse AC.",
            ComponentKind.CurrentSource => "Courant de la broche 1 vers la broche 2 dans la source. Ex. DC 1m · AC 1",
            ComponentKind.Diode => "Modèles génériques intégrés (indicatifs), en attendant les bibliothèques fabricant.",
            _ => "Le nœud prend ce nom dans la netlist, les courbes et les gabarits. Deux étiquettes identiques relient leurs nœuds."
        }));
    }

    // ───────────────────────── gabarits ─────────────────────────
    void RefreshGabarits()
    {
        _gabarits.Children.Clear();
        if (_etude.Gabarits.Count == 0)
            _gabarits.Children.Add(Ui.Hint("Aucun critère. Ajoutez par exemple « Bande passante −3 dB ≥ 1 kHz sur OUT »."));
        var nodes = Connectivity.Build(_etude.Schema).AllNodes.ToList();
        foreach (var g in _etude.Gabarits.ToList())
        {
            var res = _last?.Criteres.FirstOrDefault(r => r.Critere == g)?.Resultat;
            var card = new StackPanel { Spacing = 6 };
            var head = new Grid { ColumnDefinitions = new("*,Auto") };
            var mesure = new ComboBox { ItemsSource = TypesMesure.Tous.Select(i => i.Nom).ToList(), SelectedIndex = TypesMesure.Tous.ToList().FindIndex(i => i.Type == g.Mesure), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
            mesure.SelectionChanged += (_, _) => { if (mesure.SelectedIndex >= 0 && TypesMesure.Tous[mesure.SelectedIndex].Type != g.Mesure) { g.Mesure = TypesMesure.Tous[mesure.SelectedIndex].Type; Invalidate(); } };
            var del = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = Ui.Muted, Padding = new Thickness(6, 2), Margin = new Thickness(4, 0, 0, 0) };
            ToolTip.SetTip(del, "Retirer ce critère");
            del.Click += (_, _) => { _etude.Gabarits.Remove(g); Invalidate(); };
            head.Children.Add(mesure); head.Children.Add(del); Grid.SetColumn(del, 1);
            card.Children.Add(head);

            var row = new Grid { ColumnDefinitions = new("*,6,*,6,*") };
            var nodeItems = nodes.Contains(g.Noeud, StringComparer.OrdinalIgnoreCase) ? nodes : nodes.Append(g.Noeud).ToList();
            var node = new ComboBox { ItemsSource = nodeItems, SelectedItem = nodeItems.First(n => n.Equals(g.Noeud, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
            node.SelectionChanged += (_, _) => { if (node.SelectedItem is string n && n != g.Noeud) { g.Noeud = n; Invalidate(); } };
            var unit = g.Info.Unite;
            row.Children.Add(Ui.Labeled("Nœud", node));
            var min = Ui.Field("Min", g.Min is { } a ? SpiceNumber.Format(a, unit) : "", v => { g.Min = SpiceNumber.ParseOrNull(v); }, "—");
            Grid.SetColumn(min, 2); row.Children.Add(min);
            var max = Ui.Field("Max", g.Max is { } b ? SpiceNumber.Format(b, unit) : "", v => { g.Max = SpiceNumber.ParseOrNull(v); }, "—");
            Grid.SetColumn(max, 4); row.Children.Add(max);
            card.Children.Add(row);

            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(Ui.Badge(res?.Verdict));
            var analyse = g.Info.Analyse == AnalyseKind.Ac ? "AC" : "transitoire";
            line.Children.Add(new TextBlock
            {
                Text = res == null ? $"Analyse {analyse} — pas encore simulé"
                    : res.Valeur is { } v ? $"Mesuré : {SpiceNumber.Format(v, unit)}" : (res.Detail ?? "Non évaluable"),
                VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 230,
                Foreground = res?.Verdict == Verdict.Fail ? Ui.FailBrush : Ui.Ink
            });
            card.Children.Add(line);
            _gabarits.Children.Add(new Border { Child = card, Background = Brushes.White, BorderBrush = res?.Verdict == Verdict.Fail ? Ui.FailBrush : Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10) });
        }
    }

    /// <summary>Un critère a changé : les résultats affichés ne correspondent plus.</summary>
    void Invalidate() { _last = _last is null ? null : _last with { Criteres = [] }; SetVerdict(null); RefreshGabarits(); }

    // ───────────────────────── schéma → netlist / ERC ─────────────────────────
    void OnSchemaChanged()
    {
        var conn = Connectivity.Build(_etude.Schema);
        var issues = Erc.Check(_etude.Schema, conn);
        _netlist.Text = CircuitBuilder.Preview(_etude.Schema, conn, CircuitBuilder.AnalysisFor(_etude, Shown));
        _ercList.Children.Clear();
        if (issues.Count == 0) _ercList.Children.Add(new TextBlock { Text = "✓ Schéma cohérent : masse présente, pas de broche flottante ni de référence dupliquée.", Foreground = Ui.PassBrush });
        foreach (var i in issues)
            _ercList.Children.Add(new TextBlock { Text = (i.Severity == Severity.Error ? "⛔ " : "⚠ ") + i.Message, Foreground = i.Severity == Severity.Error ? Ui.FailBrush : Ui.WarnBrush, TextWrapping = TextWrapping.Wrap });
        int nErr = issues.Count(i => i.Severity == Severity.Error), nWarn = issues.Count - nErr;
        _ercTab.Header = nErr + nWarn == 0 ? "Vérifications ✓" : $"Vérifications ({nErr + nWarn})";
        if (_last != null) { _last = null; SetVerdict(null); RefreshGabarits(); }
        RefreshProperties();
    }

    // ───────────────────────── simulation ─────────────────────────
    public async Task SimulateAsync()
    {
        _cts?.Cancel(); _cts = new();
        _simulate.IsEnabled = false; _status.Text = "Simulation ngspice en cours…";
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = await Verification.RunAsync(_etude, [Shown], _cts.Token);
            _last = r;
            _journal.Text = r.Journal;
            if (r.Erreur != null)
            {
                _status.Text = "⛔ " + r.Erreur;
                _bottomTabs.SelectedIndex = r.Erc.Any(i => i.Severity == Severity.Error) ? 1 : 2;
                SetVerdict(null);
            }
            else
            {
                int pass = r.Criteres.Count(c => c.Resultat.Verdict == Verdict.Pass);
                _status.Text = $"Simulé en {sw.ElapsedMilliseconds} ms ({string.Join(" + ", r.Resultats.Keys.Select(k => k == AnalyseKind.Ac ? "AC" : "transitoire"))}) · {pass}/{r.Criteres.Count} critère(s) respecté(s)";
                SetVerdict(r.Criteres.Count == 0 ? null : r.Global);
                _bottomTabs.SelectedIndex = 0;
            }
            RefreshGabarits(); RefreshPlot();
        }
        catch (OperationCanceledException) { }
        finally { _simulate.IsEnabled = true; }
    }

    void SetVerdict(Verdict? v)
    {
        _verdict.IsVisible = v != null;
        if (v == null) return;
        _verdict.Background = v == Verdict.Pass ? Ui.PassBrush : v == Verdict.Fail ? Ui.FailBrush : Ui.WarnBrush;
        _verdict.Child = new TextBlock { Text = v == Verdict.Pass ? "GABARITS OK" : v == Verdict.Fail ? "GABARITS NON RESPECTÉS" : "NON ÉVALUABLE", Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 12 };
    }

    void RefreshPlot()
    {
        var p = _plot.Plot; p.Clear(); p.Axes.Right.IsVisible = false;
        p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        if (_last == null || !_last.Resultats.TryGetValue(Shown, out var raw))
        {
            p.Title(""); p.Axes.Bottom.Label.Text = ""; p.Axes.Left.Label.Text = "";
            var t = p.Add.Text(_last == null ? "Lancez « Simuler et vérifier » pour tracer les courbes" : "Cette analyse n'a pas été simulée : relancez la simulation", 0, 0);
            t.LabelFontSize = 14; t.LabelFontColor = ScottPlot.Color.FromHex("#5b6170"); t.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
            p.Axes.SetLimits(-1, 1, -1, 1); p.HideGrid(); _plot.Refresh(); return;
        }
        p.ShowGrid();
        var conn = Connectivity.Build(_etude.Schema);
        var nodes = (conn.NamedNodes.Count > 0 ? conn.NamedNodes : conn.AllNodes).Take(8).ToList();
        var palette = new ScottPlot.Palettes.Category10();
        int idx = 0;
        if (Shown == AnalyseKind.Transitoire)
        {
            foreach (var n in nodes)
            {
                var c = Verification.Temporelle(raw, $"v({n})");
                var sc = p.Add.ScatterLine(c.Temps.ToArray().Select(x => x * 1e3).ToArray(), c.Valeurs.ToArray());
                sc.LegendText = $"V({n})"; sc.LineWidth = 2; sc.Color = palette.GetColor(idx++);
            }
            p.Axes.Bottom.Label.Text = "Temps (ms)"; p.Axes.Left.Label.Text = "Tension (V)";
        }
        else
        {
            p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
            {
                MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(), IntegerTicksOnly = true,
                LabelFormatter = x => SpiceNumber.Format(Math.Pow(10, x), "Hz")
            };
            foreach (var n in nodes)
            {
                var r = Verification.Frequentielle(raw, $"v({n})");
                var lf = r.Frequences.ToArray().Select(Math.Log10).ToArray();
                var sc = p.Add.ScatterLine(lf, r.Valeurs.ToArray().Select(Silx.Mesures.Mesures.Frequentiel.Db).ToArray());
                sc.LegendText = $"|V({n})| (dB)"; sc.LineWidth = 2; sc.Color = palette.GetColor(idx);
                var ph = p.Add.ScatterLine(lf, Unwrap(r.Valeurs.ToArray().Select(z => z.Phase * 180 / Math.PI).ToArray()));
                ph.LegendText = $"phase V({n}) (°)"; ph.LineWidth = 1.5f; ph.Color = palette.GetColor(idx++).WithAlpha(0.6); ph.LinePattern = ScottPlot.LinePattern.Dashed;
                ph.Axes.YAxis = p.Axes.Right;
            }
            p.Axes.Right.IsVisible = true; p.Axes.Right.Label.Text = "Phase (°)";
            p.Axes.Bottom.Label.Text = "Fréquence"; p.Axes.Left.Label.Text = "Gain (dB)";
            foreach (var rc in _last.Criteres.Where(c => c.Critere.Mesure == TypeMesure.BandePassante3dB && c.Resultat.Valeur is not null))
            {
                var vl = p.Add.VerticalLine(Math.Log10(rc.Resultat.Valeur!.Value));
                vl.Color = rc.Resultat.Verdict == Verdict.Pass ? ScottPlot.Color.FromHex("#15803d") : ScottPlot.Color.FromHex("#b91c1c");
                vl.LinePattern = ScottPlot.LinePattern.Dotted; vl.LineWidth = 1.5f;
                vl.LegendText = $"fc −3 dB = {SpiceNumber.Format(rc.Resultat.Valeur!.Value, "Hz")}";
            }
        }
        p.Title(""); p.ShowLegend(Shown == AnalyseKind.Ac ? ScottPlot.Alignment.LowerLeft : ScottPlot.Alignment.LowerRight);
        p.Axes.AutoScale(); _plot.Refresh();
    }

    static double[] Unwrap(double[] deg)
    {
        var r = (double[])deg.Clone();
        for (int i = 1; i < r.Length; i++) { while (r[i] - r[i - 1] > 180) r[i] -= 360; while (r[i] - r[i - 1] < -180) r[i] += 360; }
        return r;
    }

    static TextBox Mono() => new() { IsReadOnly = true, FontFamily = new FontFamily("Menlo, Consolas, monospace"), AcceptsReturn = true, FontSize = 12, BorderThickness = new Thickness(0), Padding = new Thickness(10), Background = Brushes.White };
}
