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
        /// <summary>Vue du dernier tracé (banc, analyse, FFT, bruit) : si elle change, les limites ne sont pas gardées.</summary>
        public object? Vue { get; set; }
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
    public Button BoutonPalette(ComponentKind k) => _paletteButtons[k];
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
    Button _viewTran = null!, _viewAc = null!, _viewFft = null!, _viewDc = null!, _viewBruit = null!, _boutonOp = null!;
    bool _vueFft;
    /// <summary>Vue spectrale (FFT) d'un banc temporel : amplitude en dBV selon la fréquence.</summary>
    public bool VueFft => _vueFft && _banc?.Type == AnalyseKind.Transitoire;
    public void AfficherFft(bool fft) { if (_vueFft != fft) _cursors.Clear(); _vueFft = fft; if (fft) EndDrawing(false); RefreshPlotToolbar(); RefreshPlot(); }
    bool _vueBruit;
    /// <summary>Vue bruit d'un banc fréquentiel : densité de bruit en sortie et ramenée à l'entrée (analyse .noise).</summary>
    public bool VueBruit => _vueBruit && _banc?.Type == AnalyseKind.Ac;
    public void AfficherBruit(bool bruit) { if (_vueBruit != bruit) _cursors.Clear(); _vueBruit = bruit; if (bruit) EndDrawing(false); RefreshPlotToolbar(); RefreshPlot(); }
    /// <summary>Vue annexe (spectre FFT ou bruit) : pas de gabarit ni de campagne.</summary>
    bool VueAnnexe => VueFft || VueBruit;
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
        if (_banc == null || _last == null || CourbeProche(pos) is not { } sig) return;
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
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Silx.App/Assets/icone.png"))); } catch (Exception) { /* icône facultative */ }

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
        // Filet de sécurité : une erreur imprévue (dans un gestionnaire d'événement ou une tâche) est signalée et
        // journalisée au lieu de fermer l'application.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) => { e.Handled = true; SignalerErreur(e.Exception, "interface"); };
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
            CaptureSession().Enregistrer();   // projets ouverts, banc et disposition : retrouvés à la prochaine ouverture
            if (_fermetureConfirmee || !_projets.Any(p => p.Modifie)) return;
            e.Cancel = true;
            var choix = await Demander($"{_projets.Count(p => p.Modifie)} projet(s) ont des modifications non enregistrées.", "Tout enregistrer et quitter", "Quitter sans enregistrer");
            if (choix == 0) { foreach (var p in _projets.Where(p => p.Modifie).ToList()) if (!await EnregistrerAsync(p, false)) return; }
            if (choix is 0 or 1) { _fermetureConfirmee = true; Close(); }
        };

        // Démarrage : la session précédente (onglets, banc, disposition), sinon l'exemple RC au premier plan et les autres
        // démonstrations dans des onglets voisins (variété).
        if (Session.Charger() is { Onglets.Count: > 0 } session && RestaurerSession(session)) { }
        else
        {
            var exemple = OuvrirDemo("Exemple RC");
            foreach (var (nom, _) in Demos.Toutes) OuvrirDemo(nom);
            ActiverProjet(exemple);
            Disposer(Disposition.Mixte);
        }
        CreerMenus();
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
            var x = new Button { Content = Icones.Make(Icones.Croix, 12, Ui.Muted), Padding = new Thickness(3), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
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
    /// <summary>Ouvre une démonstration intégrée (« Exemple RC » ou une de <see cref="Demos.Toutes"/>).</summary>
    public ProjetOuvert OuvrirDemo(string nom)
    {
        var e = nom == "Exemple RC" ? EtudeModel.Demo() : Demos.Toutes.Single(d => d.Nom == nom).Creer();
        var p = OuvrirDansOnglet(e, nom, null); p.Demo = nom;
        return p;
    }

    /// <summary>État de travail : onglets, banc et onglet actifs, disposition, tiroir.</summary>
    public Session CaptureSession()
    {
        if (_projet != null) _projet.BancActif = _banc;
        var s = new Session { Actif = Math.Max(0, _projets.IndexOf(_projet!)), Disposition = _disposition.ToString(), Detail = _detailOuvert?.ToString(), HauteurDetails = _detailsContenu.Height };
        foreach (var p in _projets)
        {
            bool modifie = p.Modifie;
            s.Onglets.Add(new Session.Onglet
            {
                Nom = p.Nom, Chemin = p.Chemin,
                Demo = p.Demo != null && p.Chemin == null && !modifie ? p.Demo : null,
                Contenu = (p.Chemin == null && (p.Demo == null || modifie)) || (p.Chemin != null && modifie) ? Projet.VersJson(p.Etude) : null,
                Banc = p.BancActif != null ? p.Etude.Bancs.IndexOf(p.BancActif) : -1,
            });
        }
        return s;
    }

    /// <summary>Rouvre les onglets d'une session ; faux si aucun n'a pu l'être (démarrage par défaut).</summary>
    public bool RestaurerSession(Session s)
    {
        var ouverts = new List<ProjetOuvert?>();
        foreach (var o in s.Onglets)
        {
            ProjetOuvert? p = null;
            try
            {
                if (o.Demo != null && (o.Demo == "Exemple RC" || Demos.Toutes.Any(d => d.Nom == o.Demo))) p = OuvrirDemo(o.Demo);
                else if (o.Contenu != null)
                {
                    p = OuvrirDansOnglet(Projet.DepuisJson(o.Contenu), o.Nom, o.Chemin);
                    // Modifications non enregistrées d'un fichier : la référence reste le fichier, l'onglet apparaît modifié.
                    if (o.Chemin != null && File.Exists(o.Chemin)) p.Reference = Projet.VersJson(Projet.Ouvrir(o.Chemin));
                }
                else if (o.Chemin != null && File.Exists(o.Chemin)) p = OuvrirFichier(o.Chemin);
            }
            catch (Exception ex) { SignalerErreur(ex, $"restauration de « {o.Nom} »"); }
            // Le projet vient d'être activé : on affiche son banc (gardé quand on passe à l'onglet suivant).
            if (p != null && p == _projet) SelectBanc(o.Banc >= 0 && o.Banc < p.Etude.Bancs.Count ? p.Etude.Bancs[o.Banc] : null);
            ouverts.Add(p);
        }
        var valides = ouverts.OfType<ProjetOuvert>().ToList();
        if (valides.Count == 0) return false;
        ActiverProjet(ouverts.ElementAtOrDefault(s.Actif) ?? valides[0]);
        Disposer(Enum.TryParse<Disposition>(s.Disposition, out var d) ? d : Disposition.Mixte);
        _detailsContenu.Height = Math.Clamp(s.HauteurDetails, 80, 800);
        if (Enum.TryParse<Detail>(s.Detail, out var det)) OuvrirDetail(det);
        RafraichirOnglets();
        return true;
    }

    public ProjetOuvert? OuvrirFichier(string chemin)
    {
        if (_projets.FirstOrDefault(p => p.Chemin != null && Path.GetFullPath(p.Chemin) == Path.GetFullPath(chemin)) is { } deja) { ActiverProjet(deja); return deja; }
        try
        {
            var p = OuvrirDansOnglet(Projet.Ouvrir(chemin), Path.GetFileNameWithoutExtension(chemin), chemin);
            _status.Text = $"Projet ouvert : {chemin}"; RetenirRecent(chemin);
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
            _status.Text = $"Projet enregistré : {chemin}"; RetenirRecent(chemin);
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
        _canvas.SondeImplicite = _zoneActive == Zone.Courbes && b != null;
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
                var reg = new Button { Content = Icones.Make(Icones.Reglages, 13, Ui.Muted), Padding = new Thickness(3), Background = Brushes.Transparent, CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(reg, $"Réglages du banc « {removable.Nom} » : nom, analyse");
                var rb = removable; reg.Click += (_, e) => { e.Handled = true; if (_banc != rb) SelectBanc(rb); OuvrirReglagesBanc(reg); };
                row.Children.Add(reg);
                var x = new Button { Content = Icones.Make(Icones.Croix, 12, Ui.Muted), Padding = new Thickness(3), Background = Brushes.Transparent, CornerRadius = new CornerRadius(8), Margin = new Thickness(2, 0, -6, 0), VerticalAlignment = VerticalAlignment.Center };
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
        bar.Children.Add(Icones.Button(Icones.Ouvrir, "Ouvrir un projet .silx (⌘O)", () => Lancer(() => OuvrirProjetAsync(), "ouverture d'un projet")));
        bar.Children.Add(Icones.Button(Icones.Enregistrer, "Enregistrer le projet (⌘S) ; ⇧⌘S : enregistrer sous", () => Lancer(() => EnregistrerAsync(_projet!, false), "enregistrement")));
        bar.Children.Add(Icones.Button(Icones.Rapport, "Exporter le rapport de test (Markdown et courbes)", () => Lancer(() => ExporterRapportAsync(), "export du rapport")));
        Button? exp = null;
        exp = Icones.Button(Icones.Exporter, "Exporter le schéma et les courbes en PDF, SVG ou PNG", () => ProposerExport(exp!));
        bar.Children.Add(exp);
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
        _boutonOp = Icones.Button(Icones.PointFonctionnement, "Point de fonctionnement DC sur le schéma : tension de chaque nœud, courant de chaque composant (stimuli à leur valeur continue)",
            () => Lancer(() => AfficherPointFonctionnementAsync(!_opVisible), "point de fonctionnement"));
        rightSide.Children.Add(_etat); rightSide.Children.Add(_verdict); rightSide.Children.Add(_boutonOp); rightSide.Children.Add(_auto); rightSide.Children.Add(_simulate);
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
                ToolTip.SetTip(b, $"{nom} ({touche}) — Maj+clic pour en poser plusieurs" + (source ? "\nDans un banc : stimulus de ce banc. Dans le circuit sous test : alimentation ou polarisation commune à tous les bancs." : ""));
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
    public static string Categorie(DefinitionComposant d) => d.CodeXspice != null ? "Logique" : d.Symbole.Gabarit == GabaritSymbole.Quartz ? "Quartz" : d.Symbole.Gabarit switch
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
    static readonly string[] OrdreCategories = ["Diode", "Bipolaire NPN", "Bipolaire PNP", "MOSFET canal N", "MOSFET canal P", "JFET", "Quartz", "Logique", "Sous-circuit"];

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
        string texte;
        try
        {
            await using var st = await files[0].OpenReadAsync();
            if (st.CanSeek && st.Length > 50_000_000) { _status.Text = $"⛔ {files[0].Name} : fichier trop gros pour une bibliothèque de modèles (plus de 50 Mo)."; return; }
            using var rd = new StreamReader(st);
            texte = await rd.ReadToEndAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _status.Text = $"⛔ Lecture impossible de {files[0].Name} : {ex.Message}"; return; }
        var defs = ImporterModeles(texte, files[0].Name);
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
        // Un clic sur le schéma lui donne la main, sauf s'il sonde (sonde implicite : le graphique garde la main).
        _canvas.AddHandler(PointerPressedEvent, (_, _) => { if (!_canvas.SondeVisee) ActiverZone(Zone.Schema); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
    readonly Button _reduireDetails = new() { Content = "▾ Réduire", FontSize = Ui.Aide, Padding = new Thickness(8, 1), Background = Brushes.Transparent, IsVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    Border? _poigneeDetails;
    public bool BarreBancsVisible => _benchBar.IsEffectivelyVisible;
    public IReadOnlyList<string> SignauxActifs => _active?.Signaux ?? [];
    public bool ReduireDetailsVisible => _reduireDetails.IsVisible;
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
        // Tiroir réductible : chevron pour le replier, poignée au-dessus du contenu pour le redimensionner.
        _reduireDetails.Click += (_, _) => OuvrirDetail(null);
        ToolTip.SetTip(_reduireDetails, "Réduire le tiroir (re-cliquer un onglet le rouvre)");
        var g = new Grid { ColumnDefinitions = new("*,Auto") };
        g.Children.Add(barre); Grid.SetColumn(_reduireDetails, 1); g.Children.Add(_reduireDetails);
        var poignee = new Border { Height = 5, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth) };
        ToolTip.SetTip(poignee, "Glisser pour changer la hauteur du tiroir");
        double? y0 = null; double h0 = 0;
        poignee.PointerPressed += (_, e) => { y0 = e.GetPosition(this).Y; h0 = _detailsContenu.Height; e.Pointer.Capture(poignee); e.Handled = true; };
        poignee.PointerMoved += (_, e) => { if (y0 is { } y) _detailsContenu.Height = Math.Clamp(h0 + (y - e.GetPosition(this).Y), 60, Math.Max(120, Bounds.Height - 260)); };
        poignee.PointerReleased += (_, e) => { y0 = null; e.Pointer.Capture(null); };
        _poigneeDetails = poignee;
        var dock = new DockPanel();
        var barreB = new Border { Child = g, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(poignee, Dock.Top); dock.Children.Add(poignee);
        DockPanel.SetDock(barreB, Dock.Top); dock.Children.Add(barreB); dock.Children.Add(_detailsContenu);
        poignee.IsVisible = false;
        return dock;
    }

    /// <summary>Ouvre le tiroir sur un détail (null : le replie).</summary>
    public void OuvrirDetail(Detail? d)
    {
        _detailOuvert = d;
        _detailsContenu.IsVisible = d != null; _reduireDetails.IsVisible = d != null;
        if (_poigneeDetails != null) _poigneeDetails.IsVisible = d != null;
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
        _canvas.SondeImplicite = z == Zone.Courbes && _banc != null;
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
        // Courbes seules : la barre des bancs (pastilles et leurs réglages) reste en haut, seul le schéma est masqué.
        _zoneHaut.IsVisible = true; _cadreSchema.IsVisible = d != Disposition.Courbes;
        _zoneBas.IsVisible = d != Disposition.Schema; _separateur.IsVisible = d == Disposition.Mixte;
        _centre.RowDefinitions[0].Height = d == Disposition.Courbes ? GridLength.Auto : new GridLength(3, GridUnitType.Star);
        _centre.RowDefinitions[1].Height = d == Disposition.Mixte ? new GridLength(5) : new GridLength(0);
        _centre.RowDefinitions[2].Height = d == Disposition.Schema ? new GridLength(0) : new GridLength(d == Disposition.Courbes ? 1 : 2, GridUnitType.Star);
        foreach (var (k, b) in _boutonsDisposition) Ui.SetActive(b, k == d);
        if (d == Disposition.Courbes) ActiverZone(Zone.Courbes);
        else if (d == Disposition.Schema) ActiverZone(Zone.Schema);
    }

    static TextBox Mono() => new() { IsReadOnly = true, FontFamily = new FontFamily("Menlo, Consolas, monospace"), AcceptsReturn = true, FontSize = 12, BorderThickness = new Thickness(0), Padding = new Thickness(10), Background = Ui.Surface };
}
