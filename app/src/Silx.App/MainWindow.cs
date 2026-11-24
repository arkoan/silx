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
    readonly StackPanel _traces = new() { Spacing = 8 };
    readonly StackPanel _palette = new() { Spacing = 2 };
    readonly TabControl _bottomTabs = new();
    readonly TabItem _ercTab = new();
    readonly Button _simulate;
    readonly RadioButton _showTran = new() { Content = "Transitoire", GroupName = "aff", IsChecked = true };
    readonly RadioButton _showAc = new() { Content = "AC (Bode)", GroupName = "aff" };
    readonly Dictionary<ComponentKind, Button> _paletteButtons = new();
    Button _toolSelect = null!, _toolWire = null!;
    // Courbes : mode Simulation (courbes seules) / Test (gabarits visibles et dessinables).
    bool _modeTest = true;
    readonly Button _modeSim = new(), _modeTestBtn = new();
    readonly StackPanel _testTools = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    readonly ComboBox _signal = new() { MinWidth = 110, FontSize = 12 };
    readonly Button _drawHigh = new(), _drawLow = new();
    readonly TextBlock _drawHint = new() { FontSize = 12, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
    GabaritTrace? _drawing;
    (double X, double Y)? _hover;
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
            if (_drawing != null && e.Key is Key.Enter or Key.Escape) { e.Handled = true; EndDrawing(e.Key == Key.Enter); return; }
            if (e.Key == Key.F5 || (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Meta))) { e.Handled = true; await SimulateAsync(); }
        };
        _showTran.IsCheckedChanged += (_, _) => { if (_showTran.IsChecked == true) RefreshPlot(); };
        _showAc.IsCheckedChanged += (_, _) => { if (_showAc.IsChecked == true) RefreshPlot(); };

        LoadEtude(EtudeModel.Demo());
        _status.Text = "Clic : placer / sélectionner · glisser : déplacer · clic droit ou molette : vue · ⌘R : pivoter · W : fil · Suppr · ⌘Z / ⇧⌘Z";
    }

    public EtudeModel EtudeCourante => _etude;
    public AvaPlot PlotControl => _plot;
    public void SetModeTest(bool test) { _modeTest = test; RefreshPlotToolbar(); RefreshPlot(); }
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
        _bottomTabs.Items.Add(new TabItem { FontSize = 15, Header = "Courbes", Content = BuildPlotArea() });
        _bottomTabs.Items.Add(_ercTab); _ercTab.Content = new ScrollViewer { Content = _ercList };
        _bottomTabs.Items.Add(new TabItem { FontSize = 15, Header = "Journal ngspice", Content = _journal });

        var g = new Grid { RowDefinitions = new("3*,6,2*") };
        g.Children.Add(tabs);
        var split = new GridSplitter { Background = Ui.Line, ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(split, 1); g.Children.Add(split);
        var bottom = new Border { Background = Ui.Panel, Child = _bottomTabs }; Grid.SetRow(bottom, 2); g.Children.Add(bottom);
        return g;
    }

    // ───────────────────────── zone des courbes ─────────────────────────
    Control BuildPlotArea()
    {
        void Seg(Button b, string text, string tip, bool test)
        {
            b.Content = text; b.Padding = new Thickness(12, 4); b.FontSize = 12; ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { _modeTest = test; if (!test) EndDrawing(false); RefreshPlotToolbar(); RefreshPlot(); };
        }
        Seg(_modeSim, "Simulation", "Courbes seules", false);
        Seg(_modeTestBtn, "Test", "Courbes avec gabarits : dessiner et vérifier des limites", true);
        var seg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        seg.Children.Add(_modeSim); seg.Children.Add(_modeTestBtn);

        var view = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        _showTran.Content = "Temporel"; _showAc.Content = "Bode"; _showTran.FontSize = _showAc.FontSize = 12;
        view.Children.Add(_showTran); view.Children.Add(_showAc);

        _drawHigh.Content = "▔ Limite haute"; _drawLow.Content = "▁ Limite basse";
        foreach (var (b, lim) in new[] { (_drawHigh, Limite.Haute), (_drawLow, Limite.Basse) })
        {
            b.FontSize = 12; b.Padding = new Thickness(10, 4); b.Background = Brushes.Transparent;
            ToolTip.SetTip(b, lim == Limite.Haute ? "Dessiner une limite que le signal ne doit pas dépasser (zone interdite au-dessus)" : "Dessiner une limite sous laquelle le signal ne doit pas descendre (zone interdite en dessous)");
            var l = lim; b.Click += (_, _) => StartDrawing(l);
        }
        _signal.SelectionChanged += (_, _) => { if (_drawing == null) RefreshPlot(keepLimits: true); };
        _testTools.Children.Add(new TextBlock { Text = "Signal", FontSize = 12, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center });
        _testTools.Children.Add(_signal); _testTools.Children.Add(_drawHigh); _testTools.Children.Add(_drawLow); _testTools.Children.Add(_drawHint);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(8, 4) };
        bar.Children.Add(seg); bar.Children.Add(Ui.Separator()); bar.Children.Add(view); bar.Children.Add(Ui.Separator()); bar.Children.Add(_testTools);

        _plot.AddHandler(PointerPressedEvent, OnPlotPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _plot.AddHandler(PointerMovedEvent, OnPlotMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar); dock.Children.Add(_plot);
        RefreshPlotToolbar();
        return dock;
    }

    void RefreshPlotToolbar()
    {
        Ui.SetActive(_modeSim, !_modeTest); Ui.SetActive(_modeTestBtn, _modeTest);
        _testTools.IsVisible = _modeTest;
        Ui.SetActive(_drawHigh, _drawing?.Limite == Limite.Haute); Ui.SetActive(_drawLow, _drawing?.Limite == Limite.Basse);
        _drawHint.Text = _drawing == null ? "" : _drawing.Points.Count == 0
            ? "Cliquez pour poser le premier point (de gauche à droite)…"
            : "Cliquez pour ajouter un point · double-clic ou Entrée pour terminer · Échap pour annuler";
        var conn = Connectivity.Build(_etude.Schema);
        var nodes = conn.AllNodes.ToList();
        var cur = _signal.SelectedItem as string;
        if (!(_signal.ItemsSource is List<string> old && old.SequenceEqual(nodes)))
        {
            _signal.ItemsSource = nodes;
            _signal.SelectedItem = nodes.Contains(cur ?? "") ? cur : nodes.FirstOrDefault(n => n == "OUT") ?? conn.NamedNodes.LastOrDefault() ?? nodes.FirstOrDefault();
        }
    }

    string? SelectedSignal => _signal.SelectedItem as string;

    public void StartDrawing(Limite lim)
    {
        if (SelectedSignal is not { } n) return;
        if (_last == null || !_last.Resultats.ContainsKey(Shown)) { _status.Text = "Simulez d'abord : le gabarit se dessine sur la courbe."; return; }
        _drawing = new GabaritTrace { Analyse = Shown, Noeud = n, Limite = lim };
        _plot.UserInputProcessor.Disable();
        RefreshPlotToolbar(); RefreshPlot(keepLimits: true);
    }

    public void EndDrawing(bool keep)
    {
        if (_drawing == null) return;
        if (keep && _drawing.Points.Count >= 2)
        {
            _etude.GabaritsTraces.Add(_drawing);
            ReevaluateTraces();
        }
        _drawing = null; _hover = null;
        _plot.UserInputProcessor.Enable();
        RefreshPlotToolbar(); RefreshGabarits(); RefreshPlot(keepLimits: true);
    }

    /// <summary>Réévalue les gabarits dessinés sur les résultats déjà calculés (pas besoin de resimuler).</summary>
    void ReevaluateTraces()
    {
        if (_last == null || _last.Erreur != null) return;
        var traces = _etude.GabaritsTraces.Where(g => _last.Resultats.ContainsKey(g.Analyse))
            .Select(g => new ResultatTrace(g, Verification.EvaluerTrace(g, _last.Resultats[g.Analyse]))).ToList();
        _last = _last with { Traces = traces };
        if (_etude.GabaritsTraces.Any(g => !_last.Resultats.ContainsKey(g.Analyse))) SetVerdict(null); else SetVerdict(_last.Nombre == 0 ? null : _last.Global);
    }

    (double X, double Y) ToData(PointerEventArgs e)
    {
        var pos = e.GetPosition(_plot); var sc = _plot.DisplayScale;
        var c = _plot.Plot.GetCoordinates(new ScottPlot.Pixel((float)(pos.X * sc), (float)(pos.Y * sc)));
        return Shown == AnalyseKind.Ac ? (Math.Pow(10, c.X), c.Y) : (c.X * 1e-3, c.Y);
    }

    void OnPlotPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_drawing == null) return;
        e.Handled = true;
        if (!e.GetCurrentPoint(_plot).Properties.IsLeftButtonPressed) { EndDrawing(false); return; }
        if (e.ClickCount > 1) { EndDrawing(true); return; }
        var p = ToData(e);
        if (_drawing.Points.Count > 0 && p.X <= _drawing.Points[^1].X) { _drawHint.Text = "Les points se posent de gauche à droite."; return; }
        _drawing.Points.Add(p);
        RefreshPlotToolbar(); RefreshPlot(keepLimits: true);
    }

    void OnPlotMoved(object? sender, PointerEventArgs e)
    {
        if (_drawing == null || _drawing.Points.Count == 0) return;
        _hover = ToData(e); RefreshPlot(keepLimits: true);
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
        gh.Children.Add(Ui.Title("Critères mesurés"));
        var add = Ui.ToolButton("+ Critère", "Ajouter un critère pass/fail", () =>
        {
            var n = Connectivity.Build(_etude.Schema).NamedNodes.FirstOrDefault() ?? "OUT";
            _etude.Gabarits.Add(new CritereGabarit { Mesure = TypeMesure.BandePassante3dB, Noeud = n }); RefreshGabarits();
        });
        Grid.SetColumn(add, 1); gh.Children.Add(add);
        s.Children.Add(gh); s.Children.Add(_gabarits);
        var th = Ui.Title("Gabarits dessinés"); th.Margin = new Thickness(0, 12, 0, 6);
        s.Children.Add(th); s.Children.Add(_traces);
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
        RefreshTraces();
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

    void RefreshTraces()
    {
        _traces.Children.Clear();
        if (_etude.GabaritsTraces.Count == 0)
            _traces.Children.Add(Ui.Hint("Aucun. En mode Test, sous les courbes : choisissez un signal puis « Limite haute » ou « Limite basse » et cliquez sur le graphique."));
        foreach (var g in _etude.GabaritsTraces.ToList())
        {
            var res = _last?.Traces.FirstOrDefault(t => t.Gabarit == g)?.Resultat;
            var card = new StackPanel { Spacing = 4 };
            var head = new Grid { ColumnDefinitions = new("*,Auto") };
            head.Children.Add(new TextBlock { Text = g.Nom, FontWeight = FontWeight.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var del = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = Ui.Muted, Padding = new Thickness(6, 2) };
            ToolTip.SetTip(del, "Retirer ce gabarit");
            del.Click += (_, _) => { _etude.GabaritsTraces.Remove(g); ReevaluateTraces(); RefreshGabarits(); RefreshPlot(keepLimits: true); };
            Grid.SetColumn(del, 1); head.Children.Add(del);
            card.Children.Add(head);
            card.Children.Add(Ui.Hint($"{(g.Analyse == AnalyseKind.Ac ? "Bode" : "Temporel")} · {g.Points.Count} points de {Fx(g.Analyse, g.Points[0].X)} à {Fx(g.Analyse, g.Points[^1].X)}"));
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(Ui.Badge(res?.Verdict));
            line.Children.Add(new TextBlock
            {
                Text = res == null ? "Pas encore simulé" : res.Marge is { } m
                    ? (m >= 0 ? $"Marge : {SpiceNumber.Format(m, g.Unite)} à {Fx(g.Analyse, res.XPire!.Value)}" : $"Franchi de {SpiceNumber.Format(-m, g.Unite)} à {Fx(g.Analyse, res.XPire!.Value)}")
                    : res.Detail ?? "Non évaluable",
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 230,
                Foreground = res?.Verdict == Verdict.Fail ? Ui.FailBrush : Ui.Ink
            });
            card.Children.Add(line);
            _traces.Children.Add(new Border { Child = card, Background = Brushes.White, BorderBrush = res?.Verdict == Verdict.Fail ? Ui.FailBrush : Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10) });
        }
    }

    static string Fx(AnalyseKind k, double x) => k == AnalyseKind.Ac ? SpiceNumber.Format(x, "Hz") : SpiceNumber.Format(x, "s");

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
        if (_last != null) { _last = null; SetVerdict(null); RefreshGabarits(); RefreshPlot(); }
        RefreshPlotToolbar();
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
                int pass = r.Respectes;
                _status.Text = $"Simulé en {sw.ElapsedMilliseconds} ms ({string.Join(" + ", r.Resultats.Keys.Select(k => k == AnalyseKind.Ac ? "AC" : "transitoire"))}) · {pass}/{r.Nombre} contrainte(s) respectée(s)";
                SetVerdict(r.Nombre == 0 ? null : r.Global);
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

    void RefreshPlot(bool keepLimits = false)
    {
        var p = _plot.Plot;
        var limits = p.Axes.GetLimits();
        p.Clear(); p.Axes.Right.IsVisible = false;
        p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        if (_last == null || !_last.Resultats.TryGetValue(Shown, out var raw))
        {
            p.Title(""); p.Axes.Bottom.Label.Text = ""; p.Axes.Left.Label.Text = "";
            var t = p.Add.Text(_last == null ? "Lancez « Simuler et vérifier » pour tracer les courbes" : "Cette analyse n'a pas été simulée : relancez la simulation", 0, 0);
            t.LabelFontSize = 14; t.LabelFontColor = ScottPlot.Color.FromHex("#5b6170"); t.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
            p.Axes.SetLimits(-1, 1, -1, 1); p.HideGrid(); _plot.Refresh(); return;
        }
        p.ShowGrid();
        bool ac = Shown == AnalyseKind.Ac;
        double X(double x) => ac ? Math.Log10(x) : x * 1e3;
        var conn = Connectivity.Build(_etude.Schema);
        var nodes = (conn.NamedNodes.Count > 0 ? conn.NamedNodes : conn.AllNodes).ToList();
        if (_modeTest && SelectedSignal is { } sel && !nodes.Contains(sel)) nodes.Add(sel);
        var palette = new ScottPlot.Palettes.Category10();
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity, xLo = double.PositiveInfinity, xHi = double.NegativeInfinity;
        int idx = 0;
        foreach (var n in nodes.Take(8))
        {
            var (xs, ys) = Verification.Courbe(Shown, raw, n);
            bool dim = _modeTest && SelectedSignal != null && n != SelectedSignal;
            var color = palette.GetColor(idx);
            var sc = p.Add.ScatterLine(xs.Select(X).ToArray(), ys);
            sc.LegendText = ac ? $"|V({n})| (dB)" : $"V({n})"; sc.LineWidth = dim ? 1.5f : 2.5f; sc.Color = dim ? color.WithAlpha(0.35) : color;
            yMin = Math.Min(yMin, ys.Min()); yMax = Math.Max(yMax, ys.Max()); xLo = Math.Min(xLo, X(xs[0])); xHi = Math.Max(xHi, X(xs[^1]));
            if (ac && !_modeTest)
            {
                var r = Verification.Frequentielle(raw, $"v({n})");
                var ph = p.Add.ScatterLine(xs.Select(X).ToArray(), Unwrap(r.Valeurs.ToArray().Select(z => z.Phase * 180 / Math.PI).ToArray()));
                ph.LegendText = $"phase V({n}) (°)"; ph.LineWidth = 1.5f; ph.Color = color.WithAlpha(0.6); ph.LinePattern = ScottPlot.LinePattern.Dashed;
                ph.Axes.YAxis = p.Axes.Right; p.Axes.Right.IsVisible = true; p.Axes.Right.Label.Text = "Phase (°)";
            }
            idx++;
        }
        if (ac)
        {
            p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
            {
                MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(), IntegerTicksOnly = true,
                LabelFormatter = x => SpiceNumber.Format(Math.Pow(10, x), "Hz")
            };
            p.Axes.Bottom.Label.Text = "Fréquence"; p.Axes.Left.Label.Text = "Gain (dB)";
        }
        else { p.Axes.Bottom.Label.Text = "Temps (ms)"; p.Axes.Left.Label.Text = "Tension (V)"; }

        if (_modeTest)
        {
            double span = Math.Max(yMax - yMin, 1e-9), far = 1000 * span;
            var red = ScottPlot.Color.FromHex("#b91c1c");
            void DrawMask(GabaritTrace g, IReadOnlyList<(double X, double Y)> pts, ResultatGabaritGraphique? res, bool draft)
            {
                if (pts.Count == 0) return;
                var c = pts.Select(q => new ScottPlot.Coordinates(X(q.X), q.Y)).ToList();
                if (c.Count >= 2)
                {
                    double edge = g.Limite == Limite.Haute ? yMax + far : yMin - far;
                    var poly = p.Add.Polygon(c.Append(new(c[^1].X, edge)).Append(new(c[0].X, edge)).ToArray());
                    poly.FillStyle.Color = red.WithAlpha(0.10); poly.FillStyle.Hatch = new ScottPlot.Hatches.Striped(); poly.FillStyle.HatchColor = red.WithAlpha(draft ? 0.4 : 0.7);
                    poly.LineStyle.Width = 0;
                }
                var line = p.Add.ScatterLine(c.Select(q => q.X).ToArray(), c.Select(q => q.Y).ToArray());
                line.Color = red; line.LineWidth = 2.5f; line.MarkerSize = 7; line.MarkerShape = ScottPlot.MarkerShape.FilledSquare; line.MarkerColor = red;
                if (draft) line.LinePattern = ScottPlot.LinePattern.Dashed;
                if (res?.XPire is { } xp && res.YPire is { } yp && res.Marge is { } m)
                {
                    var ok = res.Verdict == Verdict.Pass;
                    var col = ok ? ScottPlot.Color.FromHex("#15803d") : red;
                    var mk = p.Add.Marker(X(xp), yp); mk.Color = col; mk.Size = 10; mk.Shape = ScottPlot.MarkerShape.OpenCircle; mk.LineWidth = 2;
                    var lim = (double)(g.Limite == Limite.Haute ? yp + m : yp - m);
                    var conn2 = p.Add.Line(X(xp), yp, X(xp), lim); conn2.Color = col; conn2.LineWidth = 1.5f; conn2.LinePattern = ScottPlot.LinePattern.Dotted;
                    var txt = p.Add.Text(ok ? $"marge {SpiceNumber.Format(m, g.Unite)}" : $"franchi de {SpiceNumber.Format(-m, g.Unite)}", X(xp), (yp + lim) / 2);
                    txt.LabelFontColor = col; txt.LabelFontSize = 12; txt.LabelBold = true;
                    bool droite = X(xp) > (xLo + xHi) / 2;
                    txt.LabelOffsetX = droite ? -10 : 10;
                    txt.LabelAlignment = droite ? ScottPlot.Alignment.MiddleRight : ScottPlot.Alignment.MiddleLeft; txt.LabelBackgroundColor = ScottPlot.Colors.White.WithAlpha(0.85);
                }
            }
            foreach (var g in _etude.GabaritsTraces.Where(g => g.Analyse == Shown))
                DrawMask(g, g.Points, _last.Traces.FirstOrDefault(t => t.Gabarit == g)?.Resultat, false);
            if (_drawing != null)
                DrawMask(_drawing, _hover is { } h && (_drawing.Points.Count == 0 || h.X > _drawing.Points[^1].X) ? _drawing.Points.Append(h).ToList() : _drawing.Points, null, true);
            foreach (var g in _etude.GabaritsTraces.Where(g => g.Analyse == Shown)) foreach (var q in g.Points) { yMin = Math.Min(yMin, q.Y); yMax = Math.Max(yMax, q.Y); }

            foreach (var rc in _last.Criteres.Where(c => ac && c.Critere.Mesure == TypeMesure.BandePassante3dB && c.Resultat.Valeur is not null))
            {
                var vl = p.Add.VerticalLine(Math.Log10(rc.Resultat.Valeur!.Value));
                vl.Color = rc.Resultat.Verdict == Verdict.Pass ? ScottPlot.Color.FromHex("#15803d") : red;
                vl.LinePattern = ScottPlot.LinePattern.Dotted; vl.LineWidth = 1.5f;
                vl.LegendText = $"fc −3 dB = {SpiceNumber.Format(rc.Resultat.Valeur!.Value, "Hz")}";
            }
        }
        p.Title(""); p.ShowLegend(ac ? ScottPlot.Alignment.LowerLeft : ScottPlot.Alignment.LowerRight);
        if (keepLimits && limits.Rect.Width > 0 && limits.Left != -1) p.Axes.SetLimits(limits);
        else
        {
            var all = _last.Resultats[Shown];
            var (x0, _) = Verification.Courbe(Shown, all, nodes.First());
            double pad = Math.Max((yMax - yMin) * (_modeTest ? 0.18 : 0.06), 1e-6);
            p.Axes.SetLimits(X(x0[0]), X(x0[^1]), yMin - pad, yMax + pad);
            if (ac && !_modeTest) p.Axes.AutoScaleY(p.Axes.Right);
        }
        _plot.Refresh();
    }

    static double[] Unwrap(double[] deg)
    {
        var r = (double[])deg.Clone();
        for (int i = 1; i < r.Length; i++) { while (r[i] - r[i - 1] > 180) r[i] -= 360; while (r[i] - r[i - 1] < -180) r[i] += 360; }
        return r;
    }

    static TextBox Mono() => new() { IsReadOnly = true, FontFamily = new FontFamily("Menlo, Consolas, monospace"), AcceptsReturn = true, FontSize = 12, BorderThickness = new Thickness(0), Padding = new Thickness(10), Background = Brushes.White };
}
