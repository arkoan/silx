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
    readonly Dictionary<ComponentKind, Button> _paletteButtons = new();
    Button _toolSelect = null!, _toolWire = null!;
    // Courbes : mode Simulation (courbes seules) / Test (gabarits visibles et dessinables).
    bool _modeTest = true;
    readonly Button _modeSim = new(), _modeTestBtn = new();
    readonly StackPanel _testTools = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    readonly ComboBox _signal = new() { MinWidth = 110, FontSize = 12 };
    readonly Button _drawHigh = new(), _drawLow = new();
    Button _viewTran = null!, _viewAc = null!;
    // Édition des points d'un gabarit : survol (grossit) puis glisser.
    (GabaritTrace G, int I)? _hoverVertex, _dragVertex;
    readonly TextBlock _drawHint = new() { FontSize = 12, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
    GabaritTrace? _drawing;
    (double X, double Y)? _hover;
    CancellationTokenSource? _cts;
    // Bancs de test : null = circuit sous test seul.
    Banc? _banc;
    readonly Dictionary<Banc, ResultatEtude> _results = new();
    readonly StackPanel _benchBar = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 6) };
    readonly StackPanel _benchPanel = new() { Spacing = 6 };
    readonly StackPanel _circuitPanel = new() { Spacing = 8 };
    readonly StackPanel _analysisHost = new();
    readonly List<Button> _sourceButtons = new();
    Border _left = null!, _right = null!;
    readonly Button _undoDelete = new() { Content = "Rétablir le banc", FontSize = 12, Padding = new Thickness(8, 1), IsVisible = false, Background = Brushes.Transparent, Foreground = Ui.AccentBrush };

    Banc Banc => _banc ?? throw new InvalidOperationException("Aucun banc actif.");
    /// <summary>Schéma vu par les analyses et les listes de nœuds : circuit + calque du banc actif.</summary>
    Schematic SchemaCourant => _banc?.Fusion(_etude.Schema) ?? _etude.Schema;
    /// <summary>Résultats du banc actif.</summary>
    ResultatEtude? _last
    {
        get => _banc != null && _results.TryGetValue(_banc, out var r) ? r : null;
        set { if (_banc == null) return; if (value == null) _results.Remove(_banc); else _results[_banc] = value; }
    }

    /// <summary>Vue des courbes : imposée par le type du banc actif.</summary>
    AnalyseKind Shown => _banc?.Type ?? AnalyseKind.Transitoire;

    public MainWindow()
    {
        Title = "Silx — étude sans titre"; Width = 1440; Height = 900; MinWidth = 1000; MinHeight = 640;
        Background = Ui.Window;

        _simulate = new Button
        {
            Content = SimContent(), Background = Ui.AccentBrush, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(12, 5), VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(6)
        };
        ToolTip.SetTip(_simulate, "Lance les analyses nécessaires et évalue les gabarits (F5 ou ⌘↵)");
        _simulate.Click += async (_, _) => await SimulateAsync();

        var root = new DockPanel();
        var top = BuildToolbar(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        _undoDelete.Click += (_, _) =>
        {
            if (_deleted is { } d) { _etude.Bancs.Insert(Math.Min(d.Index, _etude.Bancs.Count), d.B); _deleted = null; SelectBanc(d.B); _status.Text = $"Banc « {d.B.Nom} » rétabli."; }
            _undoDelete.IsVisible = false;
        };
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        statusRow.Children.Add(_status); statusRow.Children.Add(_undoDelete);
        var status = new Border { Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 2), Child = statusRow };
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

        LoadEtude(EtudeModel.Demo());
        _status.Text = "Clic : placer / sélectionner · glisser : déplacer · clic droit ou molette : vue · ⌘R : pivoter · W : fil · Suppr · ⌘Z / ⇧⌘Z";
    }

    static Control SimContent()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        sp.Children.Add(Icones.Make(Icones.Lecture, 13, Brushes.White, filled: true));
        sp.Children.Add(new TextBlock { Text = "Simuler", VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    public EtudeModel EtudeCourante => _etude;
    public AvaPlot PlotControl => _plot;
    public void SetModeTest(bool test) { _modeTest = test; RefreshPlotToolbar(); RefreshPlot(); }
    /// <summary>Change le type du banc actif (temporel / fréquentiel).</summary>
    public void ShowAnalysis(AnalyseKind k)
    {
        if (_banc == null || _banc.Type == k) return;
        EndDrawing(false); _banc.Type = k; _last = null; SetVerdict(null);
        RefreshSide(); RefreshBenchBar(); RefreshPlotToolbar(); RefreshPlot(); OnSchemaChanged();
    }

    void LoadEtude(EtudeModel e)
    {
        _etude = e; _results.Clear(); SelectBanc(e.Bancs.FirstOrDefault());
    }

    public void SelectBanc(Banc? b)
    {
        EndDrawing(false);
        _banc = b;
        _canvas.Caption = b == null
            ? "Circuit sous test — sans stimuli : les sources se branchent dans un banc de test"
            : $"Banc « {b.Nom} » — calque en violet ; le circuit dessous se modifie dans « Circuit sous test »";
        _canvas.Load(b?.Calque ?? _etude.Schema, b == null ? null : _etude.Schema);
        foreach (var sb in _sourceButtons) { sb.IsEnabled = b != null; sb.Opacity = b != null ? 1 : 0.35; }
        RefreshSide(); RefreshBenchBar(); RefreshPlotToolbar(); RefreshPlot();
        SetVerdict(b == null ? GlobalVerdict() : _last is { Erreur: null } r && r.Nombre > 0 ? r.Global : null);
    }

    public Banc? BancActif => _banc;
    public ResultatEtude? Resultats => _last;

    Verdict? GlobalVerdict()
    {
        if (_etude.Bancs.Count == 0 || _etude.Bancs.Any(b => !_results.ContainsKey(b))) return null;
        var v = _etude.Bancs.Select(b => _results[b]).Select(r => r.Erreur != null ? Verdict.NonEvaluable : r.Nombre == 0 ? Verdict.Pass : r.Global).ToList();
        return v.Contains(Verdict.Fail) ? Verdict.Fail : v.Contains(Verdict.NonEvaluable) ? Verdict.NonEvaluable : Verdict.Pass;
    }

    (Banc B, int Index)? _deleted;

    /// <summary>Supprime un banc (et ses résultats) ; « Rétablir » dans la barre d'état le remet.</summary>
    public void DeleteBanc(Banc b)
    {
        int i = _etude.Bancs.IndexOf(b); if (i < 0) return;
        _etude.Bancs.RemoveAt(i); _results.Remove(b); _deleted = (b, i);
        SelectBanc(_banc == b ? (_etude.Bancs.Count > 0 ? _etude.Bancs[Math.Min(i, _etude.Bancs.Count - 1)] : null) : _banc);
        _status.Text = $"Banc « {b.Nom} » supprimé.";
        _undoDelete.IsVisible = true;
    }

    void RefreshBenchBar()
    {
        _benchBar.Children.Clear();
        Button Tab(string text, bool active, Verdict? v, Action click, IBrush accent, Banc? removable = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (v != null) row.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Background = v == Verdict.Pass ? Ui.PassBrush : v == Verdict.Fail ? Ui.FailBrush : Ui.WarnBrush });
            row.Children.Add(new TextBlock { Text = text, FontSize = 12.5, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center });
            if (removable != null)
            {
                var x = new Button { Content = Icones.Make(Icones.Croix, 10, Ui.Muted), Padding = new Thickness(3), Background = Brushes.Transparent, CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 0, -6, 0), VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(x, $"Supprimer le banc « {removable.Nom} »");
                x.Click += (_, e) => { e.Handled = true; DeleteBanc(removable); };
                row.Children.Add(x);
            }
            var b = new Button { Content = row, Padding = new Thickness(12, 4), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
                BorderBrush = active ? accent : Ui.Line, Background = active ? (accent == Ui.AccentBrush ? Ui.ActiveBg : Ui.BenchBg) : Brushes.White, Foreground = active ? accent : Ui.Ink };
            b.Click += (_, _) => click();
            _benchBar.Children.Add(b);
            return b;
        }
        Tab("Circuit sous test", _banc == null, null, () => SelectBanc(null), Ui.AccentBrush);
        _benchBar.Children.Add(new Border { Width = 1, Height = 18, Background = Ui.Line, Margin = new Thickness(6, 0) });
        foreach (var b in _etude.Bancs)
        {
            Verdict? v = _results.TryGetValue(b, out var r) ? (r.Erreur != null ? Verdict.NonEvaluable : r.Nombre == 0 ? null : r.Global) : null;
            var bb = b; Tab(b.Nom, _banc == b, v, () => SelectBanc(bb), Ui.BenchBrush, bb);
        }
        var add = new Button { Content = Icones.Make(Icones.Plus, 14, Ui.BenchBrush), Padding = new Thickness(7), Background = Brushes.Transparent, CornerRadius = new CornerRadius(14) };
        ToolTip.SetTip(add, "Nouveau banc : un calque où brancher sources, alimentations et charges sur le circuit");
        add.Click += (_, _) =>
        {
            var b = new Banc { Nom = $"Banc {_etude.Bancs.Count + 1}" };
            _etude.Bancs.Add(b); SelectBanc(b);
        };
        _benchBar.Children.Add(add);
    }

    // ───────────────────────── barre d'outils ─────────────────────────
    Control BuildToolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 3) };
        var logo = new TextBlock { Text = "Silx", FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Ui.AccentBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
        bar.Children.Add(logo);
        bar.Children.Add(Icones.Button(Icones.Nouveau, "Nouvelle étude (schéma vide)", () => LoadEtude(new EtudeModel())));
        bar.Children.Add(Icones.Button(Icones.Exemple, "Ouvrir l'exemple : filtre RC et ses deux bancs de test", () => LoadEtude(EtudeModel.Demo())));
        bar.Children.Add(Ui.Separator());
        _toolSelect = Icones.Button(Icones.Selection, "Sélectionner et déplacer (Échap)", () => _canvas.Tool = Tool.Select);
        _toolWire = Icones.Button(Icones.Fil, "Tracer un fil (W) : double-clic ou arrivée sur une broche pour finir", () => _canvas.Tool = Tool.Wire);
        bar.Children.Add(_toolSelect); bar.Children.Add(_toolWire);
        bar.Children.Add(Icones.Button(Icones.Pivoter, "Pivoter la sélection ou le composant à placer (⌘R)", () => _canvas.RotateSelected()));
        bar.Children.Add(Icones.Button(Icones.Supprimer, "Supprimer la sélection (Suppr)", () => _canvas.DeleteSelected()));
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.Annuler, "Annuler (⌘Z)", () => _canvas.Undo()));
        bar.Children.Add(Icones.Button(Icones.Retablir, "Rétablir (⇧⌘Z)", () => _canvas.Redo()));
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.PanneauGauche, "Afficher / masquer la palette de composants", () => { _left.IsVisible = !_left.IsVisible; }));
        bar.Children.Add(Icones.Button(Icones.PanneauDroit, "Afficher / masquer le panneau de propriétés et de gabarits", () => { _right.IsVisible = !_right.IsVisible; }));

        var rightSide = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(8, 3), HorizontalAlignment = HorizontalAlignment.Right };
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
    /// <summary>Palette en tuiles de symboles (nom et raccourci en info-bulle), groupes séparés par un filet.</summary>
    Control BuildPalette()
    {
        var groups = new (ComponentKind K, string Nom, string Touche)[][]
        {
            [(ComponentKind.Resistor, "Résistance", "R"), (ComponentKind.Capacitor, "Condensateur", "C"), (ComponentKind.Inductor, "Inductance", "L"), (ComponentKind.Diode, "Diode", "D")],
            [(ComponentKind.VoltageSource, "Source de tension", "V"), (ComponentKind.CurrentSource, "Source de courant", "I")],
            [(ComponentKind.Ground, "Masse", "G"), (ComponentKind.Label, "Étiquette de nœud", "N")],
        };
        foreach (var items in groups)
        {
            if (_palette.Children.Count > 0) _palette.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(4, 6) });
            var wrap = new WrapPanel { ItemWidth = 46, ItemHeight = 40 };
            foreach (var (k, nom, touche) in items)
            {
                var ico = new Symboles.Icone { Kind = k, Width = 34, Height = 24, Brush = Ui.Ink };
                var b = new Button { Content = ico, Padding = new Thickness(5, 7), Margin = new Thickness(2), Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                bool source = k is ComponentKind.VoltageSource or ComponentKind.CurrentSource;
                ToolTip.SetTip(b, $"{nom} ({touche}) — Maj+clic pour en poser plusieurs" + (source ? "\nLes sources se placent dans un banc de test." : ""));
                ToolTip.SetShowOnDisabled(b, true);
                var kind = k; b.Click += (_, _) => _canvas.BeginPlace(kind);
                _paletteButtons[k] = b; wrap.Children.Add(b);
                if (source) _sourceButtons.Add(b);
            }
            _palette.Children.Add(wrap);
        }
        _palette.Margin = new Thickness(4, 8);
        return _left = new Border { Width = 102, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = _palette } };
    }

    // ───────────────────────── centre ─────────────────────────
    Control BuildCenter()
    {
        var tabs = new TabControl { Padding = new Thickness(0) };
        var schemaDock = new DockPanel();
        var barBorder = new Border { Child = new ScrollViewer { Content = _benchBar, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 1) };
        DockPanel.SetDock(barBorder, Dock.Top); schemaDock.Children.Add(barBorder); schemaDock.Children.Add(_canvas);
        tabs.Items.Add(new TabItem { FontSize = 13, MinHeight = 30, Header = "Schéma", Content = schemaDock });
        tabs.Items.Add(new TabItem { FontSize = 13, MinHeight = 30, Header = "Netlist", Content = _netlist });

        _plot.Plot.FigureBackground.Color = ScottPlot.Colors.White;
        _ercTab.Header = "Vérifications"; _ercTab.FontSize = 13; _ercTab.MinHeight = 30;
        _bottomTabs.Items.Add(new TabItem { FontSize = 13, MinHeight = 30, Header = "Courbes", Content = BuildPlotArea() });
        _bottomTabs.Items.Add(_ercTab); _ercTab.Content = new ScrollViewer { Content = _ercList };
        _bottomTabs.Items.Add(new TabItem { FontSize = 13, MinHeight = 30, Header = "Journal ngspice", Content = _journal });

        var g = new Grid { RowDefinitions = new("3*,5,2*") };
        g.Children.Add(tabs);
        var split = new GridSplitter { Background = Ui.Line, ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(split, 1); g.Children.Add(split);
        var bottom = new Border { Background = Ui.Panel, Child = _bottomTabs }; Grid.SetRow(bottom, 2); g.Children.Add(bottom);
        return g;
    }

    // ───────────────────────── zone des courbes ─────────────────────────
    Control BuildPlotArea()
    {
        void Seg(Button b, string icon, string tip, bool test)
        {
            b.Content = Icones.Make(icon, 17); b.Padding = new Thickness(7, 4); b.Background = Brushes.Transparent; b.CornerRadius = new CornerRadius(6); ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { _modeTest = test; if (!test) EndDrawing(false); RefreshPlotToolbar(); RefreshPlot(); };
        }
        Seg(_modeSim, Icones.ModeSimulation, "Mode simulation : courbes seules", false);
        Seg(_modeTestBtn, Icones.ModeTest, "Mode test : courbes avec gabarits (dessiner, déplacer les points, vérifier)", true);
        var seg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        seg.Children.Add(_modeSim); seg.Children.Add(_modeTestBtn);

        _viewTran = Icones.Button(Icones.Temporel, "Vue temporelle : suit le type du banc (réglable dans le panneau du banc)", () => { }, 17);
        _viewAc = Icones.Button(Icones.Bode, "Vue Bode : suit le type du banc (réglable dans le panneau du banc)", () => { }, 17);
        var view = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        view.Children.Add(_viewTran); view.Children.Add(_viewAc);

        foreach (var (b, lim) in new[] { (_drawHigh, Limite.Haute), (_drawLow, Limite.Basse) })
        {
            b.Content = Icones.Make(lim == Limite.Haute ? Icones.LimiteHaute : Icones.LimiteBasse, 17, Ui.FailBrush);
            b.Padding = new Thickness(7, 4); b.Background = Brushes.Transparent; b.CornerRadius = new CornerRadius(6);
            ToolTip.SetTip(b, lim == Limite.Haute ? "Dessiner une limite haute : zone interdite au-dessus" : "Dessiner une limite basse : zone interdite en dessous");
            var l = lim; b.Click += (_, _) => StartDrawing(l);
        }
        _signal.SelectionChanged += (_, _) => { if (_drawing == null) RefreshPlot(keepLimits: true); };
        ToolTip.SetTip(_signal, "Signal auquel s'applique le gabarit dessiné");
        _signal.MinHeight = 28; _signal.Padding = new Thickness(8, 2);
        _testTools.Children.Add(_signal); _testTools.Children.Add(_drawHigh); _testTools.Children.Add(_drawLow); _testTools.Children.Add(_drawHint);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(6, 2) };
        bar.Children.Add(seg); bar.Children.Add(Ui.Separator()); bar.Children.Add(view); bar.Children.Add(Ui.Separator()); bar.Children.Add(_testTools);

        _plot.AddHandler(PointerPressedEvent, OnPlotPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _plot.AddHandler(PointerMovedEvent, OnPlotMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _plot.AddHandler(PointerReleasedEvent, OnPlotReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar); dock.Children.Add(_plot);
        RefreshPlotToolbar();
        return dock;
    }

    void RefreshPlotToolbar()
    {
        Ui.SetActive(_modeSim, !_modeTest); Ui.SetActive(_modeTestBtn, _modeTest);
        if (_viewTran != null)
        {
            // La vue suit le type du banc : l'autre est grisée.
            Ui.SetActive(_viewTran, _banc != null && Shown == AnalyseKind.Transitoire); Ui.SetActive(_viewAc, _banc != null && Shown == AnalyseKind.Ac);
            _viewTran.IsEnabled = _banc?.Type == AnalyseKind.Transitoire; _viewAc.IsEnabled = _banc?.Type == AnalyseKind.Ac;
            _viewTran.Opacity = _viewTran.IsEnabled ? 1 : 0.3; _viewAc.Opacity = _viewAc.IsEnabled ? 1 : 0.3;
        }
        _testTools.IsVisible = _modeTest && _banc != null;
        Ui.SetActive(_drawHigh, _drawing?.Limite == Limite.Haute); Ui.SetActive(_drawLow, _drawing?.Limite == Limite.Basse);
        _drawHint.Text = _drawing == null ? "" : _drawing.Points.Count == 0
            ? "Cliquez pour poser le premier point (de gauche à droite)…"
            : "Cliquez pour ajouter un point · double-clic ou Entrée pour terminer · Échap pour annuler";
        var conn = Connectivity.Build(SchemaCourant);
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
        if (SelectedSignal is not { } n || _banc == null) return;
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
            Banc.GabaritsTraces.Add(_drawing);
            ReevaluateTraces();
        }
        _drawing = null; _hover = null;
        _plot.UserInputProcessor.Enable();
        RefreshPlotToolbar(); RefreshGabarits(); RefreshPlot(keepLimits: true);
    }

    /// <summary>Réévalue les gabarits dessinés sur les résultats déjà calculés (pas besoin de resimuler).</summary>
    void ReevaluateTraces()
    {
        if (_banc == null || _last == null || _last.Erreur != null) return;
        var traces = Banc.TracesActives.Where(g => _last.Resultats.ContainsKey(g.Analyse))
            .Select(g => new ResultatTrace(g, Verification.EvaluerTrace(g, _last.Resultats[g.Analyse]))).ToList();
        _last = _last with { Traces = traces };
        _status.Text = $"« {Banc.Nom} » : gabarit modifié · {_last.Respectes}/{_last.Nombre} contrainte(s) respectée(s)";
        if (Banc.TracesActives.Any(g => !_last.Resultats.ContainsKey(g.Analyse))) SetVerdict(null); else SetVerdict(_last.Nombre == 0 ? null : _last.Global);
    }

    (double X, double Y) ToData(PointerEventArgs e)
    {
        var pos = e.GetPosition(_plot); var sc = _plot.DisplayScale;
        var c = _plot.Plot.GetCoordinates(new ScottPlot.Pixel((float)(pos.X * sc), (float)(pos.Y * sc)));
        return Shown == AnalyseKind.Ac ? (Math.Pow(10, c.X), c.Y) : (c.X * 1e-3, c.Y);
    }

    void OnPlotPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_drawing == null)
        {
            if (_hoverVertex is { } hv && e.GetCurrentPoint(_plot).Properties.IsLeftButtonPressed)
            {
                e.Handled = true; _dragVertex = hv; _plot.UserInputProcessor.Disable();
                e.Pointer.Capture(_plot);
            }
            return;
        }
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
        if (_drawing != null)
        {
            if (_drawing.Points.Count == 0) return;
            _hover = ToData(e); RefreshPlot(keepLimits: true); return;
        }
        if (_dragVertex is { } dv)
        {
            // Déplacement d'un point : l'abscisse reste entre celles de ses voisins.
            var (x, y) = ToData(e); var pts = dv.G.Points;
            // Bornes finies seulement : le premier et le dernier point n'ont qu'un voisin.
            double? lo = dv.I > 0 ? pts[dv.I - 1].X : null, hi = dv.I < pts.Count - 1 ? pts[dv.I + 1].X : null;
            bool ac = dv.G.Analyse == AnalyseKind.Ac;
            if (lo is { } l) x = Math.Max(x, ac ? l * 1.001 : l + Math.Max(Math.Abs(l), 1e-9) * 1e-4);
            if (hi is { } h) x = Math.Min(x, ac ? h * 0.999 : h - Math.Max(Math.Abs(h), 1e-9) * 1e-4);
            if (ac) x = Math.Max(x, 1e-3);
            if (!double.IsFinite(x) || !double.IsFinite(y) || (lo is { } l2 && x <= l2) || (hi is { } h2 && x >= h2)) return;
            pts[dv.I] = (x, y);
            ReevaluateTraces(); RefreshPlot(keepLimits: true);
            e.Handled = true; return;
        }
        if (!_modeTest || _banc == null || _last == null) return;
        var hit = VertexAt(e.GetPosition(_plot));
        if (!Nullable.Equals(hit, _hoverVertex))
        {
            _hoverVertex = hit;
            _plot.Cursor = hit != null ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
            RefreshPlot(keepLimits: true);
        }
    }

    void OnPlotReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragVertex == null) return;
        _dragVertex = null; e.Pointer.Capture(null); e.Handled = true;
        _plot.UserInputProcessor.Enable();
        RefreshGabarits(); RefreshBenchBar();
    }

    /// <summary>Point de gabarit sous le pointeur (à 9 px près), dans la vue affichée.</summary>
    (GabaritTrace, int)? VertexAt(Point pos)
    {
        var sc = _plot.DisplayScale;
        foreach (var g in Banc.GabaritsTraces.Where(g => g.Analyse == Shown))
            for (int i = 0; i < g.Points.Count; i++)
            {
                var q = g.Points[i];
                var px = _plot.Plot.GetPixel(new ScottPlot.Coordinates(Shown == AnalyseKind.Ac ? Math.Log10(q.X) : q.X * 1e3, q.Y));
                if (Math.Abs(px.X / sc - pos.X) < 9 && Math.Abs(px.Y / sc - pos.Y) < 9) return (g, i);
            }
        return null;
    }

    // ───────────────────────── panneau droit ─────────────────────────
    Control BuildSidePanel()
    {
        var s = new StackPanel { Spacing = 6, Margin = new Thickness(12, 10) };
        s.Children.Add(Ui.Title("Propriétés")); s.Children.Add(_props);
        s.Children.Add(Ui.Rule());
        s.Children.Add(_circuitPanel); s.Children.Add(_benchPanel);
        return _right = new Border { Width = 310, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = s } };
    }

    /// <summary>Panneau droit selon le contexte : vue d'ensemble des bancs (circuit) ou réglages du banc actif.</summary>
    void RefreshSide()
    {
        _circuitPanel.IsVisible = _banc == null; _benchPanel.IsVisible = _banc != null;
        _circuitPanel.Children.Clear(); _benchPanel.Children.Clear();
        if (_banc == null)
        {
            _circuitPanel.Children.Add(Ui.Title("Bancs de test"));
            _circuitPanel.Children.Add(Ui.Hint("Chaque banc branche ses propres sources, alimentations et charges sur ce circuit, avec ses analyses et ses gabarits. Une modification du circuit vaut pour tous les bancs : « Simuler et vérifier » les relance tous."));
            if (_etude.Bancs.Count == 0) _circuitPanel.Children.Add(Ui.Hint("Aucun banc pour l'instant : « + Banc de test » au-dessus du schéma."));
            foreach (var b in _etude.Bancs)
            {
                var r = _results.GetValueOrDefault(b);
                var row = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
                row.Children.Add(Ui.Badge(r == null ? null : r.Erreur != null ? Verdict.NonEvaluable : r.Nombre == 0 ? null : r.Global));
                var name = new TextBlock { Text = b.Nom, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
                Grid.SetColumn(name, 1); row.Children.Add(name);
                var detail = new TextBlock
                {
                    FontSize = 11.5, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center,
                    Text = r == null ? $"{b.CriteresActifs.Count() + b.TracesActives.Count()} contrainte(s) · {(b.Type == AnalyseKind.Ac ? "fréquentiel" : "temporel")}" : r.Erreur != null ? "erreur" : $"{r.Respectes}/{r.Nombre} respectée(s)"
                };
                Grid.SetColumn(detail, 2); row.Children.Add(detail);
                var bb = b;
                var btn = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8) };
                btn.Click += (_, _) => SelectBanc(bb);
                _circuitPanel.Children.Add(btn);
            }
            RefreshGabarits();
            return;
        }
        var bh = new Grid { ColumnDefinitions = new("*,Auto") };
        var t = Ui.Title("Banc de test"); t.Foreground = Ui.BenchBrush; bh.Children.Add(t);
        var del = Ui.ToolButton("Supprimer le banc", "Supprimer ce banc, ses sources et ses gabarits", () =>
        {
            DeleteBanc(Banc);
        });
        del.FontSize = 11.5; del.Foreground = Ui.Muted; Grid.SetColumn(del, 1); bh.Children.Add(del);
        _benchPanel.Children.Add(bh);
        var nom = new TextBox { Text = Banc.Nom, FontSize = 13 };
        void Rename() { var n = (nom.Text ?? "").Trim(); if (n.Length > 0 && n != Banc.Nom) { Banc.Nom = n; RefreshBenchBar(); SelectBanc(Banc); } }
        nom.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rename(); }; nom.LostFocus += (_, _) => Rename();
        _benchPanel.Children.Add(Ui.Labeled("Nom", nom));
        _benchPanel.Children.Add(Ui.Rule());
        _benchPanel.Children.Add(Ui.Title("Analyses"));
        _benchPanel.Children.Add(BuildAnalysisPanel());
        _benchPanel.Children.Add(Ui.Rule());
        var gh = new Grid { ColumnDefinitions = new("*,Auto") };
        gh.Children.Add(Ui.Title("Critères mesurés"));
        var add = Ui.ToolButton("+ Critère", "Ajouter un critère pass/fail", () =>
        {
            var n = Connectivity.Build(SchemaCourant).NamedNodes.FirstOrDefault() ?? "OUT";
            Banc.Gabarits.Add(new CritereGabarit { Mesure = Banc.Type == AnalyseKind.Ac ? TypeMesure.BandePassante3dB : TypeMesure.TempsDeMontee, Noeud = n }); RefreshGabarits();
        });
        Grid.SetColumn(add, 1); gh.Children.Add(add);
        _benchPanel.Children.Add(gh); _benchPanel.Children.Add(_gabarits);
        var th = Ui.Title("Gabarits dessinés"); th.Margin = new Thickness(0, 12, 0, 6);
        _benchPanel.Children.Add(th); _benchPanel.Children.Add(_traces);
        RefreshGabarits();
    }

    Control BuildAnalysisPanel()
    {
        var s = new StackPanel { Spacing = 6 };
        var tran = new Grid { ColumnDefinitions = new("*,8,*") };
        tran.Children.Add(Ui.Field("Pas", SpiceNumber.Format(Banc.Transitoire.Pas, "s"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) Banc.Transitoire.Pas = x; }));
        var dur = Ui.Field("Durée", SpiceNumber.Format(Banc.Transitoire.Duree, "s"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) Banc.Transitoire.Duree = x; });
        Grid.SetColumn(dur, 2); tran.Children.Add(dur);
        var ac = new Grid { ColumnDefinitions = new("*,8,*,8,70") };
        ac.Children.Add(Ui.Field("De", SpiceNumber.Format(Banc.Ac.FDebut, "Hz"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) Banc.Ac.FDebut = x; }));
        var to = Ui.Field("À", SpiceNumber.Format(Banc.Ac.FFin, "Hz"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) Banc.Ac.FFin = x; });
        Grid.SetColumn(to, 2); ac.Children.Add(to);
        var pts = Ui.Field("Pts/déc.", Banc.Ac.PointsParDecade.ToString(), v => { if (int.TryParse(v, out var x) && x > 0) Banc.Ac.PointsParDecade = x; });
        Grid.SetColumn(pts, 4); ac.Children.Add(pts);

        var type = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var (k, icon, label) in new[] { (AnalyseKind.Transitoire, Icones.Temporel, "Temporel"), (AnalyseKind.Ac, Icones.Bode, "Fréquentiel") })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(Icones.Make(icon, 15, Banc.Type == k ? Ui.AccentBrush : Ui.Muted)); row.Children.Add(new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = row, Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Banc.Type == k ? Ui.AccentBrush : Ui.Line, Background = Banc.Type == k ? Ui.ActiveBg : Brushes.White };
            ToolTip.SetTip(b, k == AnalyseKind.Ac ? "Banc fréquentiel : analyse .ac, courbes en Bode" : "Banc temporel : analyse .tran, courbes en fonction du temps");
            var kk = k; b.Click += (_, _) => ShowAnalysis(kk);
            type.Children.Add(b);
        }
        s.Children.Add(type);
        if (Banc.Type == AnalyseKind.Transitoire) { s.Children.Add(Ui.SubTitle("Transitoire (.tran)")); s.Children.Add(tran); }
        else { s.Children.Add(Ui.SubTitle("Petit signal (.ac, décade)")); s.Children.Add(ac); }
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
        _gabarits.Children.Clear(); _traces.Children.Clear();
        if (_banc == null) return;
        RefreshTraces();
        if (!Banc.CriteresActifs.Any())
            _gabarits.Children.Add(Ui.Hint("Aucun critère. Ajoutez par exemple « Bande passante −3 dB ≥ 1 kHz sur OUT »."));
        var nodes = Connectivity.Build(SchemaCourant).AllNodes.ToList();
        foreach (var g in Banc.CriteresActifs.ToList())
        {
            var res = _last?.Criteres.FirstOrDefault(r => r.Critere == g)?.Resultat;
            var card = new StackPanel { Spacing = 6 };
            var head = new Grid { ColumnDefinitions = new("*,Auto") };
            var choix = TypesMesure.Tous.Where(i => i.Analyse == Banc.Type).ToList();
            var mesure = new ComboBox { ItemsSource = choix.Select(i => i.Nom).ToList(), SelectedIndex = choix.FindIndex(i => i.Type == g.Mesure), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
            mesure.SelectionChanged += (_, _) => { if (mesure.SelectedIndex >= 0 && choix[mesure.SelectedIndex].Type != g.Mesure) { g.Mesure = choix[mesure.SelectedIndex].Type; Invalidate(); } };
            var del = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = Ui.Muted, Padding = new Thickness(6, 2), Margin = new Thickness(4, 0, 0, 0) };
            ToolTip.SetTip(del, "Retirer ce critère");
            del.Click += (_, _) => { Banc.Gabarits.Remove(g); Invalidate(); };
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
        if (!Banc.TracesActives.Any())
            _traces.Children.Add(Ui.Hint("Aucun. En mode Test, sous les courbes : choisissez un signal puis « Limite haute » ou « Limite basse » et cliquez sur le graphique."));
        foreach (var g in Banc.TracesActives.ToList())
        {
            var res = _last?.Traces.FirstOrDefault(t => t.Gabarit == g)?.Resultat;
            var card = new StackPanel { Spacing = 4 };
            var head = new Grid { ColumnDefinitions = new("*,Auto") };
            head.Children.Add(new TextBlock { Text = g.Nom, FontWeight = FontWeight.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var del = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = Ui.Muted, Padding = new Thickness(6, 2) };
            ToolTip.SetTip(del, "Retirer ce gabarit");
            del.Click += (_, _) => { Banc.GabaritsTraces.Remove(g); ReevaluateTraces(); RefreshGabarits(); RefreshPlot(keepLimits: true); };
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
        var schema = SchemaCourant;
        var conn = Connectivity.Build(schema);
        var issues = Erc.Check(schema, conn, circuitSeul: _banc == null);
        _netlist.Text = CircuitBuilder.Preview(schema, conn, _banc == null ? null : CircuitBuilder.AnalysisFor(Banc, Shown));
        _ercList.Children.Clear();
        if (issues.Count == 0) _ercList.Children.Add(new TextBlock { Text = _banc == null ? "✓ Circuit cohérent (références, valeurs). Masse et broches libres sont vérifiées avec chaque banc." : "✓ Banc cohérent : masse présente, pas de broche flottante ni de référence dupliquée.", Foreground = Ui.PassBrush });
        foreach (var i in issues)
            _ercList.Children.Add(new TextBlock { Text = (i.Severity == Severity.Error ? "⛔ " : "⚠ ") + i.Message, Foreground = i.Severity == Severity.Error ? Ui.FailBrush : Ui.WarnBrush, TextWrapping = TextWrapping.Wrap });
        int nErr = issues.Count(i => i.Severity == Severity.Error), nWarn = issues.Count - nErr;
        _ercTab.Header = nErr + nWarn == 0 ? "Vérifications ✓" : $"Vérifications ({nErr + nWarn})";
        // Circuit modifié : tous les bancs sont à relancer. Calque modifié : seulement le banc actif.
        bool had = _banc == null ? _results.Count > 0 : _last != null;
        if (_banc == null) _results.Clear(); else _last = null;
        if (had) { SetVerdict(null); RefreshGabarits(); RefreshPlot(); RefreshBenchBar(); if (_banc == null) RefreshSide(); }
        RefreshPlotToolbar();
        RefreshProperties();
    }

    // ───────────────────────── simulation ─────────────────────────
    public async Task SimulateAsync()
    {
        _cts?.Cancel(); _cts = new();
        _simulate.IsEnabled = false;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (_banc == null)
            {
                // Depuis le circuit : tous les bancs, en parallèle (non-régression).
                if (_etude.Bancs.Count == 0) { _status.Text = "Aucun banc de test : créez-en un pour brancher des stimuli sur le circuit."; return; }
                _status.Text = $"Simulation de {_etude.Bancs.Count} banc(s)…";
                var all = await Task.WhenAll(_etude.Bancs.Select(async b => (b, r: await Verification.RunAsync(_etude, b, [], _cts.Token))));
                foreach (var (b, r) in all) _results[b] = r;
                _journal.Text = string.Join("\n", all.Select(x => $"════ {x.b.Nom} ════\n{x.r.Journal}"));
                int ok = all.Count(x => x.r.Erreur == null && (x.r.Nombre == 0 || x.r.Global == Verdict.Pass));
                _status.Text = $"{all.Length} banc(s) simulé(s) en {sw.ElapsedMilliseconds} ms · {ok}/{all.Length} conforme(s)";
                SetVerdict(GlobalVerdict());
                RefreshSide(); RefreshBenchBar();
                return;
            }
            _status.Text = "Simulation ngspice en cours…";
            var res = await Verification.RunAsync(_etude, Banc, [], _cts.Token);
            _last = res;
            _journal.Text = res.Journal;
            if (res.Erreur != null)
            {
                _status.Text = "⛔ " + res.Erreur;
                _bottomTabs.SelectedIndex = res.Erc.Any(i => i.Severity == Severity.Error) ? 1 : 2;
                SetVerdict(null);
            }
            else
            {
                _status.Text = $"« {Banc.Nom} » simulé en {sw.ElapsedMilliseconds} ms ({string.Join(" + ", res.Resultats.Keys.Select(k => k == AnalyseKind.Ac ? "AC" : "transitoire"))}) · {res.Respectes}/{res.Nombre} contrainte(s) respectée(s)";
                SetVerdict(res.Nombre == 0 ? null : res.Global);
                _bottomTabs.SelectedIndex = 0;
            }
            RefreshGabarits(); RefreshPlot(); RefreshBenchBar();
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
        if (_banc == null || _last == null || !_last.Resultats.TryGetValue(Shown, out var raw))
        {
            p.Title(""); p.Axes.Bottom.Label.Text = ""; p.Axes.Left.Label.Text = "";
            var t = p.Add.Text(_banc == null ? "Sélectionnez un banc de test pour voir ses courbes" : _last == null ? "Lancez « Simuler et vérifier » pour tracer les courbes" : "Cette analyse n'a pas été simulée : relancez la simulation", 0, 0);
            t.LabelFontSize = 14; t.LabelFontColor = ScottPlot.Color.FromHex("#5b6170"); t.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
            p.Axes.SetLimits(-1, 1, -1, 1); p.HideGrid(); p.Axes.Left.IsVisible = p.Axes.Bottom.IsVisible = p.Axes.Top.IsVisible = false; _plot.Refresh(); return;
        }
        p.ShowGrid(); p.Axes.Left.IsVisible = p.Axes.Bottom.IsVisible = p.Axes.Top.IsVisible = true;
        bool ac = Shown == AnalyseKind.Ac;
        double X(double x) => ac ? Math.Log10(x) : x * 1e3;
        var conn = Connectivity.Build(SchemaCourant);
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
                var hv = _dragVertex ?? _hoverVertex;
                if (hv is { } h0 && h0.G == g && h0.I < c.Count)
                {
                    var big = p.Add.Marker(c[h0.I].X, c[h0.I].Y); big.Size = 15; big.Shape = ScottPlot.MarkerShape.FilledSquare; big.Color = red;
                    var halo = p.Add.Marker(c[h0.I].X, c[h0.I].Y); halo.Size = 24; halo.Shape = ScottPlot.MarkerShape.OpenSquare; halo.Color = red.WithAlpha(0.45); halo.LineWidth = 2;
                }
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
            foreach (var g in Banc.GabaritsTraces.Where(g => g.Analyse == Shown))
                DrawMask(g, g.Points, _last.Traces.FirstOrDefault(t => t.Gabarit == g)?.Resultat, false);
            if (_drawing != null)
                DrawMask(_drawing, _hover is { } h && (_drawing.Points.Count == 0 || h.X > _drawing.Points[^1].X) ? _drawing.Points.Append(h).ToList() : _drawing.Points, null, true);
            foreach (var g in Banc.GabaritsTraces.Where(g => g.Analyse == Shown)) foreach (var q in g.Points) { yMin = Math.Min(yMin, q.Y); yMax = Math.Max(yMax, q.Y); }

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
