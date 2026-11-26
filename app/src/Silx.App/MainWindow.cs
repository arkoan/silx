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

/// <summary>
/// Fenêtre principale. Organisation : palette de composants (gauche), schéma / netlist (centre),
/// propriétés + analyse + gabarits (droite), courbes / vérifications / journal (bas).
/// </summary>
public sealed partial class MainWindow : Window
{
    EtudeModel _etude = EtudeModel.Demo();
    readonly SchematicCanvas _canvas = new();
    readonly TextBox _netlist = Mono();
    readonly TextBox _journal = Mono();
    readonly StackPanel _ercList = new() { Spacing = 4, Margin = new Thickness(10) };
    /// <summary>Graphique actif (celui qui reçoit sondes, curseurs, gabarits et loupes).</summary>
    AvaPlot _plot = new();
    /// <summary>Un graphique de la pile : ses signaux (nœuds sondés), son tracé et son en-tête.</summary>
    sealed class Pane
    {
        public required List<string> Signaux { get; init; }
        public required AvaPlot Plot { get; init; }
        public required Grid Host { get; init; }
        public required Border Frame { get; init; }
        /// <summary>Bandeau des mesures automatiques sous le graphique.</summary>
        public WrapPanel Mesures { get; } = new() { Margin = new Thickness(10, 0, 10, 2) };
    }
    readonly List<Pane> _panes = new();
    Pane? _active;
    readonly Grid _panesGrid = new();
    readonly Canvas _overlay = new() { IsHitTestVisible = false };
    readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted, FontSize = 12 };
    readonly Border _verdict = new() { CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly StackPanel _props = new() { Spacing = 6 };
    readonly StackPanel _gabarits = new() { Spacing = 8 };
    readonly StackPanel _traces = new() { Spacing = 8 };
    readonly StackPanel _palette = new() { Spacing = 2 };
    readonly Button _simulate;
    readonly Dictionary<ComponentKind, Button> _paletteButtons = new();
    // Composants SPICE importés : bibliothèque de l'utilisateur et tuiles de la palette.
    readonly BibliothequeComposants _bib = BibliothequeComposants.Charger(BibliothequeComposants.DossierParDefaut());
    readonly WrapPanel _spiceWrap = new() { ItemWidth = 46, ItemHeight = 40 };
    readonly Dictionary<DefinitionComposant, Button> _spiceButtons = new();
    public BibliothequeComposants Bibliotheque => _bib;
    public SchematicCanvas Schema => _canvas;
    Button _toolSelect = null!, _toolWire = null!;
    // Courbes : mode Simulation (courbes seules) / Test (gabarits visibles et dessinables).
    bool _modeTest = true;
    readonly Button _modeSim = new(), _modeTestBtn = new();
    readonly StackPanel _testTools = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    readonly Button _drawHigh = new(), _drawLow = new();
    Button _viewTran = null!, _viewAc = null!, _viewFft = null!;
    bool _vueFft;
    /// <summary>Vue spectrale (FFT) d'un banc temporel : amplitude en dBV selon la fréquence.</summary>
    public bool VueFft => _vueFft && _banc?.Type == AnalyseKind.Transitoire;
    public void AfficherFft(bool fft) { _vueFft = fft; if (fft) EndDrawing(false); RefreshPlotToolbar(); RefreshPlot(); }
    // Édition des points d'un gabarit : survol (grossit) puis glisser.
    (GabaritTrace G, int I)? _hoverVertex, _dragVertex;
    // Curseurs de mesure : deux au plus par courbe (A, B), ils suivent la courbe.
    public sealed class Curseur { public required string Noeud { get; init; } public required char Nom { get; init; } public double X { get; set; } }
    readonly List<Curseur> _cursors = new();
    Curseur? _dragCursor, _hoverCursor;
    bool _cursorMode, _zoomMode; Point? _zoomStart;
    Button _cursorBtn = null!, _zoomWinBtn = null!, _probeBtn = null!;
    readonly Border _readout = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 52), Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6), IsVisible = false, IsHitTestVisible = false };
    readonly Border _zoomRect = new() { BorderBrush = Ui.AccentBrush, BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.Parse("#2563eb"), 0.08), IsVisible = false, IsHitTestVisible = false };
    public IReadOnlyList<Curseur> Curseurs => _cursors;
    /// <summary>Pose (ou déplace) le curseur A ou B sur la courbe la plus proche du point (repère du graphique actif).</summary>
    public void PoserCurseur(char nom, Point pos)
    {
        if (_banc == null || _last == null || VueFft || CourbeProche(pos) is not { } sig) return;
        var c = _cursors.FirstOrDefault(k => k.Noeud == sig && k.Nom == nom);
        if (c == null) { c = new Curseur { Noeud = sig, Nom = nom }; _cursors.Add(c); }
        c.X = ClampToCurve(sig, ToData(pos).X);
        RefreshPlot(keepLimits: true);
    }
    bool _plotSurvol; Point _posPlot;
    readonly Border _puceCourbes = new() { Background = Ui.AccentBrush, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3), IsVisible = false, IsHitTestVisible = false };

    public void SetCursorMode(bool on) { _cursorMode = on; _zoomMode = false; RefreshPlotToolbar(); }
    public void SetZoomWindowMode(bool on) { _zoomMode = on; _cursorMode = false; RefreshPlotToolbar(); }
    public Control Readout => _readout;
    public Border PuceCourbes => _puceCourbes;
    public bool PhaseTracee => _plot.Plot.Axes.Right.IsVisible && _plot.Plot.GetPlottables().OfType<ScottPlot.Plottables.Scatter>().Any(sc => sc.Axes.YAxis == _plot.Plot.Axes.Right);
    public bool PlanDeTestVisible => _benchPanel.IsEffectivelyVisible;
    public bool ProprietesVisibles => _blocProps.IsEffectivelyVisible;
    public int LignesPlan => _gabarits.Children.OfType<Button>().Count();
    public void OuvrirLignePlan(object cle) { _ligneOuverte = cle; MontrerContrainte(cle); RefreshGabarits(); RefreshPlot(keepLimits: true); }
    public double? PhaseCurseur(Curseur c) => PhaseAt(c.Noeud, c.X);
    readonly TextBlock _drawHint = new() { FontSize = 12, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
    GabaritTrace? _drawing;
    (double X, double Y)? _hover;
    CancellationTokenSource? _cts;
    // Bancs de test : null = circuit sous test seul.
    Banc? _banc;
    Dictionary<Banc, ResultatEtude> _results = new();
    // Projets ouverts, un onglet chacun.
    readonly List<ProjetOuvert> _projets = new();
    ProjetOuvert? _projet;
    readonly StackPanel _ongletsProjets = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 3, 6, 0) };
    readonly Avalonia.Threading.DispatcherTimer _veille = new() { Interval = TimeSpan.FromSeconds(1.5) };
    bool _fermetureConfirmee, _chargement;
    readonly StackPanel _benchBar = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 6) };
    readonly StackPanel _benchPanel = new() { Spacing = 6 };
    readonly StackPanel _circuitPanel = new() { Spacing = 8 };
    readonly StackPanel _analysisHost = new();
    readonly List<Button> _sourceButtons = new();
    Border _left = null!, _right = null!;
    readonly Button _undoDelete = new() { Content = "Rétablir le banc", FontSize = 12, Padding = new Thickness(8, 1), IsVisible = false, Background = Brushes.Transparent, Foreground = Ui.AccentBrush };

    Banc Banc => _banc ?? throw new InvalidOperationException("Aucun banc actif.");
    AnalyseKind _typeAffiche;
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
        var onglets = new Border { Background = Ui.Window, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = new ScrollViewer { Content = _ongletsProjets, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
        DockPanel.SetDock(onglets, Dock.Top); root.Children.Add(onglets);
        _undoDelete.Click += (_, _) =>
        {
            if (_deleted is { } d) { _etude.Bancs.Insert(Math.Min(d.Index, _etude.Bancs.Count), d.B); _deleted = null; SelectBanc(d.B); _status.Text = $"Banc « {d.B.Nom} » rétabli."; }
            _undoDelete.IsVisible = false;
        };
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        statusRow.Children.Add(_status); statusRow.Children.Add(_undoDelete);
        statusRow.Children.Add(_progCampagne); statusRow.Children.Add(_annulerCampagne);
        _annulerCampagne.Click += (_, _) => _cts?.Cancel();
        var status = new Border { Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 2), Child = statusRow };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var explo = BuildExplorateur(); DockPanel.SetDock(explo, Dock.Left); root.Children.Add(explo);
        var left = BuildPalette(); DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        var right = BuildSidePanel(); DockPanel.SetDock(right, Dock.Right); root.Children.Add(right);
        root.Children.Add(BuildCenter());
        Content = root;

        _canvas.Changed += OnSchemaChanged;
        _canvas.SelectionChanged += RefreshProperties;
        _canvas.ToolChanged += RefreshToolButtons;
        _canvas.Probed += OnProbed;
        _canvas.DoubleClic += EditerSurPlace;
        _canvas.DoubleClicCircuit += EditerCircuitDepuisBanc;
        KeyDown += async (_, e) =>
        {
            if (_drawing != null && e.Key is Key.Enter or Key.Escape) { e.Handled = true; EndDrawing(e.Key == Key.Enter); return; }
            if (e.Key == Key.F5 || (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Meta))) { e.Handled = true; await SimulateAsync(); }
            bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (cmd0(e) && e.Key == Key.K) { e.Handled = true; OuvrirPaletteCommandes(); return; }
            if (!cmd0(e) && FocusManager?.GetFocusedElement() is not TextBox && (e.KeySymbol == "?" || (e.Key == Key.OemComma && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))) { e.Handled = true; AfficherAide(); return; }
            if (e.Key is Key.F1 or Key.F2 or Key.F3) { e.Handled = true; Disposer(e.Key == Key.F1 ? Disposition.Schema : e.Key == Key.F2 ? Disposition.Mixte : Disposition.Courbes); return; }
            if (!cmd && e.Key is Key.A or Key.B && _plotSurvol && _plot.IsPointerOver && FocusManager?.GetFocusedElement() is not TextBox)
            { e.Handled = true; PoserCurseur(e.Key == Key.A ? 'A' : 'B', _posPlot); return; }
            if (cmd && e.Key == Key.OemComma) { e.Handled = true; OuvrirParametres(); return; }
            if (cmd && e.Key == Key.N) { e.Handled = true; NouveauProjet(); }
            else if (cmd && e.Key == Key.O) { e.Handled = true; await OuvrirProjetAsync(); }
            else if (cmd && e.Key == Key.S) { e.Handled = true; await EnregistrerAsync(_projet!, e.KeyModifiers.HasFlag(KeyModifiers.Shift)); }
            else if (cmd && e.Key == Key.W) { e.Handled = true; await FermerProjetAsync(_projet!); }
        };
        _veille.Tick += (_, _) => RafraichirOnglets();
        // Thème « Système » : suit le passage de macOS en clair ou sombre.
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) =>
            {
                if (Parametres.Courant.Theme != "systeme") return;
                Ui.AppliquerTheme(app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark); AppliquerParametres();
            };
        if (Environment.GetEnvironmentVariable("SILX_SIM_AUTO") == "0") _auto.IsChecked = false;   // tests : pas de relance en arrière-plan
        _relance.Tick += async (_, _) => { _relance.Stop(); if (!_simEnCours) await SimulerAsync(keepLimits: true, campagne: false); };   // la campagne ne se relance pas toute seule
        _veille.Start();
        Closing += async (_, e) =>
        {
            if (_fermetureConfirmee || !_projets.Any(p => p.Modifie)) return;
            e.Cancel = true;
            var choix = await Demander($"{_projets.Count(p => p.Modifie)} projet(s) ont des modifications non enregistrées.", "Tout enregistrer et quitter", "Quitter sans enregistrer");
            if (choix == 0) { foreach (var p in _projets.Where(p => p.Modifie).ToList()) if (!await EnregistrerAsync(p, false)) return; }
            if (choix is 0 or 1) { _fermetureConfirmee = true; Close(); }
        };

        OuvrirDansOnglet(EtudeModel.Demo(), "Exemple RC", null);
        Disposer(Disposition.Mixte);
        ActiverZone(Zone.Schema);
        _status.Text = "Clic : placer / sélectionner · glisser : déplacer · clic droit ou molette : vue · ⌘R : pivoter · W : fil · Suppr · ⌘Z / ⇧⌘Z";
    }

    static bool cmd0(KeyEventArgs e) => e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

    static Control SimContent()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        sp.Children.Add(Icones.Make(Icones.Lecture, 13, Brushes.White, filled: true));
        sp.Children.Add(new TextBlock { Text = "Simuler", VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    public EtudeModel EtudeCourante => _etude;
    public AvaPlot PlotControl => _plot;
    public IReadOnlyList<IReadOnlyList<string>> SignauxGraphiques => _panes.Select(p => (IReadOnlyList<string>)p.Signaux).ToList();
    public SchematicCanvas Canevas => _canvas;
    public void AjouterGraphique() => AddPane();
    public void SetModeTest(bool test) { _modeTest = test; RefreshPlotToolbar(); RefreshPlot(); }
    /// <summary>Change le type du banc actif (temporel / fréquentiel).</summary>
    public void ShowAnalysis(AnalyseKind k)
    {
        if (_banc == null || _banc.Type == k) return;
        EndDrawing(false); _banc.Type = k; _last = null; SetVerdict(null);
        RefreshSide(); RefreshBenchBar(); RefreshPlotToolbar(); RefreshPlot(); OnSchemaChanged();
    }

    // ───────────────────────── projets (onglets) ─────────────────────────
    public IReadOnlyList<ProjetOuvert> Projets => _projets;
    public ProjetOuvert ProjetActif => _projet!;

    /// <summary>Ouvre une étude dans un nouvel onglet et l'affiche.</summary>
    public ProjetOuvert OuvrirDansOnglet(EtudeModel e, string nom, string? chemin)
    {
        var p = new ProjetOuvert { Etude = e, Nom = nom, Chemin = chemin };
        // Composants SPICE du projet absents de la bibliothèque : proposés dans la palette (sans les enregistrer).
        foreach (var d in e.Schema.Components.Concat(e.Bancs.SelectMany(b => b.Calque.Components)).Select(k => k.Definition).OfType<DefinitionComposant>().Distinct())
            if (!_bib.Definitions.Any(x => string.Equals(x.Modele, d.Modele, StringComparison.OrdinalIgnoreCase) && x.TexteSpice == d.TexteSpice)) _bib.Definitions.Add(d);
        RebuildSpicePalette();
        _projets.Add(p); ActiverProjet(p);
        p.MarquerEnregistre();   // après l'affichage, qui complète des réglages par défaut (graphiques)
        RafraichirOnglets();
        return p;
    }

    public void ActiverProjet(ProjetOuvert p)
    {
        if (_projet != null) _projet.BancActif = _banc;
        EndDrawing(false);
        _projet = p; _etude = p.Etude; _results = p.Resultats; _cursors.Clear();
        _banc = null;   // force le rechargement du canevas
        SelectBanc(p.BancActif != null && p.Etude.Bancs.Contains(p.BancActif) ? p.BancActif : p.Etude.Bancs.FirstOrDefault());
        RafraichirOnglets();
    }

    int _numeroSansTitre = 1;
    public ProjetOuvert NouveauProjet() => OuvrirDansOnglet(new EtudeModel(), $"Sans titre {_numeroSansTitre++}", null);

    void RafraichirOnglets()
    {
        if (_projet == null) return;
        Title = $"Silx — {_projet.Nom}{(_projet.Modifie ? " •" : "")}";
        _ongletsProjets.Children.Clear();
        foreach (var p in _projets)
        {
            bool actif = p == _projet, modif = p.Modifie;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = p.Nom, FontSize = Ui.Texte, FontWeight = actif ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center });
            if (modif) row.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Ui.WarnBrush, VerticalAlignment = VerticalAlignment.Center });
            var x = new Button { Content = Icones.Make(Icones.Croix, 9, Ui.Muted), Padding = new Thickness(3), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(x, "Fermer ce projet (⌘W)");
            var pp = p; x.Click += async (_, ev) => { ev.Handled = true; await FermerProjetAsync(pp); };
            row.Children.Add(x);
            var b = new Button
            {
                Content = row, Padding = new Thickness(12, 3, 4, 3), CornerRadius = new CornerRadius(6, 6, 0, 0),
                Background = actif ? Ui.Panel : Brushes.Transparent, BorderBrush = actif ? Ui.Line : Brushes.Transparent, BorderThickness = new Thickness(1, 1, 1, 0)
            };
            ToolTip.SetTip(b, (p.Chemin ?? "Pas encore enregistré") + (modif ? " — modifications non enregistrées" : ""));
            b.Click += (_, _) => { if (pp != _projet) ActiverProjet(pp); };
            _ongletsProjets.Children.Add(b);
        }
        var plus = new Button { Content = Icones.Make(Icones.Plus, 13, Ui.Muted), Padding = new Thickness(7, 4), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(plus, "Nouveau projet (⌘N)");
        plus.Click += (_, _) => NouveauProjet();
        _ongletsProjets.Children.Add(plus);
    }

    async Task OuvrirProjetAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Ouvrir un projet Silx", AllowMultiple = true,
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("Projet Silx") { Patterns = ["*" + Projet.Extension] }]
        });
        foreach (var f in files) if (f.TryGetLocalPath() is { } chemin) OuvrirFichier(chemin);
    }

    /// <summary>Ouvre un fichier .silx dans un onglet (ou active l'onglet s'il est déjà ouvert).</summary>
    public ProjetOuvert? OuvrirFichier(string chemin)
    {
        if (_projets.FirstOrDefault(p => p.Chemin != null && Path.GetFullPath(p.Chemin) == Path.GetFullPath(chemin)) is { } deja) { ActiverProjet(deja); return deja; }
        try
        {
            var p = OuvrirDansOnglet(Projet.Ouvrir(chemin), Path.GetFileNameWithoutExtension(chemin), chemin);
            _status.Text = $"Projet ouvert : {chemin}";
            return p;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _status.Text = $"⛔ Impossible d'ouvrir {Path.GetFileName(chemin)} : {ex.Message}";
            return null;
        }
    }

    /// <summary>Enregistre un projet ; demande un fichier s'il n'en a pas encore (ou pour « Enregistrer sous »).</summary>
    public async Task<bool> EnregistrerAsync(ProjetOuvert p, bool sous)
    {
        var chemin = p.Chemin;
        if (chemin == null || sous)
        {
            var f = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = $"Enregistrer « {p.Nom} »", SuggestedFileName = p.Nom + Projet.Extension, DefaultExtension = Projet.Extension.TrimStart('.'),
                FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("Projet Silx") { Patterns = ["*" + Projet.Extension] }]
            });
            chemin = f?.TryGetLocalPath();
            if (chemin == null) return false;
        }
        return EnregistrerSous(p, chemin);
    }

    public bool EnregistrerSous(ProjetOuvert p, string chemin)
    {
        try
        {
            Projet.Enregistrer(p.Etude, chemin);
            p.Chemin = chemin; p.Nom = Path.GetFileNameWithoutExtension(chemin); p.MarquerEnregistre();
            _status.Text = $"Projet enregistré : {chemin}";
            RafraichirOnglets();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"⛔ Enregistrement impossible : {ex.Message}";
            return false;
        }
    }

    public async Task FermerProjetAsync(ProjetOuvert p)
    {
        if (p.Modifie)
        {
            var choix = await Demander($"Enregistrer les modifications de « {p.Nom} » avant de le fermer ?", "Enregistrer", "Ne pas enregistrer");
            if (choix == 2 || (choix == 0 && !await EnregistrerAsync(p, false))) return;
        }
        FermerSansDemander(p);
    }

    public void FermerSansDemander(ProjetOuvert p)
    {
        int i = _projets.IndexOf(p); if (i < 0) return;
        _projets.RemoveAt(i);
        if (_projets.Count == 0) { NouveauProjet(); return; }
        if (p == _projet) { _projet = null; ActiverProjet(_projets[Math.Min(i, _projets.Count - 1)]); }
        else RafraichirOnglets();
    }

    /// <summary>Petite boîte de dialogue : renvoie 0 (premier bouton), 1 (second) ou 2 (Annuler).</summary>
    async Task<int> Demander(string question, string oui, string non)
    {
        int r = 2;
        var w = new Window { Title = "Silx", Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Panel };
        var bA = new Button { Content = "Annuler", Padding = new Thickness(12, 4) };
        var bN = new Button { Content = non, Padding = new Thickness(12, 4) };
        var bO = new Button { Content = oui, Padding = new Thickness(12, 4), Background = Ui.AccentBrush, Foreground = Brushes.White };
        bO.Click += (_, _) => { r = 0; w.Close(); }; bN.Click += (_, _) => { r = 1; w.Close(); }; bA.Click += (_, _) => { r = 2; w.Close(); };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        btns.Children.Add(bA); btns.Children.Add(bN); btns.Children.Add(bO);
        var sp = new StackPanel { Spacing = 16, Margin = new Thickness(18) };
        sp.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        sp.Children.Add(btns);
        w.Content = sp;
        await w.ShowDialog(this);
        return r;
    }

    public void SelectBanc(Banc? b)
    {
        EndDrawing(false);
        if (_banc != b) _cursors.Clear();
        _banc = b; _typeAffiche = b?.Type ?? AnalyseKind.Transitoire;
        _canvas.Caption = b == null ? "" : $"Banc « {b.Nom} »"; if (b != null) _canvas.CouleurCalque = CouleurDe(b);
        ToolTip.SetTip(_canvas, b == null ? null : "Le banc est le calque violet ; le circuit dessous, atténué, se modifie dans « Circuit sous test ».");
        _chargement = true;   // recharger le canevas n'est pas une modification : les résultats restent valables
        _canvas.SimulationPossible = b != null;
        try { _canvas.Load(b?.Calque ?? _etude.Schema, b == null ? null : _etude.Schema); } finally { _chargement = false; }
        if (b == null) _canvas.CalquesLies = _etude.Bancs.Select(x => x.Calque).ToList();
        _canvas.FitAll();
        if (b != null && b.Graphiques.Count == 0)
        {
            var conn0 = Connectivity.Build(b.Fusion(_etude.Schema));
            b.Graphiques.Add((conn0.NamedNodes.Count > 0 ? conn0.NamedNodes : conn0.AllNodes.Take(2)).ToList());
        }
        RebuildPanes();
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
        RafraichirExplorateur();
        _benchBar.Children.Clear();
        Button Tab(string text, bool active, Verdict? v, Action click, IBrush accent, Banc? removable = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (v != null) row.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Background = v == Verdict.Pass ? Ui.PassBrush : v == Verdict.Fail ? Ui.FailBrush : Ui.WarnBrush });
            row.Children.Add(new TextBlock { Text = text, FontSize = Ui.Texte, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center });
            if (removable != null)
            {
                // Roue dentée : réglages du banc (nom, analyse) dans une bulle.
                var reg = new Button { Content = Icones.Make(Icones.Reglages, 12, Ui.Muted), Padding = new Thickness(3), Background = Brushes.Transparent, CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(reg, $"Réglages du banc « {removable.Nom} » : nom, analyse");
                var rb = removable; reg.Click += (_, e) => { e.Handled = true; if (_banc != rb) SelectBanc(rb); OuvrirReglagesBanc(reg); };
                row.Children.Add(reg);
                var x = new Button { Content = Icones.Make(Icones.Croix, 10, Ui.Muted), Padding = new Thickness(3), Background = Brushes.Transparent, CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 0, -6, 0), VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(x, $"Supprimer le banc « {removable.Nom} »");
                x.Click += (_, e) => { e.Handled = true; DeleteBanc(removable); };
                row.Children.Add(x);
            }
            var b = new Button { Content = row, Padding = new Thickness(12, 4), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
                BorderBrush = active ? accent : Ui.Line, Background = active ? (accent == Ui.AccentBrush ? Ui.ActiveBg : Ui.Teinte(accent)) : Ui.Surface, Foreground = active ? accent : Ui.Ink };
            b.Click += (_, _) => click();
            _benchBar.Children.Add(b);
            return b;
        }
        Tab("Circuit sous test", _banc == null, null, () => SelectBanc(null), Ui.AccentBrush);
        _benchBar.Children.Add(new Border { Width = 1, Height = 18, Background = Ui.Line, Margin = new Thickness(6, 0) });
        foreach (var b in _etude.Bancs)
        {
            Verdict? v = _results.TryGetValue(b, out var r) ? (r.Erreur != null ? Verdict.NonEvaluable : r.Nombre == 0 ? null : r.Global) : null;
            var bb = b; Tab(b.Nom, _banc == b, v, () => SelectBanc(bb), CouleurDe(bb), bb);
        }
        var add = new Button { Content = Icones.Make(Icones.Plus, 14, Ui.BenchBrush), Padding = new Thickness(7), Background = Brushes.Transparent, CornerRadius = new CornerRadius(14) };
        ToolTip.SetTip(add, "Nouveau banc : un calque où brancher sources, alimentations et charges sur le circuit");
        add.Click += (_, _) => ProposerNouveauBanc(add);
        _benchBar.Children.Add(add);
    }

    // ───────────────────────── barre d'outils ─────────────────────────
    Control BuildToolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 3) };
        var logo = new TextBlock { Text = "Silx", FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Ui.AccentBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
        bar.Children.Add(logo);
        bar.Children.Add(Icones.Button(Icones.Nouveau, "Nouveau projet, dans un nouvel onglet (⌘N)", () => NouveauProjet()));
        bar.Children.Add(Icones.Button(Icones.Ouvrir, "Ouvrir un projet .silx (⌘O)", () => _ = OuvrirProjetAsync()));
        bar.Children.Add(Icones.Button(Icones.Enregistrer, "Enregistrer le projet (⌘S) ; ⇧⌘S : enregistrer sous", () => _ = EnregistrerAsync(_projet!, false)));
        bar.Children.Add(Icones.Button(Icones.Rapport, "Exporter le rapport de test (Markdown et courbes)", () => _ = ExporterRapportAsync()));
        bar.Children.Add(Icones.Button(Icones.Exemple, "Ouvrir l'exemple : filtre RC et ses deux bancs de test", () => OuvrirDansOnglet(EtudeModel.Demo(), "Exemple RC", null)));
        bar.Children.Add(Ui.Separator());
        _toolSelect = Icones.Button(Icones.Selection, "Sélectionner et déplacer (Échap)", () => _canvas.Tool = Tool.Select);
        _toolWire = Icones.Button(Icones.Fil, "Tracer un fil (W) : double-clic ou arrivée sur une broche pour finir", () => _canvas.Tool = Tool.Wire);
        bar.Children.Add(_toolSelect); bar.Children.Add(_toolWire);
        bar.Children.Add(Icones.Button(Icones.Pivoter, "Pivoter la sélection ou le composant à placer (⌘R)", () => _canvas.RotateSelected()));
        bar.Children.Add(Icones.Button(Icones.Miroir, "Miroir gauche-droite (X)", () => _canvas.MirrorSelected()));
        bar.Children.Add(Icones.Button(Icones.Dupliquer, "Dupliquer la sélection (⌘D) ; ⌘C / ⌘V copier-coller, ⌘X couper", () => _canvas.DuplicateSelection()));
        bar.Children.Add(Icones.Button(Icones.Supprimer, "Supprimer la sélection (Suppr)", () => _canvas.DeleteSelected()));
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.Annuler, "Annuler (⌘Z)", () => _canvas.Undo()));
        bar.Children.Add(Icones.Button(Icones.Retablir, "Rétablir (⇧⌘Z)", () => _canvas.Redo()));
        bar.Children.Add(Ui.Separator());
        // Loupes uniques : elles agissent sur la zone active (schéma ou courbes), signalée par un liseré bleu.
        bar.Children.Add(Icones.Button(Icones.ZoomPlus, "Zoom avant sur la zone active (+)", () => Zoomer(1.25)));
        bar.Children.Add(Icones.Button(Icones.ZoomMoins, "Zoom arrière sur la zone active (−)", () => Zoomer(1 / 1.25)));
        bar.Children.Add(Icones.Button(Icones.ToutVoir, "Tout voir dans la zone active (0)", ToutVoir));
        _zoomWinBtn = Icones.Button(Icones.ZoomFenetre, "Zoom sur une fenêtre de la zone active (tracer un rectangle)", ZoomFenetre);
        bar.Children.Add(_zoomWinBtn);
        bar.Children.Add(Ui.Separator());
        foreach (var (d, ic, tip) in new[] { (Disposition.Schema, Icones.VueSchema, "Schéma seul (F1)"), (Disposition.Mixte, Icones.VueMixte, "Schéma et courbes (F2)"), (Disposition.Courbes, Icones.VueCourbes, "Courbes seules (F3)") })
        {
            var dd = d; var b = Icones.Button(ic, tip, () => Disposer(dd)); _boutonsDisposition[d] = b; bar.Children.Add(b);
        }
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.Chercher, "Chercher une commande ou un composant (⌘K)", OuvrirPaletteCommandes));
        bar.Children.Add(Icones.Button(Icones.Aide, "Aide-mémoire des gestes et raccourcis (?)", AfficherAide));
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.Explorateur, "Afficher / masquer l'explorateur de projet", () => ExplorateurVisible = !ExplorateurVisible));
        bar.Children.Add(Icones.Button(Icones.PanneauGauche, "Afficher / masquer la palette de composants", () => { _left.IsVisible = !_left.IsVisible; }));
        bar.Children.Add(Icones.Button(Icones.PanneauDroit, "Afficher / masquer le panneau de propriétés et de gabarits", () => { _right.IsVisible = !_right.IsVisible; }));

        var rightSide = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(8, 3), HorizontalAlignment = HorizontalAlignment.Right };
        _auto.IsChecked = Parametres.Courant.SimulationAuto;
        _auto.IsCheckedChanged += (_, _) =>
        {
            _simAuto = _auto.IsChecked == true; MajEtatResultats();
            if (Parametres.Courant.SimulationAuto != _simAuto) { Parametres.Courant.SimulationAuto = _simAuto; Parametres.Courant.Enregistrer(); }
        };
        ToolTip.SetTip(_auto, "Simulation automatique : relancée seule après chaque modification quand le calcul dure moins d'une seconde");
        rightSide.Children.Add(_etat); rightSide.Children.Add(_verdict); rightSide.Children.Add(_auto); rightSide.Children.Add(_simulate);
        rightSide.Children.Add(Icones.Button(Icones.Reglages, "Paramètres de Silx (⌘,) : général, affichage (couleur des tests…), composants", () => OuvrirParametres()));
        var g = new Grid { ColumnDefinitions = new("*,Auto") };
        g.Children.Add(bar); g.Children.Add(rightSide); Grid.SetColumn(rightSide, 1);
        return new Border { Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
    }

    FenetreParametres? _parametres;
    public FenetreParametres? FenetreParametres => _parametres;
    /// <summary>Ouvre la fenêtre Paramètres (une seule à la fois) ; chaque réglage s'applique aussitôt.</summary>
    public FenetreParametres OuvrirParametres()
    {
        if (_parametres != null) { _parametres.Activate(); return _parametres; }
        _parametres = new FenetreParametres(AppliquerParametres, _bib.Dossier ?? BibliothequeComposants.DossierParDefaut());
        _parametres.Closed += (_, _) => _parametres = null;
        _parametres.Show(this);
        return _parametres;
    }
    void AppliquerParametres()
    {
        if (_auto.IsChecked != Parametres.Courant.SimulationAuto && Environment.GetEnvironmentVariable("SILX_SIM_AUTO") != "0") _auto.IsChecked = Parametres.Courant.SimulationAuto;
        _canvas.InvalidateVisual(); RefreshBenchBar(); RefreshPlot(keepLimits: true);
    }

    void RefreshToolButtons()
    {
        Ui.SetActive(_toolSelect, _canvas.Tool == Tool.Select);
        Ui.SetActive(_toolWire, _canvas.Tool == Tool.Wire);
        foreach (var (k, b) in _paletteButtons) Ui.SetActive(b, _canvas.Tool == Tool.Place && _canvas.PlaceKind == k);
        foreach (var (d, b) in _spiceButtons) Ui.SetActive(b, _canvas.Tool == Tool.Place && _canvas.PlaceDefinition == d);
        if (_probeBtn != null) Ui.SetActive(_probeBtn, _canvas.Tool == Tool.Probe);
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
        // Groupe « Modèles SPICE » : import, puis une tuile par composant de la bibliothèque.
        _palette.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(4, 6) });
        _palette.Children.Add(_spiceWrap);
        RebuildSpicePalette();
        _palette.Margin = new Thickness(4, 8);
        return _left = new Border { Width = 102, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = _palette } };
    }

    /// <summary>Catégorie d'un composant importé, d'après son symbole ou son type de modèle.</summary>
    public static string Categorie(DefinitionComposant d) => d.CodeXspice != null ? "Logique" : d.Symbole.Gabarit switch
    {
        GabaritSymbole.Nmos or GabaritSymbole.Nmos3 => "MOSFET canal N", GabaritSymbole.Pmos or GabaritSymbole.Pmos3 => "MOSFET canal P",
        GabaritSymbole.Npn => "Bipolaire NPN", GabaritSymbole.Pnp => "Bipolaire PNP", GabaritSymbole.Diode => "Diode",
        GabaritSymbole.Njf or GabaritSymbole.Pjf => "JFET",
        _ => d.Type switch
        {
            TypeModele.Nmos => "MOSFET canal N", TypeModele.Pmos => "MOSFET canal P", TypeModele.Npn => "Bipolaire NPN", TypeModele.Pnp => "Bipolaire PNP",
            TypeModele.Diode => "Diode", TypeModele.Njf or TypeModele.Pjf => "JFET", _ => "Sous-circuit"
        }
    };
    static readonly string[] OrdreCategories = ["Diode", "Bipolaire NPN", "Bipolaire PNP", "MOSFET canal N", "MOSFET canal P", "JFET", "Logique", "Sous-circuit"];

    /// <summary>Composants importés : une tuile par catégorie (MOSFET N, bipolaire NPN…), puis la liste des références.</summary>
    void RebuildSpicePalette()
    {
        _spiceWrap.Children.Clear(); _spiceButtons.Clear();
        var imp = new Button { Content = Icones.Make(Icones.ImporterSpice, 20, Ui.AccentBrush), Padding = new Thickness(5, 7), Margin = new Thickness(2), Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        ToolTip.SetTip(imp, "Importer des modèles SPICE (.lib, .mod, .cir…) : .model D, NPN, PNP, NMOS, PMOS, NJF, PJF, portes et bascules XSPICE (d_and, d_dff…) et .subckt");
        imp.Click += async (_, _) => await ImporterFichierAsync();
        _spiceWrap.Children.Add(imp);
        foreach (var g in _bib.Definitions.GroupBy(Categorie).OrderBy(g => Array.IndexOf(OrdreCategories, g.Key)))
        {
            var defs = g.OrderBy(d => d.Nom, StringComparer.OrdinalIgnoreCase).ToList();
            var grille = new Grid();
            grille.Children.Add(new Symboles.Icone { Kind = ComponentKind.Spice, Symbole = defs[0].Symbole, Width = 34, Height = 24, Brush = Ui.Ink });
            if (defs.Count > 1)
                grille.Children.Add(new Border { Background = Ui.AccentBrush, CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -6, 0),
                    Child = new TextBlock { Text = defs.Count.ToString(), Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeight.Bold } });
            var b = new Button { Content = grille, Padding = new Thickness(5, 7), Margin = new Thickness(2), Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            ToolTip.SetTip(b, $"{g.Key} — {defs.Count} référence(s) : {string.Join(", ", defs.Select(d => d.Nom).Take(6))}{(defs.Count > 6 ? "…" : "")}");
            var cat = g.Key; b.Click += (_, _) => ProposerReferences(cat, b);
            foreach (var d in defs) _spiceButtons[d] = b;
            _spiceWrap.Children.Add(b);
        }
    }

    /// <summary>Second niveau : les références d'une catégorie ; clic pour placer, crayon pour modifier le symbole.</summary>
    public void ProposerReferences(string categorie, Control ancre)
    {
        var sp = new StackPanel { Spacing = 2, Width = 280, Margin = new Thickness(4) };
        sp.Children.Add(Ui.Title(categorie));
        var fly = new Flyout { Content = new ScrollViewer { Content = sp, MaxHeight = 420 }, Placement = PlacementMode.RightEdgeAlignedTop };
        foreach (var d in _bib.Definitions.Where(d => Categorie(d) == categorie).OrderBy(d => d.Nom, StringComparer.OrdinalIgnoreCase))
        {
            var dd = d;
            var g = new Grid { ColumnDefinitions = new("34,*,Auto") };
            g.Children.Add(new Symboles.Icone { Kind = ComponentKind.Spice, Symbole = d.Symbole, Width = 30, Height = 22, Brush = Ui.Ink });
            var txt = new StackPanel { Margin = new Thickness(6, 0) };
            txt.Children.Add(new TextBlock { Text = d.Nom, FontWeight = FontWeight.SemiBold, FontSize = Ui.Texte });
            var info = d.CodeXspice != null ? d.Description : d.Notes;
            if (info.Length > 0) txt.Children.Add(new TextBlock { Text = info, FontSize = Ui.Aide, Foreground = Ui.Muted, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180 });
            Grid.SetColumn(txt, 1); g.Children.Add(txt);
            var edit = new Button { Content = Icones.Make(Icones.EditerSymbole, 13, Ui.Muted), Padding = new Thickness(4), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(edit, "Modifier le symbole");
            edit.Click += async (_, e) => { e.Handled = true; fly.Hide(); await OuvrirEditeurAsync(dd); };
            Grid.SetColumn(edit, 2); g.Children.Add(edit);
            var b = new Button { Content = g, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 4), Background = Brushes.Transparent, CornerRadius = new CornerRadius(6) };
            ToolTip.SetTip(b, $"Placer {d.Nom} (Maj+clic sur le schéma pour en poser plusieurs)");
            b.Click += (_, _) => { fly.Hide(); _canvas.BeginPlace(dd); };
            sp.Children.Add(b);
        }
        fly.ShowAt(ancre);
    }

    async Task ImporterFichierAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Importer des modèles SPICE", AllowMultiple = false,
            FileTypeFilter = [new("Modèles SPICE") { Patterns = ["*.lib", "*.mod", "*.cir", "*.sub", "*.txt", "*.spi", "*.sp", "*.model"] }, new("Tous les fichiers") { Patterns = ["*"] }],
        });
        if (files.Count == 0) return;
        await using var st = await files[0].OpenReadAsync();
        using var rd = new StreamReader(st);
        var defs = ImporterModeles(await rd.ReadToEndAsync(), files[0].Name);
        if (defs.Count == 1) await OuvrirEditeurAsync(defs[0]);
    }

    /// <summary>Analyse un texte de modèles, ajoute chaque modèle reconnu à la bibliothèque (symbole par défaut, broches reliées par nom).</summary>
    public IReadOnlyList<DefinitionComposant> ImporterModeles(string texte, string source)
    {
        var a = ModelesSpice.Analyser(texte);
        var defs = a.Modeles.GroupBy(m => m.Nom, StringComparer.OrdinalIgnoreCase).Select(g => DefinitionComposant.Depuis(g.First())).ToList();
        foreach (var d in defs)
        {
            // Réimport d'un modèle déjà connu : on garde son symbole si les broches n'ont pas changé.
            var old = _bib.Definitions.FirstOrDefault(x => string.Equals(x.Modele, d.Modele, StringComparison.OrdinalIgnoreCase));
            // On garde aussi le symbole s'il est complet ; les informations lues dans le fichier sont toujours rafraîchies.
            if (old != null && old.BrochesModele.SequenceEqual(d.BrochesModele, StringComparer.OrdinalIgnoreCase) && !old.Verifier().Any(p => p.Severity == Severity.Error)
                && !(old.Symbole.Gabarit == GabaritSymbole.Boite && d.Symbole.Gabarit != GabaritSymbole.Boite))   // boîte par défaut remplacée par le symbole reconnu
            { old.TexteSpice = d.TexteSpice; old.Parametres = d.Parametres; old.Libelles = d.Libelles; old.Notes = d.Notes; _bib.Enregistrer(old); continue; }
            if (old != null) { d.Nom = old.Nom; _bib.Definitions.Remove(old); }
            _bib.Enregistrer(d);
        }
        RebuildSpicePalette();
        var msg = defs.Count == 0 ? $"Aucun modèle importable dans {source}." : $"{defs.Count} modèle(s) importé(s) de {source} : {string.Join(", ", defs.Select(d => d.Nom))}. Ils sont rangés par catégorie dans la palette : cliquez une catégorie pour choisir la référence.";
        if (a.Avertissements.Count > 0) { msg += $" {a.Avertissements.Count} remarque(s) dans le journal."; _journal.Text = $"Import de {source}\n" + string.Join("\n", a.Avertissements.Select(w => "• " + w)) + "\n\n" + _journal.Text; }
        _status.Text = msg;
        return defs.Select(d => _bib.Definitions.First(x => string.Equals(x.Modele, d.Modele, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Ouvre l'éditeur de symbole ; à l'enregistrement, la définition est mise à jour (toutes ses instances) et ses fils suivent.</summary>
    public async Task OuvrirEditeurAsync(DefinitionComposant d)
    {
        var ed = new EditeurSymbole(d);
        await ed.ShowDialog(this);
        if (ed.Resultat is { } r) AppliquerSymbole(d, r);
    }

    public void AppliquerSymbole(DefinitionComposant d, DefinitionComposant r)
    {
        var ancien = d.Symbole; var ancienNom = d.Nom;
        d.Nom = r.Nom.Trim().Length > 0 ? r.Nom.Trim() : d.Nom; d.Symbole = r.Symbole;
        _bib.Enregistrer(d, ancienNom);
        _canvas.SymboleModifie(d, ancien);
        RebuildSpicePalette(); RefreshToolButtons();
        _status.Text = $"Symbole de {d.Nom} enregistré.";
    }

    // ───────────────────────── centre ─────────────────────────
    Control BuildCenter()
    {
        // Une seule rangée de navigation : Circuit sous test et bancs. Netlist, Vérifications et Journal dans le tiroir « Détails ».
        var schemaDock = new DockPanel();
        var barBorder = new Border { Child = new ScrollViewer { Content = _benchBar, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        DockPanel.SetDock(barBorder, Dock.Top); schemaDock.Children.Add(barBorder); _cadreSchema.Child = _canvas; schemaDock.Children.Add(_cadreSchema);
        _canvas.AddHandler(PointerPressedEvent, (_, _) => ActiverZone(Zone.Schema), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        barBorder.DoubleTapped += (_, e) => { if (e.Source is not Button && (e.Source as Visual)?.FindAncestorOfType<Button>() == null) Disposer(_disposition == Disposition.Schema ? Disposition.Mixte : Disposition.Schema); };

        var g = _centre;
        g.RowDefinitions = new("3*,5,2*,Auto");
        g.Children.Add(schemaDock); _zoneHaut = schemaDock;
        var split = new GridSplitter { Background = Ui.Line, ResizeDirection = GridResizeDirection.Rows }; Grid.SetRow(split, 1); g.Children.Add(split); _separateur = split;
        var bottom = new Border { Background = Ui.Panel, Child = BuildPlotArea() }; Grid.SetRow(bottom, 2); g.Children.Add(bottom); _zoneBas = bottom;
        var tiroir = BuildDetails(); Grid.SetRow(tiroir, 3); g.Children.Add(tiroir);
        return g;
    }

    // ───────────────────────── tiroir « Détails » ─────────────────────────
    public enum Detail { Netlist, Verifications, Journal, Campagne }
    Detail? _detailOuvert;
    readonly ContentControl _detailsContenu = new() { Height = 200, IsVisible = false };
    readonly Dictionary<Detail, Button> _boutonsDetails = new();
    public Detail? DetailOuvert => _detailOuvert;

    Control BuildDetails()
    {
        var barre = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(6, 1) };
        barre.Children.Add(new TextBlock { Text = "Détails", FontSize = Ui.Aide, FontWeight = FontWeight.SemiBold, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) });
        foreach (var (d, nom) in new[] { (Detail.Netlist, "Netlist"), (Detail.Verifications, "Vérifications"), (Detail.Journal, "Journal ngspice"), (Detail.Campagne, "Campagne") })
        {
            var b = new Button { Content = nom, FontSize = Ui.Aide, Padding = new Thickness(8, 2), Background = Brushes.Transparent, CornerRadius = new CornerRadius(4) };
            var dd = d; b.Click += (_, _) => OuvrirDetail(_detailOuvert == dd ? null : dd);
            _boutonsDetails[d] = b; barre.Children.Add(b);
        }
        var dock = new DockPanel();
        var barreB = new Border { Child = barre, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(barreB, Dock.Top); dock.Children.Add(barreB); dock.Children.Add(_detailsContenu);
        return dock;
    }

    /// <summary>Ouvre le tiroir sur un détail (null : le replie).</summary>
    public void OuvrirDetail(Detail? d)
    {
        _detailOuvert = d;
        _detailsContenu.IsVisible = d != null;
        _detailsContenu.Content = d switch
        {
            Detail.Netlist => _netlist, Detail.Verifications => new ScrollViewer { Content = _ercList }, Detail.Journal => _journal, Detail.Campagne => VueCampagne(), _ => null
        };
        foreach (var (k, b) in _boutonsDetails) Ui.SetActive(b, k == d);
    }

    // ───────────────────────── zone active (cible des loupes) ─────────────────────────
    public enum Zone { Schema, Courbes }
    Zone _zoneActive = Zone.Schema;
    public Zone ZoneActive => _zoneActive;
    public void ZoomerZoneActive(double f) => Zoomer(f);
    readonly Border _cadreSchema = new() { BorderThickness = new Thickness(0, 2, 0, 0) };

    public void ActiverZone(Zone z)
    {
        _zoneActive = z;
        _cadreSchema.BorderBrush = z == Zone.Schema ? Ui.AccentBrush : Brushes.Transparent;
        if (_zoneBas is Border bas) { bas.BorderThickness = new Thickness(0, 2, 0, 0); bas.BorderBrush = z == Zone.Courbes ? Ui.AccentBrush : Brushes.Transparent; }
    }

    void Zoomer(double f)
    {
        if (_zoneActive == Zone.Courbes && _zoneBas.IsVisible) { _plot.Plot.Axes.Zoom(f, 1); _plot.Refresh(); }   // courbes : zoom en temps ou en fréquence, comme un oscilloscope
        else _canvas.ZoomBy(f);
    }
    void ToutVoir() { if (_zoneActive == Zone.Courbes && _zoneBas.IsVisible) RefreshPlot(); else _canvas.FitAll(); }
    void ZoomFenetre()
    {
        if (_zoneActive == Zone.Courbes && _zoneBas.IsVisible) { _zoomMode = !_zoomMode; _cursorMode = false; RefreshPlotToolbar(); }
        else _canvas.Tool = Tool.ZoomWindow;
    }

    // ───────────────────────── dispositions d'écran ─────────────────────────
    public enum Disposition { Schema, Mixte, Courbes }
    Disposition _disposition = Disposition.Mixte;
    readonly Grid _centre = new();
    Control _zoneHaut = null!, _zoneBas = null!, _separateur = null!;
    readonly Dictionary<Disposition, Button> _boutonsDisposition = new();
    public Disposition DispositionActive => _disposition;

    /// <summary>Schéma seul, mixte (schéma au-dessus des courbes) ou courbes seules.</summary>
    public void Disposer(Disposition d)
    {
        _disposition = d;
        _zoneHaut.IsVisible = d != Disposition.Courbes; _zoneBas.IsVisible = d != Disposition.Schema; _separateur.IsVisible = d == Disposition.Mixte;
        _centre.RowDefinitions[0].Height = d == Disposition.Courbes ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
        _centre.RowDefinitions[1].Height = d == Disposition.Mixte ? new GridLength(5) : new GridLength(0);
        _centre.RowDefinitions[2].Height = d == Disposition.Schema ? new GridLength(0) : new GridLength(d == Disposition.Courbes ? 1 : 2, GridUnitType.Star);
        foreach (var (k, b) in _boutonsDisposition) Ui.SetActive(b, k == d);
        if (d == Disposition.Courbes) ActiverZone(Zone.Courbes);
        else if (d == Disposition.Schema) ActiverZone(Zone.Schema);
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

        _viewTran = Icones.Button(Icones.Temporel, "Vue temporelle (banc temporel)", () => AfficherFft(false), 17);
        _viewFft = Icones.Button(Icones.Fft, "Vue spectrale (FFT) du banc temporel : amplitude en dBV selon la fréquence", () => AfficherFft(true), 17);
        _viewAc = Icones.Button(Icones.Bode, "Vue Bode : suit le type du banc (réglable dans le panneau du banc)", () => { }, 17);
        var view = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        view.Children.Add(_viewTran); view.Children.Add(_viewFft); view.Children.Add(_viewAc);

        foreach (var (b, lim) in new[] { (_drawHigh, Limite.Haute), (_drawLow, Limite.Basse) })
        {
            b.Content = Icones.Make(lim == Limite.Haute ? Icones.LimiteHaute : Icones.LimiteBasse, 17, Ui.FailBrush);
            b.Padding = new Thickness(7, 4); b.Background = Brushes.Transparent; b.CornerRadius = new CornerRadius(6);
            ToolTip.SetTip(b, lim == Limite.Haute ? "Dessiner une limite haute : zone interdite au-dessus" : "Dessiner une limite basse : zone interdite en dessous");
            var l = lim; b.Click += (_, _) => StartDrawing(l);
        }
        _testTools.Children.Add(_drawHigh); _testTools.Children.Add(_drawLow); _drawHint.IsVisible = false; _testTools.Children.Add(_drawHint);   // remplacé par la puce de mode


        var curs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        _cursorBtn = Icones.Button(Icones.Curseurs, "Curseurs : cliquer sur le graphique pour poser A puis B sur le signal choisi ; glisser un curseur pour le déplacer", () => { _cursorMode = !_cursorMode; _zoomMode = false; RefreshPlotToolbar(); }, 17);
        // Plus de mode curseurs : double-clic sur une courbe, ou touches A et B sous la souris.
        curs.Children.Add(Icones.Button(Icones.Croix, "Curseurs : double-clic sur une courbe, ou touches A et B avec la souris sur le graphique ; glisser pour déplacer. Cette croix les retire tous.", () => { _cursors.Clear(); RefreshPlot(keepLimits: true); }, 14));

        var probes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        _probeBtn = Icones.Button(Icones.Sonde, "Sonde (P) : survolez le schéma pour voir l'équipotentiel, cliquez pour l'ajouter au graphique actif ; Échap pour finir", () => _canvas.Tool = _canvas.Tool == Tool.Probe ? Tool.Select : Tool.Probe, 17);
        probes.Children.Add(_probeBtn);
        probes.Children.Add(Icones.Button(Icones.Graphique, "Ajouter un graphique sous les autres (les sondes iront dedans)", () => AddPane(), 17));

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(6, 2) };
        bar.Children.Add(probes); bar.Children.Add(Ui.Separator());
        bar.Children.Add(seg); bar.Children.Add(Ui.Separator()); bar.Children.Add(view); bar.Children.Add(Ui.Separator());
        bar.Children.Add(curs); bar.Children.Add(Ui.Separator()); bar.Children.Add(_testTools);

        var dock = new DockPanel();
        _overlay.Children.Add(_zoomRect);
        Canvas.SetLeft(_puceCourbes, 8); Canvas.SetTop(_puceCourbes, 6); _overlay.Children.Add(_puceCourbes);
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar); dock.Children.Add(_panesGrid);
        RefreshPlotToolbar();
        return dock;
    }

    // ───────────────────────── graphiques empilés ─────────────────────────
    void RebuildPanes()
    {
        _panesGrid.Children.Clear(); _panesGrid.RowDefinitions.Clear(); _panes.Clear();
        var lists = _banc?.Graphiques ?? [new List<string>()];
        for (int i = 0; i < lists.Count; i++)
        {
            var plot = new AvaPlot();
            plot.Plot.FigureBackground.Color = ScottPlot.Colors.White;
            plot.AddHandler(PointerPressedEvent, OnPlotPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            plot.AddHandler(PointerMovedEvent, OnPlotMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            plot.AddHandler(PointerReleasedEvent, OnPlotReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            plot.AddHandler(PointerWheelChangedEvent, OnPlotWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            var host = new Grid(); host.Children.Add(plot);
            var frame = new Border { BorderThickness = new Thickness(2, 0, 0, 0), Margin = new Thickness(0, i == 0 ? 0 : 3, 0, 0) };
            var pane = new Pane { Signaux = lists[i], Plot = plot, Host = host, Frame = frame };
            var dock = new DockPanel();
            var header = BuildPaneHeader(pane, lists.Count > 1);
            DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
            DockPanel.SetDock(pane.Mesures, Dock.Bottom); dock.Children.Add(pane.Mesures);
            dock.Children.Add(host);
            frame.Child = dock;
            _panesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetRow(frame, i); _panesGrid.Children.Add(frame);
            _panes.Add(pane);
        }
        // Axes X liés : zoomer ou déplacer un graphique en temps (ou en fréquence) entraîne les autres.
        foreach (var a in _panes) foreach (var b in _panes) if (a != b) a.Plot.Plot.Axes.Link(b.Plot, x: true, y: false);
        Activate(_panes[Math.Clamp(_active != null ? _panes.FindIndex(p => p.Signaux == _active.Signaux) : 0, 0, _panes.Count - 1)]);
    }

    Control BuildPaneHeader(Pane pane, bool closable)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 2, 4, 0) };
        var palette = new ScottPlot.Palettes.Category10();
        for (int i = 0; i < pane.Signaux.Count; i++)
        {
            var n = pane.Signaux[i]; var c = palette.GetColor(i);
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            chip.Children.Add(new Border { Width = 10, Height = 3, Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), VerticalAlignment = VerticalAlignment.Center });
            chip.Children.Add(new TextBlock { Text = $"V({n})", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            var x = new Button { Content = Icones.Make(Icones.Croix, 9, Ui.Muted), Padding = new Thickness(2), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(x, $"Retirer V({n}) de ce graphique");
            var nn = n; x.Click += (_, e) => { e.Handled = true; pane.Signaux.Remove(nn); _cursors.RemoveAll(k => k.Noeud == nn && !_panes.Any(p => p != pane && p.Signaux.Contains(nn))); RebuildPanes(); RefreshPlot(); };
            chip.Children.Add(x);
            row.Children.Add(new Border { Child = chip, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 2, 1), Background = Ui.Surface });
        }
        if (pane.Signaux.Count == 0)
            row.Children.Add(new TextBlock { Text = "Graphique vide : activez la sonde puis cliquez sur un fil du schéma.", FontSize = 11.5, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center });
        var g = new Grid { ColumnDefinitions = new("*,Auto") };
        g.Children.Add(row);
        if (closable)
        {
            var close = Icones.Button(Icones.Croix, "Fermer ce graphique", () => { _banc?.Graphiques.Remove(pane.Signaux); if (_active == pane) _active = null; RebuildPanes(); RefreshPlot(); }, 12);
            close.Padding = new Thickness(4); Grid.SetColumn(close, 1); g.Children.Add(close);
        }
        g.PointerPressed += (_, _) => Activate(pane);
        return g;
    }

    void Activate(Pane pane)
    {
        _active = pane; _plot = pane.Plot;
        foreach (var p in _panes) p.Frame.BorderBrush = p == pane && _panes.Count > 1 ? Ui.AccentBrush : Brushes.Transparent;
        (_overlay.Parent as Panel)?.Children.Remove(_overlay); (_readout.Parent as Panel)?.Children.Remove(_readout);
        pane.Host.Children.Add(_overlay); pane.Host.Children.Add(_readout);
    }

    void AddPane()
    {
        if (_banc == null) return;
        _banc.Graphiques.Add(new List<string>());
        RebuildPanes(); Activate(_panes[^1]); RefreshPlot();
        _canvas.Tool = Tool.Probe;
        _status.Text = "Nouveau graphique : cliquez sur un fil ou une broche du schéma pour y ajouter une tension.";
    }

    void OnProbed(string node)
    {
        if (_banc == null) { _status.Text = "Choisissez un banc de test pour sonder le circuit."; return; }
        if (_active == null) return;
        if (!_active.Signaux.Contains(node)) _active.Signaux.Add(node);
        var idx = _panes.IndexOf(_active);
        RebuildPanes(); Activate(_panes[idx]); RefreshPlot();
        _status.Text = $"V({node}) ajoutée au graphique {idx + 1}" + (_last == null ? " — lancez la simulation pour la voir." : ".");
    }

    void RefreshPlotToolbar()
    {
        Ui.SetActive(_modeSim, !_modeTest); Ui.SetActive(_modeTestBtn, _modeTest);
        if (_cursorBtn != null) { Ui.SetActive(_cursorBtn, _cursorMode); Ui.SetActive(_zoomWinBtn, _zoomMode || _canvas.Tool == Tool.ZoomWindow); }
        // Puce de mode des courbes : zoom fenêtre ou tracé de gabarit, avec la façon d'en sortir.
        var mode = _zoomMode ? "Zoom fenêtre — tracez un rectangle" : _drawing == null ? null
            : _drawing.Points.Count == 0 ? $"Limite {(_drawing.Limite == Limite.Haute ? "haute" : "basse")} — cliquez près de la courbe · Échap"
            : "Clic : point suivant · double-clic ou Entrée : terminer · Échap : annuler";
        _puceCourbes.IsVisible = mode != null;
        _puceCourbes.Child = new TextBlock { Text = mode ?? "", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeight.SemiBold };
        if (_viewTran != null)
        {
            // La vue suit le type du banc : l'autre est grisée.
            Ui.SetActive(_viewTran, _banc != null && Shown == AnalyseKind.Transitoire && !VueFft); Ui.SetActive(_viewFft, VueFft); Ui.SetActive(_viewAc, _banc != null && Shown == AnalyseKind.Ac);
            _viewTran.IsEnabled = _viewFft.IsEnabled = _banc?.Type == AnalyseKind.Transitoire; _viewAc.IsEnabled = _banc?.Type == AnalyseKind.Ac;
            _viewTran.Opacity = _viewTran.IsEnabled ? 1 : 0.3; _viewFft.Opacity = _viewFft.IsEnabled ? 1 : 0.3; _viewAc.Opacity = _viewAc.IsEnabled ? 1 : 0.3;
        }
        _testTools.IsVisible = _modeTest && _banc != null && !VueFft;
        Ui.SetActive(_drawHigh, _drawing?.Limite == Limite.Haute); Ui.SetActive(_drawLow, _drawing?.Limite == Limite.Basse);
        _drawHint.Text = _drawing == null ? "" : _drawing.Points.Count == 0
            ? "Cliquez près de la courbe à contraindre pour poser le premier point (de gauche à droite)…"
            : "Cliquez pour ajouter un point · double-clic ou Entrée pour terminer · Échap pour annuler";
    }

    /// <summary>Courbe du graphique actif la plus proche du pointeur (distance verticale à l'écran à la même abscisse).</summary>
    string? CourbeProche(Point pos)
    {
        var x = ToData(pos).X; var sc = _plot.DisplayScale;
        string? best = null; double bd = double.PositiveInfinity;
        foreach (var n in ActiveSignals)
        {
            if (CurveAt(n, x) is not { } y) continue;
            var px = _plot.Plot.GetPixel(new ScottPlot.Coordinates(Shown == AnalyseKind.Ac ? Math.Log10(x) : x * 1e3, y));
            var dd = Math.Abs(px.Y / sc - pos.Y);
            if (dd < bd) { bd = dd; best = n; }
        }
        return best;
    }

    public void StartDrawing(Limite lim)
    {
        var n = ActiveSignals.FirstOrDefault();   // précisé au premier clic : courbe la plus proche
        if (n == null || _banc == null || VueFft) return;
        if (_last == null || !_last.Resultats.ContainsKey(Shown)) { _status.Text = "Simulez d'abord : le gabarit se dessine sur la courbe."; return; }
        // Temporel : gabarit accroché au front du stimulus par défaut (il suit un changement de retard).
        _drawing = new GabaritTrace { Analyse = Shown, Noeud = n, Limite = lim, Relatif = Shown == AnalyseKind.Transitoire, Ancrage = Banc.InstantFront };
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

    (double X, double Y) ToData(PointerEventArgs e) => ToData(e.GetPosition(_plot));
    (double X, double Y) ToData(Point pos)
    {
        var sc = _plot.DisplayScale;
        var c = _plot.Plot.GetCoordinates(new ScottPlot.Pixel((float)(pos.X * sc), (float)(pos.Y * sc)));
        return Shown == AnalyseKind.Ac ? (Math.Pow(10, c.X), c.Y) : (c.X * 1e-3, c.Y);
    }

    void OnPlotPressed(object? sender, PointerPressedEventArgs e)
    {
        ActiverZone(Zone.Courbes);
        if (sender is AvaPlot ap && ap != _plot && _panes.FirstOrDefault(p => p.Plot == ap) is { } pp) { Activate(pp); RefreshPlot(keepLimits: true); }
        var left = e.GetCurrentPoint(_plot).Properties.IsLeftButtonPressed;
        if (_drawing != null)
        {
            e.Handled = true;
            if (!left) { EndDrawing(false); return; }
            if (e.ClickCount > 1) { EndDrawing(true); return; }
            var p = ToData(e);
            if (_drawing.Points.Count == 0 && CourbeProche(e.GetPosition(_plot)) is { } proche) _drawing.Noeud = proche;
            if (_drawing.Points.Count > 0 && p.X <= _drawing.Points[^1].X) { _drawHint.Text = "Les points se posent de gauche à droite."; return; }
            _drawing.Points.Add(p);
            RefreshPlotToolbar(); RefreshPlot(keepLimits: true);
            return;
        }
        if (!left || _banc == null || _last == null) return;
        if (_zoomMode)
        {
            e.Handled = true; _zoomStart = e.GetPosition(_plot); _plot.UserInputProcessor.Disable(); e.Pointer.Capture(_plot);
            return;
        }
        if (VueFft) return;   // spectre : ni curseurs ni gabarits
        if (_hoverVertex is { } hv)
        {
            e.Handled = true; _dragVertex = hv; _plot.UserInputProcessor.Disable(); e.Pointer.Capture(_plot);
            return;
        }
        if (CursorAt(e.GetPosition(_plot)) is { } hc)
        {
            e.Handled = true; _dragCursor = hc; _plot.UserInputProcessor.Disable(); e.Pointer.Capture(_plot);
            return;
        }
        // Curseurs sans mode : double-clic sur la courbe (ou mode curseurs, gardé pour les tests).
        if ((_cursorMode || e.ClickCount >= 2) && CourbeProche(e.GetPosition(_plot)) is { } sig)
        {
            e.Handled = true;
            var x = ToData(e).X;
            var mine = _cursors.Where(c => c.Noeud == sig).ToList();
            Curseur c;
            if (mine.Count < 2) { c = new Curseur { Noeud = sig, Nom = mine.Any(m => m.Nom == 'A') ? 'B' : 'A' }; _cursors.Add(c); }
            else c = mine.OrderBy(m => Math.Abs(m.X - x)).First();
            c.X = ClampToCurve(c.Noeud, x);
            _dragCursor = c; _plot.UserInputProcessor.Disable(); e.Pointer.Capture(_plot);
            RefreshPlot(keepLimits: true);
        }
    }

    void OnPlotMoved(object? sender, PointerEventArgs e)
    {
        if (sender is AvaPlot ap && ap != _plot) return;
        _plotSurvol = true; _posPlot = e.GetPosition(_plot);
        if (_drawing != null)
        {
            if (_drawing.Points.Count == 0) return;
            _hover = ToData(e); RefreshPlot(keepLimits: true); return;
        }
        if (_zoomStart is { } z0)
        {
            var z1 = e.GetPosition(_plot); var r = new Rect(z0, z1).Normalize();
            Canvas.SetLeft(_zoomRect, r.X); Canvas.SetTop(_zoomRect, r.Y); _zoomRect.Width = r.Width; _zoomRect.Height = r.Height; _zoomRect.IsVisible = true;
            e.Handled = true; return;
        }
        if (_dragCursor is { } dc)
        {
            dc.X = ClampToCurve(dc.Noeud, ToData(e).X);
            RefreshPlot(keepLimits: true); e.Handled = true; return;
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
        if (_banc == null || _last == null || VueFft) return;
        var pos = e.GetPosition(_plot);
        var hit = _modeTest ? VertexAt(pos) : null;
        var hc = hit == null ? CursorAt(pos) : null;
        if (!Nullable.Equals(hit, _hoverVertex) || hc != _hoverCursor)
        {
            _hoverVertex = hit; _hoverCursor = hc;
            _plot.Cursor = hit != null ? new Cursor(StandardCursorType.SizeAll) : hc != null ? new Cursor(StandardCursorType.SizeWestEast) : _cursorMode || _zoomMode ? new Cursor(StandardCursorType.Cross) : Cursor.Default;
            RefreshPlot(keepLimits: true);
        }
        else if (_cursorMode || _zoomMode) _plot.Cursor = new Cursor(StandardCursorType.Cross);
    }

    void OnPlotReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_zoomStart is { } z0)
        {
            var z1 = e.GetPosition(_plot); _zoomStart = null; _zoomRect.IsVisible = false;
            e.Pointer.Capture(null); e.Handled = true; _plot.UserInputProcessor.Enable();
            if (Math.Abs(z1.X - z0.X) > 6 && Math.Abs(z1.Y - z0.Y) > 6)
            {
                var sc = _plot.DisplayScale;
                var a = _plot.Plot.GetCoordinates(new ScottPlot.Pixel((float)(z0.X * sc), (float)(z0.Y * sc)));
                var b = _plot.Plot.GetCoordinates(new ScottPlot.Pixel((float)(z1.X * sc), (float)(z1.Y * sc)));
                _plot.Plot.Axes.SetLimits(Math.Min(a.X, b.X), Math.Max(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
                _plot.Refresh();
            }
            _zoomMode = false; RefreshPlotToolbar();
            return;
        }
        if (_dragCursor != null)
        {
            _dragCursor = null; e.Pointer.Capture(null); e.Handled = true; _plot.UserInputProcessor.Enable();
            return;
        }
        if (_dragVertex == null) return;
        _dragVertex = null; e.Pointer.Capture(null); e.Handled = true;
        _plot.UserInputProcessor.Enable();
        RefreshGabarits(); RefreshBenchBar();
    }

    double ClampToCurve(string noeud, double x)
    {
        if (_last == null || !_last.Resultats.TryGetValue(Shown, out var raw)) return x;
        var (xs, _) = Verification.Courbe(Shown, raw, noeud);
        return Math.Clamp(x, xs[0], xs[^1]);
    }

    /// <summary>Valeur de la courbe en x (interpolation linéaire, en log f pour Bode).</summary>
    double? CurveAt(string noeud, double x)
    {
        if (_last == null || !_last.Resultats.TryGetValue(Shown, out var raw)) return null;
        if (!raw.Variables.Any(v => string.Equals(v.Name, $"v({noeud})", StringComparison.OrdinalIgnoreCase))) return null;
        var (xs, ys) = Verification.Courbe(Shown, raw, noeud);
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        int i = Array.BinarySearch(xs, x); if (i >= 0) return ys[i];
        i = ~i; bool log = Shown == AnalyseKind.Ac;
        double t = log ? Math.Log(x / xs[i - 1]) / Math.Log(xs[i] / xs[i - 1]) : (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
        return ys[i - 1] + t * (ys[i] - ys[i - 1]);
    }

    /// <summary>Phase (degrés, déroulée depuis la plus basse fréquence) de V(nœud) en f, interpolée en log f.</summary>
    double? PhaseAt(string noeud, double f)
    {
        if (_last == null || Shown != AnalyseKind.Ac || !_last.Resultats.TryGetValue(AnalyseKind.Ac, out var raw)) return null;
        if (!raw.Variables.Any(v => string.Equals(v.Name, $"v({noeud})", StringComparison.OrdinalIgnoreCase))) return null;
        var r = Verification.Frequentielle(raw, $"v({noeud})");
        var fs = r.Frequences.ToArray(); var ph = Unwrap(r.Valeurs.ToArray().Select(z => z.Phase * 180 / Math.PI).ToArray());
        if (f <= fs[0]) return ph[0];
        if (f >= fs[^1]) return ph[^1];
        int i = Array.BinarySearch(fs, f); if (i >= 0) return ph[i];
        i = ~i; double t = Math.Log(f / fs[i - 1]) / Math.Log(fs[i] / fs[i - 1]);
        return ph[i - 1] + t * (ph[i] - ph[i - 1]);
    }

    /// <summary>Curseur dont la ligne verticale passe sous le pointeur (à 6 px près).</summary>
    Curseur? CursorAt(Point pos)
    {
        var sc = _plot.DisplayScale;
        foreach (var c in _cursors.Where(c => ActiveSignals.Contains(c.Noeud)))
        {
            var px = _plot.Plot.GetPixel(new ScottPlot.Coordinates(Shown == AnalyseKind.Ac ? Math.Log10(c.X) : c.X * 1e3, 0));
            if (Math.Abs(px.X / sc - pos.X) < 6) return c;
        }
        return null;
    }

    /// <summary>Mesures automatiques par trace, sous le graphique : comme les mesures d'un oscilloscope.</summary>
    void RemplirMesures(Pane pane, Silx.Core.Raw.RawFile raw, List<string> nodes, Func<string, ScottPlot.Color> couleur)
    {
        pane.Mesures.Children.Clear();
        bool ac = Shown == AnalyseKind.Ac;
        foreach (var n in nodes)
        {
            var (xs, ys) = Verification.Courbe(Shown, raw, n);
            var c = couleur(n);
            var ligne = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 18, 0) };
            ligne.Children.Add(new Border { Width = 10, Height = 3, Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), VerticalAlignment = VerticalAlignment.Center });
            ligne.Children.Add(new TextBlock { Text = ac ? $"|V({n})|" : $"V({n})", FontWeight = FontWeight.SemiBold, FontSize = Ui.Aide });
            foreach (var (nom, val) in ac ? MesuresBode(xs, ys) : MesuresTemps(xs, ys))
                ligne.Children.Add(new TextBlock { Text = $"{nom} {val}", FontSize = Ui.Aide, Foreground = Ui.Muted, FontFamily = new FontFamily("Menlo, Consolas, monospace") });
            pane.Mesures.Children.Add(ligne);
        }
    }

    /// <summary>Min, max, crête-crête, moyenne temporelle, fréquence (passages montants par la moyenne).</summary>
    public static IEnumerable<(string, string)> MesuresTemps(double[] t, double[] v)
    {
        double min = v.Min(), max = v.Max(), aire = 0;
        for (int i = 1; i < t.Length; i++) aire += 0.5 * (v[i] + v[i - 1]) * (t[i] - t[i - 1]);
        double moy = aire / Math.Max(t[^1] - t[0], 1e-30);
        var montees = new List<double>();
        for (int i = 1; i < t.Length; i++)
            if (v[i - 1] < moy && v[i] >= moy) montees.Add(t[i - 1] + (moy - v[i - 1]) / (v[i] - v[i - 1]) * (t[i] - t[i - 1]));
        string f = montees.Count >= 3 && max - min > 1e-9 ? SpiceNumber.Format((montees.Count - 1) / (montees[^1] - montees[0]), "Hz") : "—";
        return [("min", SpiceNumber.Format(min, "V")), ("max", SpiceNumber.Format(max, "V")), ("c-c", SpiceNumber.Format(max - min, "V")), ("moy", SpiceNumber.Format(moy, "V")), ("f", f)];
    }

    /// <summary>Gain basse fréquence, fréquence de coupure −3 dB, pente sur la dernière décade.</summary>
    public static IEnumerable<(string, string)> MesuresBode(double[] f, double[] db)
    {
        string fc = "—";
        for (int i = 1; i < f.Length; i++)
            if (db[i - 1] > db[0] - 3.0103 && db[i] <= db[0] - 3.0103)
            {
                double a = Math.Log10(f[i - 1]), b = Math.Log10(f[i]), t = (db[0] - 3.0103 - db[i - 1]) / (db[i] - db[i - 1]);
                fc = SpiceNumber.Format(Math.Pow(10, a + t * (b - a)), "Hz"); break;
            }
        int j = Array.FindLastIndex(f, x => x <= f[^1] / 10);
        string pente = j >= 0 && j < f.Length - 1 ? $"{(db[^1] - db[j]) / Math.Log10(f[^1] / f[j]):0.#} dB/déc".Replace('.', ',') : "—";
        return [("gain BF", SpiceNumber.Format(db[0], "dB")), ("fc", fc), ("pente", pente)];
    }

    /// <summary>Tableau des curseurs en bas à droite : valeurs en A et B, écarts Δ (et 1/Δt en temporel).</summary>
    void RefreshReadout(Func<string, ScottPlot.Color> colorOf)
    {
        var bySignal = _cursors.Where(c => ActiveSignals.Contains(c.Noeud)).GroupBy(c => c.Noeud).ToList();
        _readout.IsVisible = bySignal.Count > 0 && _banc != null && _last != null;
        if (!_readout.IsVisible) return;
        bool ac = Shown == AnalyseKind.Ac;
        string ux = ac ? "Hz" : "s", uy = ac ? "dB" : "V";
        var g = new Grid { ColumnDefinitions = new("Auto,14,Auto,14,Auto,14,Auto,14,Auto") };
        int row = 0;
        void Cell(string t, int col, IBrush? b = null, bool bold = false, bool right = true)
        {
            var tb = new TextBlock { Text = t, FontSize = 11.5, Foreground = b ?? Ui.Ink, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, FontFamily = new FontFamily("Menlo, Consolas, monospace") };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, col); g.Children.Add(tb);
        }
        void NewRow() { g.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
        NewRow(); Cell("", 0); Cell(ac ? "f" : "t", 2, Ui.Muted); Cell(ac ? "gain" : "tension", 4, Ui.Muted); Cell(ac ? "phase" : "1/Δt", 6, Ui.Muted); if (ac) Cell("pente", 8, Ui.Muted); row++;
        foreach (var grp in bySignal)
        {
            var col = colorOf(grp.Key); var brush = new SolidColorBrush(Color.FromArgb(255, col.R, col.G, col.B));
            NewRow(); Cell(ac ? $"|V({grp.Key})|" : $"V({grp.Key})", 0, brush, bold: true, right: false); row++;
            foreach (var c in grp.OrderBy(c => c.Nom))
            {
                NewRow(); Cell($"  {c.Nom}", 0, brush, right: false); Cell(SpiceNumber.Format(c.X, ux), 2);
                Cell(CurveAt(c.Noeud, c.X) is { } y ? SpiceNumber.Format(y, uy) : "—", 4);
                if (ac) Cell(PhaseAt(c.Noeud, c.X) is { } ph ? $"{ph:0.0}°".Replace('.', ',') : "—", 6);
                row++;
            }
            var ab = grp.OrderBy(c => c.Nom).ToList();
            if (ab.Count == 2 && CurveAt(ab[0].Noeud, ab[0].X) is { } ya && CurveAt(ab[1].Noeud, ab[1].X) is { } yb)
            {
                double dx = ab[1].X - ab[0].X, dy = yb - ya;
                NewRow(); Cell("  Δ B−A", 0, Ui.AccentBrush, bold: true, right: false);
                Cell(ac ? $"×{ab[1].X / ab[0].X:0.###}" : SpiceNumber.Format(dx, ux), 2, Ui.AccentBrush, bold: true);
                Cell(SpiceNumber.Format(dy, uy), 4, Ui.AccentBrush, bold: true);
                if (ac)
                {
                    if (PhaseAt(ab[0].Noeud, ab[0].X) is { } pa && PhaseAt(ab[1].Noeud, ab[1].X) is { } pb) Cell($"{pb - pa:+0.0;-0.0;0}°".Replace('.', ','), 6, Ui.AccentBrush, bold: true);
                    Cell(Math.Abs(Math.Log10(ab[1].X / ab[0].X)) > 1e-9 ? $"{dy / Math.Log10(ab[1].X / ab[0].X):0.#} dB/déc".Replace('.', ',') : "", 8, Ui.AccentBrush, bold: true);
                }
                else Cell(Math.Abs(dx) > 0 ? SpiceNumber.Format(1 / Math.Abs(dx), "Hz") : "—", 6, Ui.AccentBrush, bold: true);
                row++;
            }
        }
        _readout.Child = g;
    }

    /// <summary>Point de gabarit sous le pointeur (à 9 px près), dans la vue affichée.</summary>
    (GabaritTrace, int)? VertexAt(Point pos)
    {
        var sc = _plot.DisplayScale;
        foreach (var g in Banc.TracesActives.Where(g => g.Analyse == Shown && ActiveSignals.Contains(g.Noeud)))
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
        // Inspecteur contextuel : la sélection, sinon le plan de test du banc (ou la vue d'ensemble des bancs).
        _blocProps.Children.Add(Ui.Title("Propriétés")); _blocProps.Children.Add(_props);
        s.Children.Add(_blocProps);
        s.Children.Add(_circuitPanel); s.Children.Add(_benchPanel);
        return _right = new Border { Width = 310, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = s } };
    }

    /// <summary>Panneau droit selon le contexte : vue d'ensemble des bancs (circuit) ou réglages du banc actif.</summary>
    void RefreshSide()
    {
        bool sel = _canvas.Selection.Count + _canvas.FilsSelectionnes.Count > 0;
        _circuitPanel.IsVisible = !sel && _banc == null; _benchPanel.IsVisible = !sel && _banc != null;
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
                var btn = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8) };
                btn.Click += (_, _) => SelectBanc(bb);
                _circuitPanel.Children.Add(btn);
            }
            RefreshGabarits();
            return;
        }
        // Plan de test du banc : une ligne par contrainte (critère mesuré ou gabarit dessiné), verdict et marge.
        var gh = new Grid { ColumnDefinitions = new("*,Auto") };
        var titre = Ui.Title("Plan de test"); gh.Children.Add(titre);
        var add = Ui.ToolButton("+ Critère", "Ajouter un critère mesuré (temps de montée, bande passante…)", () =>
        {
            var n = Connectivity.Build(SchemaCourant).NamedNodes.FirstOrDefault() ?? "OUT";
            var c = new CritereGabarit { Mesure = Banc.Type == AnalyseKind.Ac ? TypeMesure.BandePassante3dB : TypeMesure.TempsDeMontee, Noeud = n };
            Banc.Gabarits.Add(c); _ligneOuverte = c; RefreshGabarits();
        });
        Grid.SetColumn(add, 1); gh.Children.Add(add);
        _benchPanel.Children.Add(gh);
        _benchPanel.Children.Add(_resumePlan);
        _benchPanel.Children.Add(_gabarits);
        _benchPanel.Children.Add(Ui.Hint("Gabarits dessinés : sous les courbes, « Limite haute » ou « Limite basse ». Réglages du banc (nom, analyse) : roue dentée de sa pastille."));
        _benchPanel.Children.Add(Ui.Rule());
        _benchPanel.Children.Add(ValeursCircuit());
        RefreshGabarits();
    }

    /// <summary>Nouveau banc depuis un modèle (échelon, sinus, Bode, vide) : choix de l'entrée et de la sortie, puis création.</summary>
    void ProposerNouveauBanc(Control ancre)
    {
        var conn = Connectivity.Build(_etude.Schema);
        var noeuds = conn.AllNodes.ToList();
        var sp = new StackPanel { Spacing = 6, Width = 300, Margin = new Thickness(4) };
        var t = Ui.Title("Nouveau banc de test"); t.Foreground = Ui.BenchBrush; sp.Children.Add(t);
        var entree = new ComboBox { ItemsSource = noeuds, SelectedItem = noeuds.FirstOrDefault(n => n.Equals("IN", StringComparison.OrdinalIgnoreCase)) ?? conn.NamedNodes.FirstOrDefault() ?? noeuds.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var sortie = new ComboBox { ItemsSource = noeuds, SelectedItem = noeuds.FirstOrDefault(n => n.Equals("OUT", StringComparison.OrdinalIgnoreCase)) ?? conn.NamedNodes.LastOrDefault() ?? noeuds.LastOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var g = new Grid { ColumnDefinitions = new("*,8,*") };
        g.Children.Add(Ui.Labeled("Entrée (stimulus)", entree)); var so = Ui.Labeled("Sortie (critère)", sortie); Grid.SetColumn(so, 2); g.Children.Add(so);
        sp.Children.Add(g);
        var fly = new Flyout { Content = sp, Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var m in Enum.GetValues<ModeleBanc>())
        {
            var mm = m;
            var b = new Button { Content = ModelesBanc.Libelle(m), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 5), Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
            b.Click += (_, _) => { fly.Hide(); CreerBanc(mm, entree.SelectedItem as string, sortie.SelectedItem as string); };
            sp.Children.Add(b);
        }
        sp.Children.Add(Ui.Hint("La source est posée sur l'entrée, référencée à la masse ; le premier critère mesure la sortie. Ajoutez ensuite ses bornes dans le plan de test."));
        fly.ShowAt(ancre);
    }

    public Banc CreerBanc(ModeleBanc m, string? entree, string? sortie)
    {
        var nom = m switch { ModeleBanc.Echelon => "Échelon", ModeleBanc.Sinus => "Sinus", ModeleBanc.Bode => "Bode", _ => "Banc" };
        int i = 1; var n = nom; while (_etude.Bancs.Any(b => b.Nom == n)) n = $"{nom} {++i}";
        var banc = ModelesBanc.Creer(_etude, m, entree, sortie, n);
        _etude.Bancs.Add(banc); SelectBanc(banc);
        if (m != ModeleBanc.Vide && !banc.Calque.Components.Any()) _status.Text = "Pas de place libre pour poser la source près de l'entrée : posez-la à la main.";
        else if (m != ModeleBanc.Vide) { _status.Text = $"Banc « {n} » créé : source sur {entree}."; _ = SimulateAsync(); }
        return banc;
    }

    /// <summary>Bulle des réglages du banc actif, ouverte depuis la roue dentée de sa pastille.</summary>
    public void OuvrirReglagesBanc(Control ancre)
    {
        if (_banc == null) return;
        var sp = new StackPanel { Spacing = 6, Width = 300, Margin = new Thickness(4) };
        var t = Ui.Title("Banc de test"); t.Foreground = CouleurDe(Banc); sp.Children.Add(t);
        var nom = new TextBox { Text = Banc.Nom, FontSize = 13 };
        void Renommer() { var n = (nom.Text ?? "").Trim(); if (n.Length > 0 && n != Banc.Nom) { Banc.Nom = n; RefreshBenchBar(); _canvas.Caption = $"Banc « {n} »"; _canvas.InvalidateVisual(); } }
        nom.KeyDown += (_, e) => { if (e.Key == Key.Enter) Renommer(); }; nom.LostFocus += (_, _) => Renommer();
        sp.Children.Add(Ui.Labeled("Nom", nom));
        sp.Children.Add(Ui.Labeled("Couleur du banc", NuancierBanc(t)));
        sp.Children.Add(Ui.Rule());
        sp.Children.Add(Ui.Title("Analyse"));
        sp.Children.Add(BuildAnalysisPanel());
        sp.Children.Add(Ui.Rule());
        sp.Children.Add(Ui.Title("Campagne"));
        sp.Children.Add(EditeurCampagne());
        var fly = new Flyout { Content = sp, Placement = PlacementMode.BottomEdgeAlignedLeft };
        _bulleBanc = fly;
        fly.ShowAt(ancre);
    }
    Flyout? _bulleBanc;

    /// <summary>Couleur d'un banc : la sienne, sinon celle des paramètres.</summary>
    public IBrush CouleurDe(Banc b) => b.Couleur is { } c && Color.TryParse(c, out var col) ? new SolidColorBrush(col) : Ui.BenchBrush;

    /// <summary>Change la couleur du banc actif (null = couleur des paramètres) : pastille, calque et explorateur suivent.</summary>
    public void ColorerBanc(string? couleur)
    {
        if (_banc == null) return;
        Banc.Couleur = couleur;
        _canvas.CouleurCalque = CouleurDe(Banc); _canvas.InvalidateVisual(); RefreshBenchBar();
    }

    Control NuancierBanc(TextBlock titre)
    {
        var sp = new WrapPanel { Orientation = Orientation.Horizontal };
        void Remplir()
        {
            sp.Children.Clear();
            var auto = new Button { Content = "Par défaut", FontSize = Ui.Aide, Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(Banc.Couleur == null ? 2 : 1), BorderBrush = Banc.Couleur == null ? Ui.Ink : Ui.Line, Background = Ui.Surface };
            ToolTip.SetTip(auto, "Couleur des bancs choisie dans Paramètres › Affichage");
            auto.Click += (_, _) => { ColorerBanc(null); titre.Foreground = CouleurDe(Banc); Remplir(); };
            sp.Children.Add(auto);
            foreach (var n in FenetreParametres.Nuancier)
            {
                bool on = string.Equals(n, Banc.Couleur, StringComparison.OrdinalIgnoreCase);
                var b = new Button { Width = 20, Height = 20, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.Parse(n)),
                    BorderBrush = on ? Ui.Ink : Brushes.Transparent, BorderThickness = new Thickness(on ? 2 : 0) };
                var nn = n; b.Click += (_, _) => { ColorerBanc(nn); titre.Foreground = CouleurDe(Banc); Remplir(); };
                sp.Children.Add(b);
            }
        }
        Remplir();
        return sp;
    }
    public bool ReglagesBancOuverts => _bulleBanc?.IsOpen == true;

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
            row.Children.Add(Icones.Make(icon, 15, Banc.Type == k ? Ui.AccentBrush : Ui.Muted)); row.Children.Add(new TextBlock { Text = label, FontSize = Ui.Texte, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = row, Padding = new Thickness(10, 4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Banc.Type == k ? Ui.AccentBrush : Ui.Line, Background = Banc.Type == k ? Ui.ActiveBg : Ui.Surface };
            ToolTip.SetTip(b, k == AnalyseKind.Ac ? "Banc fréquentiel : analyse .ac, courbes en Bode" : "Banc temporel : analyse .tran, courbes en fonction du temps");
            var kk = k; b.Click += (_, _) => ShowAnalysis(kk);
            type.Children.Add(b);
        }
        if (Banc.PremierStimulus is { } src)
        {
            // Le type est fixé par le premier stimulus : on l'affiche, sans le proposer au choix.
            var st = Stimulus.Lire(src.Value);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(Icones.Make(Banc.Type == AnalyseKind.Ac ? Icones.Bode : Icones.Temporel, 15, Ui.AccentBrush));
            row.Children.Add(new TextBlock { Text = Banc.Type == AnalyseKind.Ac ? "Fréquentiel" : "Temporel", FontWeight = FontWeight.SemiBold, FontSize = Ui.Texte, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = $"· fixé par {src.Ref} ({Stimulus.Libelle(st.Forme).ToLowerInvariant()})", FontSize = 12, Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center });
            ToolTip.SetTip(row, "Le premier stimulus posé dans le banc décide : petit signal (AC) → fréquentiel ; continu, impulsion, sinus → temporel.");
            s.Children.Add(row);
        }
        else s.Children.Add(type);
        if (Banc.Type == AnalyseKind.Transitoire) { s.Children.Add(Ui.SubTitle("Transitoire (.tran)")); s.Children.Add(tran); }
        else { s.Children.Add(Ui.SubTitle("Petit signal (.ac, décade)")); s.Children.Add(ac); }
        return s;
    }

    /// <summary>Panneau où s'écrivent les propriétés : le panneau droit, ou la bulle d'édition sur place.</summary>
    StackPanel _cible = null!;
    Flyout? _bulle; StackPanel? _bulleContenu; PlacedComponent? _bulleComposant;
    /// <summary>Composant dont on construit les propriétés ; <see cref="_bulleCircuit"/> : composant du circuit sous test édité depuis un banc.</summary>
    PlacedComponent? _composantCible; bool _bulleCircuit;

    readonly StackPanel _blocProps = new() { Spacing = 6 };

    void RefreshProperties()
    {
        _cible = _props; _composantCible = _canvas.Selected;
        Proprietes();
        bool sel = _canvas.Selection.Count + _canvas.FilsSelectionnes.Count > 0;
        _blocProps.IsVisible = sel;
        _circuitPanel.IsVisible = !sel && _banc == null; _benchPanel.IsVisible = !sel && _banc != null;
        // Bulle d'édition ouverte : reconstruite si le composant est toujours sélectionné, fermée sinon.
        if (_bulle?.IsOpen == true && _bulleContenu != null)
        {
            if (!_bulleCircuit && _canvas.Selected != _bulleComposant) { _bulle.Hide(); return; }
            _cible = _bulleContenu; _composantCible = _bulleComposant; Proprietes(); _cible = _props; _composantCible = _canvas.Selected;
        }
    }

    /// <summary>Édition sur place : double-clic sur un composant → bulle avec ses propriétés, près du pointeur.</summary>
    public void EditerSurPlace(PlacedComponent c) => OuvrirBulle(c, circuit: false);

    /// <summary>Depuis un banc : double-clic sur un composant du circuit sous test (atténué) pour le modifier sans quitter le banc.</summary>
    public void EditerCircuitDepuisBanc(PlacedComponent c) => OuvrirBulle(c, circuit: true);

    void OuvrirBulle(PlacedComponent c, bool circuit)
    {
        if (!circuit && _canvas.Selected != c) return;
        _bulleComposant = c; _bulleCircuit = circuit;
        _bulleContenu = new StackPanel { Spacing = 6, Width = 280, Margin = new Thickness(4) };
        _cible = _bulleContenu; _composantCible = c; Proprietes(); _cible = _props; _composantCible = _canvas.Selected;
        _bulle = new Flyout { Content = _bulleContenu, Placement = PlacementMode.Pointer, ShowMode = FlyoutShowMode.Standard };
        _bulle.ShowAt(_canvas, showAtPointer: true);
        // Première zone de saisie active : on tape directement la nouvelle valeur.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var tb = _bulleContenu.GetLogicalDescendants().OfType<TextBox>().Skip(c.Kind is ComponentKind.Label ? 0 : 1).FirstOrDefault();
            tb?.Focus(); tb?.SelectAll();
        });
    }
    public bool BulleOuverte => _bulle?.IsOpen == true;
    public void ModifierComposantDeLaBulle(string reference, string valeur) { if (_bulleComposant != null) { _composantCible = _bulleComposant; EditerSelection(reference, valeur); _composantCible = _canvas.Selected; } }

    /// <summary>Vrai pendant la reconstruction d'un panneau : les zones de saisie retirées perdent le focus et ne doivent rien réécrire.</summary>
    bool _reconstruction;
    /// <summary>
    /// Depuis un banc : valeurs des composants du circuit sous test, modifiables (on est en conception) ;
    /// la topologie du circuit reste verrouillée. La modification vaut pour tous les bancs.
    /// </summary>
    Control ValeursCircuit()
    {
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(Ui.Title("Valeurs du circuit"));
        var modifiables = _etude.Schema.Components.Where(k => k.Kind is ComponentKind.Resistor or ComponentKind.Capacitor or ComponentKind.Inductor or ComponentKind.Diode
                                                            or ComponentKind.VoltageSource or ComponentKind.CurrentSource or ComponentKind.Spice)
            .OrderBy(k => k.Ref, StringComparer.Ordinal).ToList();
        if (modifiables.Count == 0) { sp.Children.Add(Ui.Hint("Le circuit sous test n'a pas de valeur à régler.")); return sp; }
        foreach (var k in modifiables)
        {
            var g = new Grid { ColumnDefinitions = new("60,*") };
            g.Children.Add(new TextBlock { Text = k.Ref, FontWeight = FontWeight.SemiBold, FontSize = Ui.Texte, VerticalAlignment = VerticalAlignment.Center });
            var t = new TextBox { Text = k.Value, FontSize = 12, PlaceholderText = k.Kind == ComponentKind.Spice ? "paramètres d'instance" : "" };
            var kk = k;
            void Ok() { if (!_reconstruction && (t.Text ?? "") != kk.Value) ModifierValeurCircuit(kk, t.Text ?? ""); }
            t.KeyDown += (_, e) => { if (e.Key == Key.Enter) Ok(); }; t.LostFocus += (_, _) => Ok();
            Grid.SetColumn(t, 1); g.Children.Add(t);
            sp.Children.Add(g);
        }
        sp.Children.Add(Ui.Hint("Modifiables depuis chaque banc, pour la mise au point : la valeur change pour tous les bancs. Un clic sur un composant estompé du schéma ouvre aussi sa bulle. Le câblage du circuit, lui, se modifie depuis « Circuit sous test »."));
        return sp;
    }

    /// <summary>Change la valeur d'un composant du circuit sous test sans quitter le banc (vaut pour tous les bancs).</summary>
    public void ModifierValeurCircuit(PlacedComponent c, string valeur)
    {
        if (c.Value == valeur) return;
        c.Value = valeur;
        _results.Clear(); SetVerdict(null); _canvas.InvalidateVisual();
        RefreshGabarits(); RefreshBenchBar();
        _status.Text = $"{c.Ref} = {valeur} dans le circuit sous test (tous les bancs).";
        ResultatsPerimes();
        if (_bulle?.IsOpen == true && _bulleContenu != null && _bulleComposant == c) { _cible = _bulleContenu; Proprietes(); _cible = _props; }
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshSide);   // liste « Valeurs du circuit » à jour, après la fin de la saisie
    }

    void EditerSelection(string reference, string valeur)
    {
        if (_reconstruction) return;
        if (_bulleCircuit && _composantCible is { } c && c != _canvas.Selected)
        {
            // Composant du circuit sous test modifié depuis un banc : vaut pour tous les bancs, résultats à refaire.
            if (c.Ref == reference.Trim() && c.Value == valeur) return;
            c.Ref = reference.Trim(); c.Value = valeur;
            _results.Clear(); SetVerdict(null); _canvas.InvalidateVisual();
            RefreshGabarits(); RefreshBenchBar(); RefreshPlot();
            _status.Text = $"{c.Ref} modifié dans le circuit sous test.";
            ResultatsPerimes();
            if (_bulle?.IsOpen == true && _bulleContenu != null) { _cible = _bulleContenu; Proprietes(); _cible = _props; }
            return;
        }
        _canvas.EditSelected(reference, valeur);
    }

    void Proprietes()
    {
        _reconstruction = true;
        try { ProprietesInterne(); } finally { _reconstruction = false; }
    }

    void ProprietesInterne()
    {
        _cible.Children.Clear();
        var c = _composantCible;
        if (_bulleCircuit && c != null && _cible != _props && c != _canvas.Selected)
            _cible.Children.Add(new TextBlock { Text = "Circuit sous test : la modification vaut pour tous les bancs.", FontSize = 11.5, Foreground = Ui.AccentBrush, TextWrapping = TextWrapping.Wrap });
        if (c == null)
        {
            int n = _canvas.Selection.Count, nf = _canvas.FilsSelectionnes.Count;
            _cible.Children.Add(new TextBlock { Text = n + nf > 1
                ? $"{n} composant(s) et {nf} fil(s) sélectionnés : glisser pour déplacer, ⌘R pour pivoter, Suppr pour effacer. Maj+clic ajoute ou retire un élément."
                : "Sélectionnez un composant sur le schéma pour modifier sa référence et sa valeur. Glissez depuis le fond pour sélectionner plusieurs éléments.", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, FontSize = 12 });
            return;
        }
        var kindName = c.Kind switch
        {
            ComponentKind.Resistor => "Résistance", ComponentKind.Capacitor => "Condensateur", ComponentKind.Inductor => "Inductance",
            ComponentKind.VoltageSource => "Source de tension", ComponentKind.CurrentSource => "Source de courant",
            ComponentKind.Diode => "Diode", ComponentKind.Ground => "Masse", _ => "Étiquette de nœud"
        };
        if (c.Kind == ComponentKind.Spice) { ProprietesSpice(c); return; }
        _cible.Children.Add(new TextBlock { Text = kindName, FontWeight = FontWeight.SemiBold });
        if (c.Kind == ComponentKind.Ground) { _cible.Children.Add(Ui.Hint("Nœud 0 de référence. Toutes les masses sont reliées.")); return; }

        var refBox = new TextBox { Text = c.Ref };
        if (c.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource) { ProprietesStimulus(c, refBox); return; }
        Control valueEditor;
        Func<string> value;
        if (c.Kind == ComponentKind.Diode)
        {
            var cb = new ComboBox { ItemsSource = DiodeModels.All.Keys.ToList(), SelectedItem = DiodeModels.All.Keys.FirstOrDefault(k => k.Equals(c.Value, StringComparison.OrdinalIgnoreCase)), HorizontalAlignment = HorizontalAlignment.Stretch };
            cb.SelectionChanged += (_, _) => EditerSelection(refBox.Text ?? c.Ref, cb.SelectedItem as string ?? c.Value);
            valueEditor = cb; value = () => cb.SelectedItem as string ?? c.Value;
        }
        else
        {
            var vb = new TextBox { Text = c.Value };
            vb.KeyDown += (_, e) => { if (e.Key == Key.Enter) EditerSelection(refBox.Text ?? c.Ref, vb.Text ?? ""); };
            vb.LostFocus += (_, _) => EditerSelection(refBox.Text ?? c.Ref, vb.Text ?? "");
            valueEditor = vb; value = () => vb.Text ?? "";
        }
        refBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) EditerSelection(refBox.Text ?? c.Ref, value()); };
        refBox.LostFocus += (_, _) => EditerSelection(refBox.Text ?? c.Ref, value());

        if (c.Kind != ComponentKind.Label) { _cible.Children.Add(Ui.Label("Référence")); _cible.Children.Add(refBox); }
        _cible.Children.Add(Ui.Label(c.Kind switch { ComponentKind.Label => "Nom du nœud", ComponentKind.Diode => "Modèle", _ => "Valeur" }));
        _cible.Children.Add(valueEditor);
        _cible.Children.Add(Ui.Hint(c.Kind switch
        {
            ComponentKind.Resistor => "Ex. 4,7k · 10meg · 220",
            ComponentKind.Capacitor => "Ex. 100n · 4,7u · 22p",
            ComponentKind.Inductor => "Ex. 10u · 1m",
            ComponentKind.VoltageSource => "Syntaxe SPICE : DC 5 · AC 1 · PULSE(v1 v2 td tr tf pw per) · SIN(vo va f). « AC 1 » sert à l'analyse AC.",
            ComponentKind.CurrentSource => "Courant de la broche 1 vers la broche 2 dans la source. Ex. DC 1m · AC 1",
            ComponentKind.Diode => "Modèles génériques intégrés (indicatifs), en attendant les bibliothèques fabricant.",
            _ => "Le nœud prend ce nom dans la netlist, les courbes et les gabarits. Deux étiquettes identiques relient leurs nœuds."
        }));
        if (Campagnes.Variable(c)) _cible.Children.Add(ChampTolerance(c));
    }

    /// <summary>Tolérance du composant (± %) pour les campagnes Monte-Carlo et coins ; vide = défaut du type (Paramètres).</summary>
    Control ChampTolerance(PlacedComponent c)
    {
        var defaut = Campagnes.ToleranceParDefaut(c.Kind);
        return Ui.Field("Tolérance (± %)", c.Tolerance is { } t ? t.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture) : "", v =>
        {
            if (_reconstruction) return;
            v = v.Replace("%", "").Replace("±", "").Trim();
            double? n = v.Length == 0 ? null : SpiceNumber.ParseOrNull(v);
            if (v.Length > 0 && n is not >= 0) return;
            if (n == c.Tolerance) return;
            c.Tolerance = n; OublierCampagne(); RefreshGabarits();
            _status.Text = $"{c.Ref} : tolérance {(n is { } x ? $"± {x:0.###} %" : defaut is { } d ? $"par défaut (± {d:0.###} %)" : "aucune")}.";
        }, defaut is { } dd ? $"{dd:0.###} (défaut du type)" : "aucune (défaut)");
    }

    /// <summary>Source : forme du stimulus et champs en clair (notation ingénieur) ; l'expression SPICE est générée.</summary>
    void ProprietesStimulus(PlacedComponent c, TextBox refBox)
    {
        bool courant = c.Kind == ComponentKind.CurrentSource;
        var st = Stimulus.Lire(c.Value);
        void Appliquer() => EditerSelection(refBox.Text ?? c.Ref, st.VersSpice());
        refBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Appliquer(); }; refBox.LostFocus += (_, _) => Appliquer();
        _cible.Children.Add(Ui.Label("Référence")); _cible.Children.Add(refBox);
        var formes = Enum.GetValues<FormeStimulus>().ToList();
        var forme = new ComboBox { ItemsSource = formes.Select(Stimulus.Libelle).ToList(), SelectedIndex = formes.IndexOf(st.Forme), HorizontalAlignment = HorizontalAlignment.Stretch };
        forme.SelectionChanged += (_, _) =>
        {
            if (forme.SelectedIndex < 0 || formes[forme.SelectedIndex] == st.Forme) return;
            var f = formes[forme.SelectedIndex];
            st = f == FormeStimulus.Expression ? new Stimulus { Forme = f, Expression = st.VersSpice() } : Stimulus.Nouveau(f);
            Appliquer();
        };
        _cible.Children.Add(Ui.Label("Forme")); _cible.Children.Add(forme);
        if (st.Forme == FormeStimulus.Expression)
        {
            var ex = new TextBox { Text = st.Expression, FontFamily = new FontFamily("Menlo, Consolas, monospace"), FontSize = 12 };
            void Ex() { st.Expression = ex.Text ?? ""; Appliquer(); }
            ex.KeyDown += (_, e) => { if (e.Key == Key.Enter) Ex(); }; ex.LostFocus += (_, _) => Ex();
            _cible.Children.Add(Ui.Label("Expression SPICE")); _cible.Children.Add(ex);
            _cible.Children.Add(Ui.Hint("Pour les formes non prévues (PWL, EXP…). Ex. PWL(0 0 1m 5)."));
        }
        else
        {
            var grid = new Grid { ColumnDefinitions = new("*,8,*") };
            var champs = Stimulus.Champs(st.Forme);
            for (int i = 0; i < champs.Count; i++)
            {
                var ch = champs[i]; var unite = courant && ch.Unite == "V" ? "A" : ch.Unite;
                if (i % 2 == 0) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var f = Ui.Field($"{ch.Libelle} ({unite})", SpiceNumber.Format(st[ch.Cle], unite), v => { if (SpiceNumber.TryParse(v, out var x) && x != st[ch.Cle]) { st[ch.Cle] = x; Appliquer(); } });
                f.Margin = new Thickness(0, 0, 0, 4);
                Grid.SetRow(f, i / 2); Grid.SetColumn(f, i % 2 == 0 ? 0 : 2); grid.Children.Add(f);
            }
            _cible.Children.Add(grid);
            _cible.Children.Add(Ui.Hint((st.Forme == FormeStimulus.PetitSignal ? "Source petit signal : le banc devient fréquentiel (Bode) si c'est son premier stimulus. "
                : "Le banc est temporel si c'est son premier stimulus. ") + "Valeurs en notation ingénieur : 4,7k · 100n · 1meg." + (courant ? " Courant de la broche 1 vers la broche 2 dans la source." : "")));
        }
        _cible.Children.Add(new TextBlock { Text = "SPICE : " + st.VersSpice(), FontSize = 11, Foreground = Ui.Muted, FontFamily = new FontFamily("Menlo, Consolas, monospace"), TextWrapping = TextWrapping.Wrap });
        if (st.Forme == FormeStimulus.Continu) _cible.Children.Add(ChampTolerance(c));
    }

    void ProprietesSpice(PlacedComponent c)
    {
        var d = c.Definition;
        _cible.Children.Add(new TextBlock { Text = d == null ? "Composant SPICE" : $"{d.Nom} · {d.Description}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var refBox = new TextBox { Text = c.Ref };
        var vb = new TextBox { Text = c.Value, PlaceholderText = d?.Type == TypeModele.SousCircuit ? d.Parametres : "" };
        void Commit() => EditerSelection(refBox.Text ?? c.Ref, vb.Text ?? "");
        foreach (var t in new[] { refBox, vb }) { t.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); }; t.LostFocus += (_, _) => Commit(); }
        _cible.Children.Add(Ui.Label("Référence")); _cible.Children.Add(refBox);
        _cible.Children.Add(Ui.Label("Paramètres d'instance (facultatifs)")); _cible.Children.Add(vb);
        if (d == null) return;
        _cible.Children.Add(Ui.Hint($"Ligne SPICE : {c.Ref} {string.Join(" ", d.BrochesModele)} {d.Modele} [paramètres]. "
            + d.Type switch
            {
                TypeModele.Nmos or TypeModele.Pmos => "Ex. W=2u L=180n.",
                TypeModele.SousCircuit => d.Parametres.Length > 0 ? $"Par défaut : {d.Parametres}." : "",
                _ => "Ex. area=2."
            }));
        var edit = new Button { Padding = new Thickness(8, 4), HorizontalAlignment = HorizontalAlignment.Left };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(Icones.Make(Icones.EditerSymbole, 15)); sp.Children.Add(new TextBlock { Text = "Modifier le symbole", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        edit.Content = sp; ToolTip.SetTip(edit, "Dessin et correspondance des broches ; s'applique à tous les composants de ce modèle");
        edit.Click += async (_, _) => await OuvrirEditeurAsync(d);
        _cible.Children.Add(edit);
    }

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
        var nodes = Connectivity.Build(SchemaCourant).AllNodes.ToList();
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
            var ligne = LignePlan(VerdictPlan(g, res?.Verdict), $"{g.Info.Nom} · V({g.Noeud})", $"{bornes} · {mesure}{StatCritere(g)}", g, () => { Banc.Gabarits.Remove(g); Invalidate(); }, "Retirer ce critère");
            _gabarits.Children.Add(ligne);
            if (_ligneOuverte == g) _gabarits.Children.Add(EditeurCritere(g, nodes));
        }
        foreach (var g in Banc.TracesActives.ToList())
        {
            var res = _last?.Traces.FirstOrDefault(t => t.Gabarit == g)?.Resultat;
            string detail = res == null ? $"{g.Points.Count} points · pas encore simulé" : res.Marge is { } m
                ? (m >= 0 ? $"marge {SpiceNumber.Format(m, g.Unite)} à {Fx(g.Analyse, res.XPire!.Value)}" : $"franchi de {SpiceNumber.Format(-m, g.Unite)} à {Fx(g.Analyse, res.XPire!.Value)}")
                : res.Detail ?? "non évaluable";
            var titre = $"Gabarit {(g.Limite == Limite.Haute ? "haut" : "bas")} · {(g.Analyse == AnalyseKind.Ac ? $"|V({g.Noeud})|" : $"V({g.Noeud})")}";
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
                .Select(g => new ResultatCritere(g, Verification.Evaluer(g, r.Resultats[g.Info.Analyse]))).ToList();
            _last = r with { Criteres = crit };
            SetVerdict(_last.Nombre == 0 ? null : _last.Global);
        }
        else SetVerdict(null);
        RefreshGabarits(); RefreshBenchBar(); RefreshPlot(keepLimits: true);
    }

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
        if (_boutonsDetails.TryGetValue(Detail.Verifications, out var bv)) { bv.Content = nErr + nWarn == 0 ? "Vérifications ✓" : $"Vérifications ({nErr + nWarn})"; bv.Foreground = nErr > 0 ? Ui.FailBrush : nWarn > 0 ? Ui.WarnBrush : Ui.Ink; }
        // Circuit modifié : tous les bancs sont à relancer. Calque modifié : seulement le banc actif.
        bool had = !_chargement && (_banc == null ? _results.Count > 0 : _last != null);
        if (!_chargement) { if (_banc == null) _results.Clear(); else _last = null; }
        if (had) { SetVerdict(null); RefreshGabarits(); RefreshPlot(keepLimits: true); RefreshBenchBar(); if (_banc == null) RefreshSide(); }
        if (!_chargement && (had || _last == null)) ResultatsPerimes();
        if (_banc != null && _banc.Type != _typeAffiche) { _typeAffiche = _banc.Type; _cursors.Clear(); RefreshSide(); RefreshBenchBar(); RefreshPlot(); }
        RefreshPlotToolbar();
        RefreshProperties();
    }

    // ───────────────────────── simulation ─────────────────────────
    public async Task SimulateAsync() => await SimulerAsync(keepLimits: false);

    async Task SimulerAsync(bool keepLimits, bool campagne = true)
    {
        _cts?.Cancel(); _cts = new();
        _simulate.IsEnabled = false; _simEnCours = true; MajEtatResultats();
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
                _dureeDerniere = sw.ElapsedMilliseconds;
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
                OuvrirDetail(res.Erc.Any(i => i.Severity == Severity.Error) ? Detail.Verifications : Detail.Journal);
                SetVerdict(null);
            }
            else
            {
                _status.Text = $"« {Banc.Nom} » simulé en {sw.ElapsedMilliseconds} ms ({string.Join(" + ", res.Resultats.Keys.Select(k => k == AnalyseKind.Ac ? "AC" : "transitoire"))}) · {res.Respectes}/{res.Nombre} contrainte(s) respectée(s)";
                SetVerdict(res.Nombre == 0 ? null : res.Global);
            }
            RefreshGabarits(); RefreshPlot(keepLimits); RefreshBenchBar();
            _dureeDerniere = sw.ElapsedMilliseconds;
            // Campagne du banc (balayage, Monte-Carlo, coins) : après le nominal, sur demande explicite seulement.
            if (campagne && res.Erreur == null && Banc.Campagne.Mode != ModeCampagne.Aucune) await LancerCampagneAsync(_cts.Token);
        }
        catch (OperationCanceledException) { }
        finally { _simulate.IsEnabled = true; _simEnCours = false; MajEtatResultats(); }
    }

    // ───────────────────────── simulation automatique ─────────────────────────
    bool _simAuto = true, _simEnCours;
    long _dureeDerniere = long.MaxValue;   // durée du dernier calcul (ms) ; inconnue = pas d'automatique
    readonly CheckBox _auto = new() { Content = "Auto", IsChecked = true, FontSize = Ui.Aide, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _etat = new() { FontSize = Ui.Aide, VerticalAlignment = VerticalAlignment.Center };
    readonly Avalonia.Threading.DispatcherTimer _relance = new() { Interval = TimeSpan.FromMilliseconds(350) };
    public bool SimulationAutomatique { get => _simAuto; set => _auto.IsChecked = value; }

    /// <summary>Résultats périmés : relance automatique si le dernier calcul était court, sinon « à relancer ».</summary>
    void ResultatsPerimes()
    {
        OublierCampagne();
        MajEtatResultats();
        if (_simAuto && _dureeDerniere < 1000 && !_chargement) { _relance.Stop(); _relance.Start(); }
    }

    void MajEtatResultats()
    {
        bool aJour = _banc == null ? _etude.Bancs.Count > 0 && _etude.Bancs.All(b => _results.ContainsKey(b)) : _last != null;
        _etat.Text = _simEnCours ? "Calcul…" : aJour ? "✓ À jour" : _banc == null && _etude.Bancs.Count == 0 ? "" : "● À relancer";
        _etat.Foreground = _simEnCours ? Ui.Muted : aJour ? Ui.PassBrush : Ui.WarnBrush;
    }
    public string EtatResultats => _etat.Text ?? "";
    public void Reevaluer() => Invalidate();

    void SetVerdict(Verdict? v)
    {
        MajEtatResultats();
        _verdict.IsVisible = v != null;
        if (v == null) return;
        _verdict.Background = v == Verdict.Pass ? Ui.PassBrush : v == Verdict.Fail ? Ui.FailBrush : Ui.WarnBrush;
        _verdict.Child = new TextBlock { Text = v == Verdict.Pass ? "✓ Conforme" : v == Verdict.Fail ? "✕ Non conforme" : "Non évaluable", Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, FontSize = Ui.Texte };
    }

    void RefreshPlot(bool keepLimits = false)
    {
        var active = _plot;
        foreach (var pane in _panes.ToList()) { _plot = pane.Plot; RenderPane(pane, keepLimits); }
        _plot = active;
    }

    IEnumerable<string> ActiveSignals => _active?.Signaux ?? [];

    void RenderPane(Pane pane, bool keepLimits)
    {
        bool isActive = pane == _active;
        var p = _plot.Plot;
        var limits = p.Axes.GetLimits();
        p.Clear(); p.Axes.Right.IsVisible = false;
        StylerGraphique(p);
        p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        var nodesOk = new List<string>();
        if (_last != null && _last.Resultats.TryGetValue(Shown, out var raw0))
            nodesOk = pane.Signaux.Where(n => raw0.Variables.Any(v => string.Equals(v.Name, $"v({n})", StringComparison.OrdinalIgnoreCase))).ToList();
        if (_banc == null || _last == null || !_last.Resultats.TryGetValue(Shown, out var raw) || nodesOk.Count == 0)
        {
            p.Title(""); p.Axes.Bottom.Label.Text = ""; p.Axes.Left.Label.Text = "";
            var t = p.Add.Text(_banc == null ? "Sélectionnez un banc de test pour voir ses courbes" : pane.Signaux.Count == 0 ? "Sonde : cliquez sur un fil du schéma pour afficher sa tension ici" : _last == null ? "Lancez « Simuler » pour tracer les courbes" : "Cette analyse n'a pas été simulée : relancez la simulation", 0, 0);
            t.LabelFontSize = 14; t.LabelFontColor = ScottPlot.Color.FromHex("#5b6170"); t.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
            if (isActive) _readout.IsVisible = false;
            pane.Mesures.Children.Clear();
            p.Axes.SetLimits(-1, 1, -1, 1); p.HideGrid(); p.Axes.Left.IsVisible = p.Axes.Bottom.IsVisible = p.Axes.Top.IsVisible = false; _plot.Refresh(); return;
        }
        p.ShowGrid(); p.Axes.Left.IsVisible = p.Axes.Bottom.IsVisible = p.Axes.Top.IsVisible = true;
        if (VueFft) { RenderSpectre(pane, raw, nodesOk, isActive); return; }
        bool ac = Shown == AnalyseKind.Ac;
        double X(double x) => ac ? Math.Log10(x) : x * 1e3;
        var nodes = nodesOk;
        var palette = new ScottPlot.Palettes.Category10();
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity, xLo = double.PositiveInfinity, xHi = double.NegativeInfinity;
        int idx = 0;
        // Tirages de la campagne sous les courbes nominales.
        var (cLo, cHi) = DessinerCampagne(p, pane, nodes.Take(8).ToList(), X, Enumerable.Range(0, 8).Select(i => palette.GetColor(i)).ToArray());
        foreach (var n in nodes.Take(8))
        {
            var (xs, ys) = Verification.Courbe(Shown, raw, n);
            bool dim = _drawing != null && isActive && nodes.Count > 1 && _drawing.Points.Count > 0 && n != _drawing.Noeud;
            var color = palette.GetColor(idx);
            var sc = p.Add.ScatterLine(xs.Select(X).ToArray(), ys);
            sc.LegendText = ac ? $"|V({n})| (dB)" : $"V({n})"; sc.LineWidth = dim ? 1.5f : (float)Parametres.Courant.EpaisseurCourbes; sc.Color = dim ? color.WithAlpha(0.35) : color;
            yMin = Math.Min(yMin, ys.Min()); yMax = Math.Max(yMax, ys.Max()); xLo = Math.Min(xLo, X(xs[0])); xHi = Math.Max(xHi, X(xs[^1]));
            if (ac)   // Bode : la phase est tracée dans les deux modes, en tirets, sur l'axe de droite
            {
                var r = Verification.Frequentielle(raw, $"v({n})");
                var ph = p.Add.ScatterLine(xs.Select(X).ToArray(), Unwrap(r.Valeurs.ToArray().Select(z => z.Phase * 180 / Math.PI).ToArray()));
                ph.LegendText = $"phase V({n}) (°)"; ph.LineWidth = 1.5f; ph.Color = color.WithAlpha(0.6); ph.LinePattern = ScottPlot.LinePattern.Dashed;
                ph.Axes.YAxis = p.Axes.Right; p.Axes.Right.IsVisible = true; p.Axes.Right.Label.Text = "Phase (°, tirets)";
            }
            idx++;
        }
        if (double.IsFinite(cLo)) { yMin = Math.Min(yMin, cLo); yMax = Math.Max(yMax, cHi); }
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
            if (!ac && Banc.RecalerGabarits()) ReevaluateTraces();
            if (!ac && Banc.TracesActives.Any(g => g.Relatif && pane.Signaux.Contains(g.Noeud)))
            {
                // Repère du front du stimulus auquel les gabarits relatifs sont accrochés.
                var fl = p.Add.VerticalLine(X(Banc.InstantFront));
                fl.Color = Parametres.VersPlot(((ISolidColorBrush)Ui.Muted).Color).WithAlpha(0.7); fl.LineWidth = 1; fl.LinePattern = ScottPlot.LinePattern.Dashed;
                fl.LabelText = "front t0"; fl.LabelFontSize = 10.5f; fl.LabelBackgroundColor = fl.Color; fl.LabelFontColor = ScottPlot.Colors.White;
            }
            double span = Math.Max(yMax - yMin, 1e-9), far = 1000 * span;
            var red = Parametres.VersPlot(Parametres.Courant.Gabarit);
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
                line.Color = red; line.LineWidth = _ligneOuverte == g ? 4.5f : 2.5f; line.MarkerSize = 7; line.MarkerShape = ScottPlot.MarkerShape.FilledSquare; line.MarkerColor = red;
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
                    var col = ok ? Parametres.VersPlot(Parametres.Courant.Conforme) : Parametres.VersPlot(Parametres.Courant.NonConforme);
                    var mk = p.Add.Marker(X(xp), yp); mk.Color = col; mk.Size = 10; mk.Shape = ScottPlot.MarkerShape.OpenCircle; mk.LineWidth = 2;
                    var lim = (double)(g.Limite == Limite.Haute ? yp + m : yp - m);
                    var conn2 = p.Add.Line(X(xp), yp, X(xp), lim); conn2.Color = col; conn2.LineWidth = 1.5f; conn2.LinePattern = ScottPlot.LinePattern.Dotted;
                    var txt = p.Add.Text(ok ? $"marge {SpiceNumber.Format(m, g.Unite)}" : $"franchi de {SpiceNumber.Format(-m, g.Unite)}", X(xp), (yp + lim) / 2);
                    txt.LabelFontColor = col; txt.LabelFontSize = 12; txt.LabelBold = true;
                    bool droite = X(xp) > (xLo + xHi) / 2;
                    txt.LabelOffsetX = droite ? -10 : 10;
                    txt.LabelAlignment = droite ? ScottPlot.Alignment.MiddleRight : ScottPlot.Alignment.MiddleLeft; txt.LabelBackgroundColor = Parametres.VersPlot(((ISolidColorBrush)Ui.Surface).Color).WithAlpha(0.85);
                }
            }
            foreach (var g in Banc.TracesActives.Where(g => g.Analyse == Shown && pane.Signaux.Contains(g.Noeud)))
                DrawMask(g, g.Points, _last.Traces.FirstOrDefault(t => t.Gabarit == g)?.Resultat, false);
            if (_drawing != null && isActive)
                DrawMask(_drawing, _hover is { } h && (_drawing.Points.Count == 0 || h.X > _drawing.Points[^1].X) ? _drawing.Points.Append(h).ToList() : _drawing.Points, null, true);
            foreach (var g in Banc.GabaritsTraces.Where(g => g.Analyse == Shown && pane.Signaux.Contains(g.Noeud))) foreach (var q in g.Points) { yMin = Math.Min(yMin, q.Y); yMax = Math.Max(yMax, q.Y); }

            foreach (var rc in _last.Criteres.Where(c => ac && c.Critere.Mesure == TypeMesure.BandePassante3dB && c.Resultat.Valeur is not null && pane.Signaux.Contains(c.Critere.Noeud)))
            {
                var vl = p.Add.VerticalLine(Math.Log10(rc.Resultat.Valeur!.Value));
                vl.Color = Parametres.VersPlot(rc.Resultat.Verdict == Verdict.Pass ? Parametres.Courant.Conforme : Parametres.Courant.NonConforme);
                vl.LinePattern = ScottPlot.LinePattern.Dotted; vl.LineWidth = 1.5f;
                vl.LabelText = $"fc {SpiceNumber.Format(rc.Resultat.Valeur!.Value, "Hz")}"; vl.LabelFontSize = 11; vl.LabelBackgroundColor = vl.Color; vl.LabelFontColor = ScottPlot.Colors.White;
            }
        }
        // Curseurs : ligne verticale, point sur la courbe, lettre.
        var colorOf = new Dictionary<string, ScottPlot.Color>();
        { int k = 0; foreach (var n in nodes.Take(8)) colorOf[n] = palette.GetColor(k++); }
        ScottPlot.Color ColorOf(string n) => colorOf.TryGetValue(n, out var c) ? c : ScottPlot.Colors.Gray;
        foreach (var c in _cursors.Where(c => pane.Signaux.Contains(c.Noeud)))
        {
            if (CurveAt(c.Noeud, c.X) is not { } yc) continue;
            var col = ColorOf(c.Noeud); bool hot = c == _hoverCursor || c == _dragCursor;
            // Réticule : pointillés fins vertical et horizontal passant par le point de la courbe.
            var vl = p.Add.VerticalLine(X(c.X)); vl.Color = col.WithAlpha(hot ? 0.95 : 0.75); vl.LineWidth = hot ? 1.5f : 1; vl.LinePattern = ScottPlot.LinePattern.Dotted;
            var hl = p.Add.HorizontalLine(yc); hl.Color = col.WithAlpha(hot ? 0.95 : 0.75); hl.LineWidth = hot ? 1.5f : 1; hl.LinePattern = ScottPlot.LinePattern.Dotted;
            var mk = p.Add.Marker(X(c.X), yc); mk.Color = col; mk.Size = hot ? 12 : 9; mk.Shape = ScottPlot.MarkerShape.FilledCircle;
            var mk2 = p.Add.Marker(X(c.X), yc); mk2.Color = ScottPlot.Colors.White; mk2.Size = hot ? 5 : 4; mk2.Shape = ScottPlot.MarkerShape.FilledCircle;
            var lt = p.Add.Text(c.Nom.ToString(), X(c.X), yc); lt.LabelFontColor = ScottPlot.Colors.White; lt.LabelBackgroundColor = col; lt.LabelBold = true; lt.LabelFontSize = 11;
            bool haut = yc > yMin + 0.7 * (yMax - yMin);   // près du haut : étiquette sous le point
            lt.LabelAlignment = haut ? ScottPlot.Alignment.UpperRight : ScottPlot.Alignment.LowerRight; lt.LabelOffsetX = -6; lt.LabelOffsetY = haut ? 6 : -6; lt.LabelPadding = 3;
        }
        if (isActive) RefreshReadout(ColorOf);
        RemplirMesures(pane, raw, nodes, ColorOf);
        // Les pastilles au-dessus du graphique servent de légende : plus de cadre de légende sur les courbes.
        p.Title(""); p.HideLegend();
        if (_panes.Count > 1 && pane != _panes[^1]) p.Axes.Bottom.Label.Text = "";   // titre de l'axe X sur le dernier graphique seulement
        if (keepLimits && limits.Rect.Width > 0 && limits.Left != -1) p.Axes.SetLimits(limits);
        else
        {
            var all = _last.Resultats[Shown];
            var (x0, _) = Verification.Courbe(Shown, all, nodes.First());
            double pad = Math.Max((yMax - yMin) * (_modeTest ? 0.18 : 0.06), 1e-6);
            p.Axes.SetLimits(X(x0[0]), X(x0[^1]), yMin - pad, yMax + pad);

        }
        if (ac) p.Axes.AutoScaleY(p.Axes.Right);   // la phase garde toujours son échelle, même après un zoom
        // Zoom et déplacement bornés aux données : inutile de dézoomer au-delà des courbes.
        BornerAuxDonnees(p, xLo, xHi, yMin, yMax);
        _plot.Refresh();
    }

    /// <summary>Vue FFT d'un banc temporel : amplitude crête (dBV) de chaque signal, fréquence en échelle log.</summary>
    void RenderSpectre(Pane pane, Silx.Core.Raw.RawFile raw, List<string> nodes, bool isActive)
    {
        var p = _plot.Plot;
        var palette = new ScottPlot.Palettes.Category10();
        double yMax = double.NegativeInfinity, xLo = double.PositiveInfinity, xHi = double.NegativeInfinity;
        int idx = 0;
        foreach (var n in nodes.Take(8))
        {
            var (t, y) = Verification.Courbe(AnalyseKind.Transitoire, raw, n);
            var (f, db) = Spectre.Calculer(t, y);
            var color = palette.GetColor(idx++);
            if (f.Length == 0) continue;
            var sc = p.Add.ScatterLine(f.Select(Math.Log10).ToArray(), db);
            sc.LegendText = $"|V({n})| (dBV)"; sc.LineWidth = (float)Math.Max(1, Parametres.Courant.EpaisseurCourbes * 0.7); sc.Color = color;
            yMax = Math.Max(yMax, db.Max()); xLo = Math.Min(xLo, Math.Log10(f[0])); xHi = Math.Max(xHi, Math.Log10(f[^1]));
        }
        p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(), IntegerTicksOnly = true,
            LabelFormatter = x => SpiceNumber.Format(Math.Pow(10, x), "Hz")
        };
        p.Axes.Bottom.Label.Text = "Fréquence (FFT, fenêtre de Hann)"; p.Axes.Left.Label.Text = "Amplitude (dBV crête)";
        p.Title(""); p.HideLegend();
        if (_panes.Count > 1 && pane != _panes[^1]) p.Axes.Bottom.Label.Text = "";
        if (isActive) _readout.IsVisible = false;
        pane.Mesures.Children.Clear();
        if (!double.IsFinite(yMax)) { _plot.Refresh(); return; }
        double yMin = yMax - 120;   // 120 dB de dynamique : en dessous, c'est du bruit numérique
        p.Axes.SetLimits(xLo, xHi, yMin, yMax + 10);
        BornerAuxDonnees(p, xLo, xHi, yMin, yMax + 5);
        _plot.Refresh();
    }

    /// <summary>Couleurs du graphique selon le thème (fond, axes, grille).</summary>
    static void StylerGraphique(ScottPlot.Plot p)
    {
        ScottPlot.Color C(IBrush b) => Parametres.VersPlot(((ISolidColorBrush)b).Color);
        p.FigureBackground.Color = C(Ui.Surface); p.DataBackground.Color = C(Ui.Surface);
        p.Axes.Color(C(Ui.Muted)); p.Axes.FrameColor(C(Ui.Line));
        p.Grid.MajorLineColor = C(Ui.Line);
        foreach (var a in new ScottPlot.IAxis[] { p.Axes.Bottom, p.Axes.Left, p.Axes.Right }) a.Label.ForeColor = C(Ui.Ink);
    }

    static void BornerAuxDonnees(ScottPlot.Plot p, double x0, double x1, double y0, double y1)
    {
        if (!(x1 > x0) || !double.IsFinite(x0) || !double.IsFinite(x1)) return;
        double pad = Math.Max((y1 - y0) * 0.2, 1e-6);
        p.Axes.Rules.Clear();
        p.Axes.Rules.Add(new ScottPlot.AxisRules.MaximumBoundary(p.Axes.Bottom, p.Axes.Left, new ScottPlot.AxisLimits(x0, x1, y0 - pad, y1 + pad)));
    }

    /// <summary>Molette ou pavé tactile sur une courbe : zoom en X autour du pointeur, doux et proportionnel au geste.</summary>
    void OnPlotWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not AvaPlot ap) return;
        e.Handled = true;
        var f = SchematicCanvas.FacteurMolette(e.Delta.Y);
        var pos = e.GetPosition(ap); var sc = ap.DisplayScale;
        var c = ap.Plot.GetCoordinates(new ScottPlot.Pixel((float)(pos.X * sc), (float)(pos.Y * sc)));
        var l = ap.Plot.Axes.GetLimits();
        ap.Plot.Axes.SetLimitsX(c.X - (c.X - l.Left) / f, c.X + (l.Right - c.X) / f);
        ap.Refresh();
    }

    static double[] Unwrap(double[] deg)
    {
        var r = (double[])deg.Clone();
        for (int i = 1; i < r.Length; i++) { while (r[i] - r[i - 1] > 180) r[i] -= 360; while (r[i] - r[i - 1] < -180) r[i] += 360; }
        return r;
    }

    static TextBox Mono() => new() { IsReadOnly = true, FontFamily = new FontFamily("Menlo, Consolas, monospace"), AcceptsReturn = true, FontSize = 12, BorderThickness = new Thickness(0), Padding = new Thickness(10), Background = Ui.Surface };
}
