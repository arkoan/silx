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

/// <summary>Zone des courbes : barre d'outils, graphiques empilés, curseurs, mesures, tracé (temporel, Bode, FFT).</summary>
public sealed partial class MainWindow
{
    // ───────────────────────── zone des courbes ─────────────────────────
    Control BuildPlotArea()
    {
        void Seg(Button b, string icon, string tip, bool test)
        {
            b.Content = Icones.Make(icon, 16); b.Padding = new Thickness(7, 4); b.Background = Brushes.Transparent; b.CornerRadius = new CornerRadius(6); ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { _modeTest = test; if (!test) EndDrawing(false); RefreshPlotToolbar(); RefreshPlot(); };
        }
        Seg(_modeSim, Icones.ModeSimulation, "Mode simulation : courbes seules", false);
        Seg(_modeTestBtn, Icones.ModeTest, "Mode test : courbes avec gabarits (dessiner, déplacer les points, vérifier)", true);
        var seg = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        seg.Children.Add(_modeSim); seg.Children.Add(_modeTestBtn);

        _viewTran = Icones.Button(Icones.Temporel, "Vue temporelle (banc temporel)", () => AfficherFft(false), 16);
        _viewFft = Icones.Button(Icones.Fft, "Vue spectrale (FFT) du banc temporel : amplitude en dBV selon la fréquence", () => AfficherFft(true), 16);
        _viewAc = Icones.Button(Icones.Bode, "Vue Bode du banc fréquentiel", () => AfficherBruit(false), 16);
        _viewBruit = Icones.Button(Icones.Bruit, "Vue bruit du banc fréquentiel : densité de bruit en sortie (trait plein) et ramenée à l'entrée (tirets), en V/√Hz", () => AfficherBruit(true), 16);
        _viewDc = Icones.Button(Icones.TransfertDc, "Caractéristique de transfert DC : sorties en fonction de la source balayée", () => { }, 16);
        var view = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        view.Children.Add(_viewTran); view.Children.Add(_viewFft); view.Children.Add(_viewAc); view.Children.Add(_viewBruit); view.Children.Add(_viewDc);

        foreach (var (b, lim) in new[] { (_drawHigh, Limite.Haute), (_drawLow, Limite.Basse) })
        {
            b.Content = Icones.Make(lim == Limite.Haute ? Icones.LimiteHaute : Icones.LimiteBasse, 16, Ui.FailBrush);
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
        probes.Children.Add(Icones.Button(Icones.Graphique, "Ajouter un graphique sous les autres (les sondes iront dedans)", () => AddPane(), 16));

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(6, 2) };
        bar.Children.Add(probes); bar.Children.Add(Ui.Separator());
        bar.Children.Add(seg); bar.Children.Add(Ui.Separator()); bar.Children.Add(view); bar.Children.Add(Ui.Separator());
        bar.Children.Add(curs); bar.Children.Add(Ui.Separator()); bar.Children.Add(_testTools);
        _boutonEstomper.Click += (_, _) => EstompageTirages = _estompage == Estompage.Conformes ? Estompage.NonConformes : Estompage.Conformes;
        ToolTip.SetTip(_boutonEstomper, "Campagne : tirages estompés (pastel, trait fin). Cliquer pour estomper plutôt les autres. Un clic sur une courbe affiche sa valeur.");
        bar.Children.Add(_boutonEstomper);

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
            plot.AddHandler(PointerTouchPadGestureMagnifyEvent, OnPlotMagnify, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            // Écran tactile : même zoom homogène au pincement.
            plot.GestureRecognizers.Add(new PinchGestureRecognizer());
            double echelle = 1; var pl = plot;
            plot.AddHandler(InputElement.PinchEvent, (_, e) => { ZoomerCourbe(pl, e.ScaleOrigin, Math.Clamp(e.Scale / echelle, 0.5, 2)); echelle = e.Scale; e.Handled = true; });
            plot.AddHandler(InputElement.PinchEndedEvent, (_, _) => echelle = 1);
            plot.PointerExited += (_, _) => { if (_tirageSurvole != null) { _tirageSurvole = null; ToolTip.SetIsOpen(plot, false); RefreshPlot(keepLimits: true); } };
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

    static readonly ScottPlot.Palettes.Category10 Palette10 = new();
    /// <summary>Couleur d'un signal : celle choisie pour le banc (clic sur sa pastille), sinon celle de sa place dans le graphique.</summary>
    public ScottPlot.Color CouleurSignal(string noeud, int rang)
        => _banc != null && _banc.CouleursSignaux.TryGetValue(noeud, out var hex) && Color.TryParse(hex, out var c) ? new ScottPlot.Color(c.R, c.G, c.B) : Palette10.GetColor(rang);

    /// <summary>Choisit (ou rend par défaut, null) la couleur d'un signal du banc actif.</summary>
    public void ColorerSignal(string noeud, string? hex)
    {
        if (_banc == null) return;
        if (hex == null) _banc.CouleursSignaux.Remove(noeud); else _banc.CouleursSignaux[noeud] = hex;
        RebuildPanes(); RefreshPlot(keepLimits: true);
    }

    /// <summary>Clic sur la pastille d'un signal : nuancier (et « par défaut »).</summary>
    void ProposerCouleurSignal(string noeud, Control ancre)
    {
        var sp = new StackPanel { Spacing = 6, Margin = new Thickness(4) };
        sp.Children.Add(new TextBlock { Text = $"Couleur de {NomSignal(noeud)}", FontWeight = FontWeight.SemiBold, FontSize = Ui.Texte });
        var fly = new Flyout { Content = sp, Placement = PlacementMode.BottomEdgeAlignedLeft };
        var w = new WrapPanel { MaxWidth = 230 };
        var nuancier = new[] { "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd", "#8c564b", "#e377c2", "#7f7f7f", "#bcbd22", "#17becf", "#000000", "#0891b2" };
        foreach (var hex in nuancier)
        {
            var b = new Button { Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.Parse(hex)), BorderThickness = new Thickness(0) };
            var hh = hex; b.Click += (_, _) => { fly.Hide(); ColorerSignal(noeud, hh); };
            w.Children.Add(b);
        }
        sp.Children.Add(w);
        var hexBox = new TextBox { PlaceholderText = "#rrggbb", FontSize = 12, Width = 110 };
        hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter && Color.TryParse((hexBox.Text ?? "").Trim(), out Color _)) { fly.Hide(); ColorerSignal(noeud, hexBox.Text!.Trim()); } };
        var defaut = new Button { Content = "Par défaut", FontSize = Ui.Aide, Padding = new Thickness(8, 2) };
        defaut.Click += (_, _) => { fly.Hide(); ColorerSignal(noeud, null); };
        var ligne = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; ligne.Children.Add(hexBox); ligne.Children.Add(defaut);
        sp.Children.Add(ligne);
        fly.ShowAt(ancre);
    }

    Control BuildPaneHeader(Pane pane, bool closable)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 2, 4, 0) };
        for (int i = 0; i < pane.Signaux.Count; i++)
        {
            var n = pane.Signaux[i]; var c = CouleurSignal(n, i);
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            // Pastille de couleur : un clic ouvre le nuancier du signal.
            var pastille = new Button { Padding = new Thickness(2, 6), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand),
                Content = new Border { Width = 12, Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)) } };
            ToolTip.SetTip(pastille, $"Couleur de {NomSignal(n)} : cliquer pour la changer");
            var nc = n; pastille.Click += (_, e) => { e.Handled = true; ProposerCouleurSignal(nc, pastille); };
            chip.Children.Add(pastille);
            chip.Children.Add(new TextBlock { Text = NomSignal(n), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            var x = new Button { Content = Icones.Make(Icones.Croix, 12, Ui.Muted), Padding = new Thickness(2), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(x, $"Retirer {NomSignal(n)} de ce graphique");
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
        _status.Text = $"{NomSignal(node)} ajouté au graphique {idx + 1}" + (_last == null ? " — lancez la simulation pour le voir." : "")
            + (Signaux.EstCourant(node) && _last != null && _last.Resultats.TryGetValue(Shown, out var rw) && !Signaux.Disponible(rw, node, FusionAffichee) ? " — courant non disponible pour ce composant (sous-circuit) : relancez ou sondez une autre broche." : ".");
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
            Ui.SetActive(_viewTran, _banc != null && Shown == AnalyseKind.Transitoire && !VueFft); Ui.SetActive(_viewFft, VueFft); Ui.SetActive(_viewAc, _banc != null && Shown == AnalyseKind.Ac && !VueBruit); Ui.SetActive(_viewBruit, VueBruit);
            _viewTran.IsEnabled = _viewFft.IsEnabled = _banc?.Type == AnalyseKind.Transitoire; _viewAc.IsEnabled = _viewBruit.IsEnabled = _banc?.Type == AnalyseKind.Ac; _viewBruit.Opacity = _viewBruit.IsEnabled ? 1 : 0.3;
            _viewDc.IsVisible = _banc?.Type == AnalyseKind.Dc; Ui.SetActive(_viewDc, _viewDc.IsVisible);
            _viewTran.Opacity = _viewTran.IsEnabled ? 1 : 0.3; _viewFft.Opacity = _viewFft.IsEnabled ? 1 : 0.3; _viewAc.Opacity = _viewAc.IsEnabled ? 1 : 0.3;
        }
        _testTools.IsVisible = _modeTest && _banc != null && !VueAnnexe;
        _boutonEstomper.IsVisible = ResultatCampagne != null && !VueAnnexe;
        _boutonEstomper.Content = _estompage == Estompage.Conformes ? "Estomper : conformes" : "Estomper : non conformes";
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
            var px = _plot.Plot.GetPixel(new ScottPlot.Coordinates(XAff(x), y), _plot.Plot.Axes.Bottom, AxeY(n));
            var dd = Math.Abs(px.Y / sc - pos.Y);
            if (dd < bd) { bd = dd; best = n; }
        }
        return best;
    }

    public void StartDrawing(Limite lim)
    {
        var n = ActiveSignals.FirstOrDefault();   // précisé au premier clic : courbe la plus proche
        if (n == null || _banc == null || VueAnnexe) return;
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

    /// <summary>Point ramené dans la zone de tracé visible (marge de 4 px) : un point de gabarit reste toujours attrapable.</summary>
    public Point DansLaZone(Point pos)
    {
        var r = _plot.Plot.RenderManager.LastRender.DataRect; var sc = _plot.DisplayScale;
        if (!(r.Width > 8 && r.Height > 8)) return pos;
        const double m = 4;
        return new(Math.Clamp(pos.X, r.Left / sc + m, r.Right / sc - m), Math.Clamp(pos.Y, r.Top / sc + m, r.Bottom / sc - m));
    }
    (double X, double Y) ToData(Point pos)
    {
        var sc = _plot.DisplayScale;
        var c = _plot.Plot.GetCoordinates(new ScottPlot.Pixel((float)(pos.X * sc), (float)(pos.Y * sc)));
        return AxeLog ? (Math.Pow(10, c.X), c.Y) : (c.X / EchX, c.Y);
    }

    Schematic? _fusionMemo; Banc? _fusionBanc;
    /// <summary>Circuit + banc affiché (pour résoudre les sondes de courant), mémorisé le temps d'un tracé.</summary>
    Schematic? FusionAffichee
    {
        get
        {
            if (_banc == null) return null;
            if (_fusionMemo == null || _fusionBanc != _banc) { _fusionMemo = _banc.Fusion(_etude.Schema); _fusionBanc = _banc; }
            return _fusionMemo;
        }
    }
    string NomSignal(string s) => Signaux.Nom(s, FusionAffichee);

    /// <summary>Axe des abscisses en log f (Bode, FFT) ; sinon temps en ms.</summary>
    bool AxeLog => Shown == AnalyseKind.Ac || VueFft;   // bruit : banc fréquentiel, donc log f aussi
    double XAff(double x) => AxeLog ? Math.Log10(x) : x * EchX;
    /// <summary>Échelle de l'abscisse linéaire : temps affiché en ms ; grandeur balayée (DC) telle quelle.</summary>
    double EchX => Shown == AnalyseKind.Transitoire ? 1e3 : 1;

    Silx.Core.Raw.RawFile? _spectreRaw; readonly Dictionary<string, (double[], double[])> _spectres = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Courbe affichée d'un signal : v(t), |V| dB (Bode), ou spectre en dBV (vue FFT, mis en cache).</summary>
    (double[] X, double[] Y)? CourbeAffichee(string noeud)
    {
        if (_last == null || !_last.Resultats.TryGetValue(Shown, out var raw)) return null;
        if (!Signaux.Disponible(raw, noeud, FusionAffichee)) return null;
        if (VueBruit) return _bruit.TryGetValue(noeud, out var cb) && cb.F.Length > 0 ? (cb.F, cb.Sortie.Select(DbBruit).ToArray()) : null;
        if (!VueFft) return Verification.Courbe(Shown, raw, noeud, FusionAffichee);
        if (_spectreRaw != raw) { _spectres.Clear(); _spectreRaw = raw; }
        if (!_spectres.TryGetValue(noeud, out var sp))
        {
            var (t, y) = Verification.Courbe(AnalyseKind.Transitoire, raw, noeud, FusionAffichee);
            _spectres[noeud] = sp = Spectre.Calculer(t, y);
        }
        return sp.Item1.Length == 0 ? null : sp;
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
            var p = ToData(DansLaZone(e.GetPosition(_plot)));
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
        if (!VueAnnexe && _hoverVertex is { } hv)   // spectre : pas de gabarit
        {
            e.Handled = true; _dragVertex = hv; _plot.UserInputProcessor.Disable(); e.Pointer.Capture(_plot);
            return;
        }
        if (CursorAt(e.GetPosition(_plot)) is { } hc)
        {
            e.Handled = true; _dragCursor = hc; _plot.UserInputProcessor.Disable(); e.Pointer.Capture(_plot);
            return;
        }
        // Campagne : un clic sur la courbe d'un tirage la met en avant avec sa valeur (re-clic : désélection).
        if (e.ClickCount == 1 && !_cursorMode && TirageSous(e.GetPosition(_plot)) is { } tir)
        { e.Handled = true; ChoisirTirage(tir == _tirageChoisi ? null : tir); return; }
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
            _hover = ToData(DansLaZone(e.GetPosition(_plot))); RefreshPlot(keepLimits: true); return;
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
            // Déplacement d'un point : dans la zone visible, l'abscisse entre celles de ses voisins.
            var (x, y) = ToData(DansLaZone(e.GetPosition(_plot))); var pts = dv.G.Points;
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
        if (_banc == null || _last == null) return;
        var pos = e.GetPosition(_plot);
        SurvolerTirage(pos);
        var hit = _modeTest && !VueAnnexe ? VertexAt(pos) : null;
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
        if (CourbeAffichee(noeud) is not { } c) return x;
        return Math.Clamp(x, c.X[0], c.X[^1]);
    }

    /// <summary>Valeur de la courbe en x (interpolation linéaire, en log f pour Bode).</summary>
    double? CurveAt(string noeud, double x)
    {
        if (CourbeAffichee(noeud) is not { } c) return null;
        var (xs, ys) = c;
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        int i = Array.BinarySearch(xs, x); if (i >= 0) return ys[i];
        i = ~i; bool log = AxeLog;
        double t = log ? Math.Log(x / xs[i - 1]) / Math.Log(xs[i] / xs[i - 1]) : (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
        return ys[i - 1] + t * (ys[i] - ys[i - 1]);
    }

    /// <summary>Phase (degrés, déroulée depuis la plus basse fréquence) de V(nœud) en f, interpolée en log f.</summary>
    double? PhaseAt(string noeud, double f)
    {
        if (_last == null || Shown != AnalyseKind.Ac || VueAnnexe || !_last.Resultats.TryGetValue(AnalyseKind.Ac, out var raw)) return null;
        if (!Signaux.Disponible(raw, noeud, FusionAffichee)) return null;
        var r = Verification.Frequentielle(raw, Signaux.Vecteur(noeud, FusionAffichee)!.Value.Vecteur);
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
            var px = _plot.Plot.GetPixel(new ScottPlot.Coordinates(XAff(c.X), 0));
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
            var (xs, ys) = Verification.Courbe(Shown, raw, n, FusionAffichee);
            var c = couleur(n);
            var ligne = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 18, 0) };
            ligne.Children.Add(new Border { Width = 10, Height = 3, Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), VerticalAlignment = VerticalAlignment.Center });
            ligne.Children.Add(new TextBlock { Text = ac ? $"|{NomSignal(n)}|" : NomSignal(n), FontWeight = FontWeight.SemiBold, FontSize = Ui.Aide });
            foreach (var (nom, val) in ac ? MesuresBode(xs, ys) : Shown == AnalyseKind.Dc ? MesuresTransfert(xs, ys, Signaux.Unite(n), Banc.SourceBalayee?.Unite ?? "V") : MesuresTemps(xs, ys, Signaux.Unite(n)))
                ligne.Children.Add(new TextBlock { Text = $"{nom} {val}", FontSize = Ui.Aide, Foreground = Ui.Muted, FontFamily = new FontFamily("Menlo, Consolas, monospace") });
            pane.Mesures.Children.Add(ligne);
        }
    }

    /// <summary>Min, max, crête-crête, moyenne temporelle, fréquence (passages montants par la moyenne).</summary>
    public static IEnumerable<(string, string)> MesuresTemps(double[] t, double[] v, string u = "V")
    {
        double min = v.Min(), max = v.Max(), aire = 0;
        for (int i = 1; i < t.Length; i++) aire += 0.5 * (v[i] + v[i - 1]) * (t[i] - t[i - 1]);
        double moy = aire / Math.Max(t[^1] - t[0], 1e-30);
        var montees = new List<double>();
        for (int i = 1; i < t.Length; i++)
            if (v[i - 1] < moy && v[i] >= moy) montees.Add(t[i - 1] + (moy - v[i - 1]) / (v[i] - v[i - 1]) * (t[i] - t[i - 1]));
        string f = montees.Count >= 3 && max - min > 1e-9 ? SpiceNumber.Format((montees.Count - 1) / (montees[^1] - montees[0]), "Hz") : "—";
        return [("min", SpiceNumber.Format(min, u)), ("max", SpiceNumber.Format(max, u)), ("c-c", SpiceNumber.Format(max - min, u)), ("moy", SpiceNumber.Format(moy, u)), ("f", f)];
    }

    /// <summary>Caractéristique DC : min, max, seuil de basculement (mi-excursion), pente maximale (gain).</summary>
    public static IEnumerable<(string, string)> MesuresTransfert(double[] x, double[] y, string u, string ux)
    {
        var c = new Silx.Mesures.Courbes.CourbeTemporelle(x, y);
        string Essai(Func<Silx.Mesures.Courbes.CourbeTemporelle, double> f, string unite)
        { try { return SpiceNumber.Format(f(c), unite); } catch (Silx.Mesures.MesureImpossibleException) { return "—"; } }
        return [("min", SpiceNumber.Format(y.Min(), u)), ("max", SpiceNumber.Format(y.Max(), u)), ("seuil", Essai(MesuresDc.Seuil, ux)),
            ("pente max", Essai(MesuresDc.PenteMax, u == ux ? "V/V" : $"{u}/{ux}"))];
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
        bool ac = Shown == AnalyseKind.Ac && !VueAnnexe, fft = VueAnnexe;   // Bode : phase et pente ; FFT / bruit : fréquence et densité
        bool dc = Shown == AnalyseKind.Dc;   // caractéristique DC : abscisse = source balayée ; Δ donne la pente
        string ux = ac || fft ? "Hz" : dc ? Banc.SourceBalayee?.Unite ?? "V" : "s", uy = ac || VueBruit ? "dB" : fft ? "dBV" : "V";
        var g = new Grid { ColumnDefinitions = new("Auto,14,Auto,14,Auto,14,Auto,14,Auto") };
        int row = 0;
        void Cell(string t, int col, IBrush? b = null, bool bold = false, bool right = true)
        {
            var tb = new TextBlock { Text = t, FontSize = 11.5, Foreground = b ?? Ui.Ink, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, FontFamily = new FontFamily("Menlo, Consolas, monospace") };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, col); g.Children.Add(tb);
        }
        void NewRow() { g.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
        NewRow(); Cell("", 0); Cell(ac || fft ? "f" : dc ? Banc.SourceBalayee?.Ref ?? "x" : "t", 2, Ui.Muted); Cell(ac ? "gain" : VueBruit ? "densité" : fft ? "amplitude" : "valeur", 4, Ui.Muted); if (!fft) Cell(ac ? "phase" : dc ? "pente" : "1/Δt", 6, Ui.Muted); if (ac) Cell("pente", 8, Ui.Muted); row++;
        foreach (var grp in bySignal)
        {
            var col = colorOf(grp.Key); var brush = new SolidColorBrush(Color.FromArgb(255, col.R, col.G, col.B));
            NewRow(); Cell(ac || fft ? $"|{NomSignal(grp.Key)}|" : NomSignal(grp.Key), 0, brush, bold: true, right: false); row++;
            var uyS = ac || fft ? uy : Signaux.Unite(grp.Key);
            foreach (var c in grp.OrderBy(c => c.Nom))
            {
                NewRow(); Cell($"  {c.Nom}", 0, brush, right: false); Cell(SpiceNumber.Format(c.X, ux), 2);
                Cell(CurveAt(c.Noeud, c.X) is { } y ? VueBruit ? FormatBruit(y) : SpiceNumber.Format(y, uyS) : "—", 4);
                if (ac) Cell(PhaseAt(c.Noeud, c.X) is { } ph ? $"{ph:0.0}°".Replace('.', ',') : "—", 6);
                row++;
            }
            var ab = grp.OrderBy(c => c.Nom).ToList();
            if (ab.Count == 2 && CurveAt(ab[0].Noeud, ab[0].X) is { } ya && CurveAt(ab[1].Noeud, ab[1].X) is { } yb)
            {
                double dx = ab[1].X - ab[0].X, dy = yb - ya;
                NewRow(); Cell("  Δ B−A", 0, Ui.AccentBrush, bold: true, right: false);
                Cell(ac || fft ? SpiceNumber.Format(dx, ux) + $" (×{ab[1].X / ab[0].X:0.###})" : SpiceNumber.Format(dx, ux), 2, Ui.AccentBrush, bold: true);
                Cell(SpiceNumber.Format(dy, uyS), 4, Ui.AccentBrush, bold: true);
                if (ac)
                {
                    if (PhaseAt(ab[0].Noeud, ab[0].X) is { } pa && PhaseAt(ab[1].Noeud, ab[1].X) is { } pb) Cell($"{pb - pa:+0.0;-0.0;0}°".Replace('.', ','), 6, Ui.AccentBrush, bold: true);
                    Cell(Math.Abs(Math.Log10(ab[1].X / ab[0].X)) > 1e-9 ? $"{dy / Math.Log10(ab[1].X / ab[0].X):0.#} dB/déc".Replace('.', ',') : "", 8, Ui.AccentBrush, bold: true);
                }
                else if (dc) Cell(Math.Abs(dx) > 0 ? SpiceNumber.Format(dy / dx, uyS == ux ? "V/V" : $"{uyS}/{ux}") : "—", 6, Ui.AccentBrush, bold: true);
                else if (!fft) Cell(Math.Abs(dx) > 0 ? SpiceNumber.Format(1 / Math.Abs(dx), "Hz") : "—", 6, Ui.AccentBrush, bold: true);
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
                var px = _plot.Plot.GetPixel(new ScottPlot.Coordinates(XAff(q.X), q.Y));
                if (Math.Abs(px.X / sc - pos.X) < 9 && Math.Abs(px.Y / sc - pos.Y) < 9) return (g, i);
            }
        return null;
    }


    // ───────────────────────── tracé des courbes ─────────────────────────
    void RefreshPlot(bool keepLimits = false)
    {
        _fusionMemo = null;   // le schéma ou le banc a pu changer
        var active = _plot;
        foreach (var pane in _panes.ToList())
        {
            _plot = pane.Plot;
            try { RenderPane(pane, keepLimits); }
            catch (Exception ex)
            {
                // Une courbe impossible à tracer ne doit jamais fermer l'application.
                SignalerErreur(ex, "tracé des courbes");
                var p = pane.Plot.Plot; p.Clear();
                var t = p.Add.Text("Courbes impossibles à tracer : " + ex.Message, 0, 0); t.LabelFontSize = 13; t.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
                p.Axes.SetLimits(-1, 1, -1, 1); pane.Plot.Refresh();
            }
        }
        _plot = active;
    }

    IEnumerable<string> ActiveSignals => _active?.Signaux ?? [];

    void RenderPane(Pane pane, bool keepLimits)
    {
        bool isActive = pane == _active;
        var p = _plot.Plot;
        var limits = p.Axes.GetLimits();
        p.Clear(); p.Axes.Right.IsVisible = false; p.Axes.Right.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        StylerGraphique(p);
        p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        var nodesOk = new List<string>();
        if (_last != null && _last.Resultats.TryGetValue(Shown, out var raw0))
            nodesOk = pane.Signaux.Where(n => Signaux.Disponible(raw0, n, FusionAffichee)).ToList();
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
        if (VueBruit) { RenderBruit(pane, nodesOk, isActive); return; }
        bool ac = Shown == AnalyseKind.Ac;
        double X(double x) => ac ? Math.Log10(x) : x * EchX;
        var nodes = nodesOk;
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity, xLo = double.PositiveInfinity, xHi = double.NegativeInfinity;
        double iMin = double.PositiveInfinity, iMax = double.NegativeInfinity;
        int idx = 0;
        // Tirages de la campagne sous les courbes nominales.
        var (cLo, cHi) = DessinerCampagne(p, pane, nodes.Take(8).ToList(), X, nodes.Take(8).Select((n, i) => CouleurSignal(n, pane.Signaux.IndexOf(n))).ToArray());
        foreach (var n in nodes.Take(8))
        {
            var (xs, ys) = Verification.Courbe(Shown, raw, n, FusionAffichee);
            bool dim = _drawing != null && isActive && nodes.Count > 1 && _drawing.Points.Count > 0 && n != _drawing.Noeud;
            var color = CouleurSignal(n, pane.Signaux.IndexOf(n));
            var sc = p.Add.ScatterLine(xs.Select(X).ToArray(), ys);
            sc.LegendText = ac ? $"|{NomSignal(n)}| (dB)" : NomSignal(n); sc.LineWidth = dim ? 1.5f : (float)Parametres.Courant.EpaisseurCourbes; sc.Color = dim ? color.WithAlpha(0.35) : color;
            xLo = Math.Min(xLo, X(xs[0])); xHi = Math.Max(xHi, X(xs[^1]));
            if (AxeCourant(n))
            {
                // Courant en temporel : axe de droite, à son échelle (les tensions gardent l'axe de gauche).
                sc.Axes.YAxis = p.Axes.Right; p.Axes.Right.IsVisible = true; p.Axes.Right.Label.Text = "Courant";
                p.Axes.Right.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { LabelFormatter = y => SpiceNumber.Format(y, "A") };
                iMin = Math.Min(iMin, ys.Min()); iMax = Math.Max(iMax, ys.Max());
            }
            else { yMin = Math.Min(yMin, ys.Min()); yMax = Math.Max(yMax, ys.Max()); }
            if (ac)   // Bode : la phase est tracée dans les deux modes, en tirets, sur l'axe de droite
            {
                var r = Verification.Frequentielle(raw, Signaux.Vecteur(n, FusionAffichee)!.Value.Vecteur);
                var ph = p.Add.ScatterLine(xs.Select(X).ToArray(), Unwrap(r.Valeurs.ToArray().Select(z => z.Phase * 180 / Math.PI).ToArray()));
                ph.LegendText = $"phase V({n}) (°)"; ph.LineWidth = 1.5f; ph.Color = color.WithAlpha(0.6); ph.LinePattern = ScottPlot.LinePattern.Dashed;
                ph.Axes.YAxis = p.Axes.Right; p.Axes.Right.IsVisible = true; p.Axes.Right.Label.Text = "Phase (°, tirets)";
            }
            idx++;
        }
        if (double.IsFinite(cLo)) { yMin = Math.Min(yMin, cLo); yMax = Math.Max(yMax, cHi); }
        if (ac && xHi - xLo < 0.5)
        {
            // Bande étroite (moins d'une décade, ex. résonance d'un quartz) : graduations ordinaires, fréquence au hertz près.
            var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { LabelFormatter = x => Math.Pow(10, x).ToString("#,0.#", fr) + " Hz" };
            p.Axes.Bottom.Label.Text = "Fréquence"; p.Axes.Left.Label.Text = "Gain (dB)";
        }
        else if (ac)
        {
            p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
            {
                MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(), IntegerTicksOnly = true,
                LabelFormatter = x => SpiceNumber.Format(Math.Pow(10, x), "Hz")
            };
            p.Axes.Bottom.Label.Text = "Fréquence"; p.Axes.Left.Label.Text = "Gain (dB)";
        }
        else if (Shown == AnalyseKind.Dc)
        {
            var sb = Banc.SourceBalayee;
            p.Axes.Bottom.Label.Text = sb is { } s ? $"{s.Ref} ({s.Unite})" : "Grandeur balayée"; p.Axes.Left.Label.Text = "Tension (V)";
            p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { LabelFormatter = x => SpiceNumber.Format(x, "") };
        }
        else { p.Axes.Bottom.Label.Text = "Temps (ms)"; p.Axes.Left.Label.Text = "Tension (V)"; }

        if (_modeTest)
        {
            if (Shown == AnalyseKind.Transitoire && Banc.RecalerGabarits()) ReevaluateTraces();
            if (Shown == AnalyseKind.Transitoire && Banc.TracesActives.Any(g => g.Relatif && pane.Signaux.Contains(g.Noeud)))
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
        ScottPlot.Color ColorOf(string n) => CouleurSignal(n, pane.Signaux.IndexOf(n));
        DessinerCurseurs(p, pane, X, ColorOf, yMin, yMax);
        if (isActive) RefreshReadout(ColorOf);
        RemplirMesures(pane, raw, nodes, ColorOf);
        // Les pastilles au-dessus du graphique servent de légende : plus de cadre de légende sur les courbes.
        p.Title(""); p.HideLegend();
        if (_panes.Count > 1 && pane != _panes[^1]) p.Axes.Bottom.Label.Text = "";   // titre de l'axe X sur le dernier graphique seulement
        if (keepLimits && limits.Rect.Width > 0 && limits.Left != -1) p.Axes.SetLimits(limits);
        else
        {
            var all = _last.Resultats[Shown];
            var (x0, _) = Verification.Courbe(Shown, all, nodes.First(), FusionAffichee);
            double pad = Math.Max((yMax - yMin) * (_modeTest ? 0.18 : 0.06), 1e-6);
            p.Axes.SetLimits(X(x0[0]), X(x0[^1]), yMin - pad, yMax + pad);

        }
        if (ac) p.Axes.AutoScaleY(p.Axes.Right);   // la phase garde toujours son échelle, même après un zoom
        else if (double.IsFinite(iMin)) { double pi = Math.Max((iMax - iMin) * 0.08, 1e-12); p.Axes.SetLimitsY(iMin - pi, iMax + pi, p.Axes.Right); }
        if (!double.IsFinite(yMin) && double.IsFinite(iMin)) { yMin = -1; yMax = 1; }   // que des courants : axe de gauche par défaut
        // Zoom et déplacement bornés aux données : inutile de dézoomer au-delà des courbes.
        BornerAuxDonnees(p, xLo, xHi, yMin, yMax);
        _plot.Refresh();
    }

    /// <summary>Vue FFT d'un banc temporel : amplitude crête (dBV) de chaque signal, fréquence en échelle log.</summary>
    /// <summary>Un courant en vue temporelle se trace sur l'axe de droite.</summary>
    bool AxeCourant(string n) => Signaux.EstCourant(n) && Shown is AnalyseKind.Transitoire or AnalyseKind.Dc && !VueFft;
    ScottPlot.IYAxis AxeY(string n) => AxeCourant(n) ? _plot.Plot.Axes.Right : _plot.Plot.Axes.Left;

    /// <summary>Curseurs : réticule pointillé, point sur la courbe, lettre (temporel, Bode et FFT).</summary>
    void DessinerCurseurs(ScottPlot.Plot p, Pane pane, Func<double, double> X, Func<string, ScottPlot.Color> ColorOf, double yMin, double yMax)
    {
        foreach (var c in _cursors.Where(c => pane.Signaux.Contains(c.Noeud)))
        {
            if (CurveAt(c.Noeud, c.X) is not { } yc) continue;
            var col = ColorOf(c.Noeud); bool hot = c == _hoverCursor || c == _dragCursor;
            var vl = p.Add.VerticalLine(X(c.X)); vl.Color = col.WithAlpha(hot ? 0.95 : 0.75); vl.LineWidth = hot ? 1.5f : 1; vl.LinePattern = ScottPlot.LinePattern.Dotted;
            var ay = AxeY(c.Noeud);
            var hl = p.Add.HorizontalLine(yc); hl.Axes.YAxis = ay; hl.Color = col.WithAlpha(hot ? 0.95 : 0.75); hl.LineWidth = hot ? 1.5f : 1; hl.LinePattern = ScottPlot.LinePattern.Dotted;
            var mk = p.Add.Marker(X(c.X), yc); mk.Axes.YAxis = ay; mk.Color = col; mk.Size = hot ? 12 : 9; mk.Shape = ScottPlot.MarkerShape.FilledCircle;
            var mk2 = p.Add.Marker(X(c.X), yc); mk2.Axes.YAxis = ay; mk2.Color = ScottPlot.Colors.White; mk2.Size = hot ? 5 : 4; mk2.Shape = ScottPlot.MarkerShape.FilledCircle;
            var lt = p.Add.Text(c.Nom.ToString(), X(c.X), yc); lt.Axes.YAxis = ay; lt.LabelFontColor = ScottPlot.Colors.White; lt.LabelBackgroundColor = col; lt.LabelBold = true; lt.LabelFontSize = 11;
            bool haut = yc > yMin + 0.7 * (yMax - yMin);   // près du haut : étiquette sous le point
            lt.LabelAlignment = haut ? ScottPlot.Alignment.UpperRight : ScottPlot.Alignment.LowerRight; lt.LabelOffsetX = -6; lt.LabelOffsetY = haut ? 6 : -6; lt.LabelPadding = 3;
        }
    }

    // ───────────────────────── vue bruit ─────────────────────────
    readonly Dictionary<string, CourbeBruit> _bruit = new(StringComparer.OrdinalIgnoreCase);
    ResultatEtude? _bruitPour; bool _bruitEnCours; string? _bruitErreur;
    /// <summary>Densité de bruit tracée en dB (1 V/√Hz = 0 dB), graduée en V/√Hz.</summary>
    static double DbBruit(double v) => 20 * Math.Log10(Math.Max(v, 1e-30));
    static string FormatBruit(double db) => SpiceNumber.Format(Math.Pow(10, db / 20), "V/√Hz");

    /// <summary>Calcule (en tâche de fond) le bruit des nœuds affichés qui manquent, puis redessine.</summary>
    void DemanderBruit(IEnumerable<string> noeuds)
    {
        if (_last != _bruitPour) { _bruit.Clear(); _bruitErreur = null; _bruitPour = _last; if (_last != null) foreach (var (k, v) in _last.Bruit) _bruit[k] = v; }
        var manquants = noeuds.Where(n => !Signaux.EstCourant(n) && !_bruit.ContainsKey(n)).ToList();
        if (manquants.Count == 0 || _bruitEnCours || _bruitErreur != null || _banc == null) return;
        _bruitEnCours = true; var pour = _last; var banc = _banc;
        Lancer(async () =>
        {
            try
            {
                var (c, err) = await Bruit.CalculerAsync(_etude, banc, manquants);
                if (pour != _bruitPour) return;
                foreach (var (k, v) in c) _bruit[k] = v;
                _bruitErreur = err;
                foreach (var n in manquants.Where(n => !_bruit.ContainsKey(n))) _bruit[n] = new CourbeBruit(n, [], [], [], double.NaN, double.NaN);
            }
            finally { _bruitEnCours = false; }
            RefreshPlot(keepLimits: false);
        }, "analyse de bruit");
    }

    void RenderBruit(Pane pane, List<string> nodes, bool isActive)
    {
        var p = _plot.Plot;
        DemanderBruit(nodes);
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity, xLo = double.PositiveInfinity, xHi = double.NegativeInfinity;
        pane.Mesures.Children.Clear();
        foreach (var n in nodes.Take(8))
        {
            if (!_bruit.TryGetValue(n, out var cb) || cb.F.Length == 0) continue;
            var color = CouleurSignal(n, pane.Signaux.IndexOf(n));
            var xs = cb.F.Select(Math.Log10).ToArray();
            var so = p.Add.ScatterLine(xs, cb.Sortie.Select(DbBruit).ToArray());
            so.LegendText = $"bruit en sortie {NomSignal(n)}"; so.LineWidth = (float)Parametres.Courant.EpaisseurCourbes; so.Color = color;
            var en = p.Add.ScatterLine(xs, cb.Entree.Select(DbBruit).ToArray());
            en.LegendText = $"ramené à l'entrée"; en.LineWidth = 1.5f; en.Color = color.WithAlpha(0.6); en.LinePattern = ScottPlot.LinePattern.Dashed;
            // Une densité nulle (nœud tenu par une source idéale) ne fixe pas l'échelle.
            foreach (var y in cb.Sortie.Concat(cb.Entree).Where(v => v > 1e-25).Select(DbBruit)) { yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y); }
            xLo = Math.Min(xLo, xs[0]); xHi = Math.Max(xHi, xs[^1]);
            var ligne = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 18, 0) };
            ligne.Children.Add(new Border { Width = 10, Height = 3, Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), VerticalAlignment = VerticalAlignment.Center });
            ligne.Children.Add(new TextBlock { Text = NomSignal(n), FontWeight = FontWeight.SemiBold, FontSize = Ui.Aide });
            foreach (var (nom, val) in new[] { ("total sortie", SpiceNumber.Format(cb.TotalSortie, "V") + " eff."), ("total entrée", SpiceNumber.Format(cb.TotalEntree, "V") + " eff."),
                ("à 1 kHz", cb.SortieA(1e3) is { } d ? SpiceNumber.Format(d, "V/√Hz") : "—") })
                ligne.Children.Add(new TextBlock { Text = $"{nom} {val}", FontSize = Ui.Aide, Foreground = Ui.Muted, FontFamily = new FontFamily("Menlo, Consolas, monospace") });
            pane.Mesures.Children.Add(ligne);
        }
        p.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(), IntegerTicksOnly = true,
            LabelFormatter = x => SpiceNumber.Format(Math.Pow(10, x), "Hz")
        };
        p.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { LabelFormatter = FormatBruit };
        p.Axes.Bottom.Label.Text = "Fréquence"; p.Axes.Left.Label.Text = "Densité de bruit (V/√Hz)";
        p.Title(""); p.HideLegend();
        if (_panes.Count > 1 && pane != _panes[^1]) p.Axes.Bottom.Label.Text = "";
        if (!double.IsFinite(yMax))
        {
            p.Axes.Left.IsVisible = p.Axes.Bottom.IsVisible = false; p.HideGrid();
            var t = p.Add.Text(_bruitErreur ?? (_bruitEnCours ? "Analyse de bruit en cours…" : "Bruit indisponible pour ces signaux (tensions de nœud seulement)"), 0, 0);
            t.LabelFontSize = 14; t.LabelFontColor = ScottPlot.Color.FromHex("#5b6170"); t.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
            p.Axes.SetLimits(-1, 1, -1, 1);
            if (isActive) _readout.IsVisible = false; _plot.Refresh(); return;
        }
        ScottPlot.Color ColorOf(string n) => CouleurSignal(n, pane.Signaux.IndexOf(n));
        DessinerCurseurs(p, pane, Math.Log10, ColorOf, yMin, yMax);
        if (isActive) RefreshReadout(ColorOf);
        yMin = Math.Max(yMin, yMax - 120);   // 120 dB de dynamique au plus
        double marge = Math.Max(3, (yMax - yMin) * 0.08);
        p.Axes.SetLimits(xLo, xHi, yMin - marge, yMax + marge);
        BornerAuxDonnees(p, xLo, xHi, yMin - marge, yMax + marge);
        _plot.Refresh();
    }

    void RenderSpectre(Pane pane, Silx.Core.Raw.RawFile raw, List<string> nodes, bool isActive)
    {
        var p = _plot.Plot;
        double yMax = double.NegativeInfinity, xLo = double.PositiveInfinity, xHi = double.NegativeInfinity;
        int idx = 0;
        foreach (var n in nodes.Take(8))
        {
            var (t, y) = Verification.Courbe(AnalyseKind.Transitoire, raw, n, FusionAffichee);
            var (f, db) = Spectre.Calculer(t, y);
            var color = CouleurSignal(n, pane.Signaux.IndexOf(n)); idx++;
            if (f.Length == 0) continue;
            var sc = p.Add.ScatterLine(f.Select(Math.Log10).ToArray(), db);
            sc.LegendText = $"|{NomSignal(n)}| (dB)"; sc.LineWidth = (float)Math.Max(1, Parametres.Courant.EpaisseurCourbes * 0.7); sc.Color = color;
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
        pane.Mesures.Children.Clear();
        if (!double.IsFinite(yMax)) { if (isActive) _readout.IsVisible = false; _plot.Refresh(); return; }
        double yMin = yMax - 120;   // 120 dB de dynamique : en dessous, c'est du bruit numérique
        ScottPlot.Color ColorOf(string n) => CouleurSignal(n, pane.Signaux.IndexOf(n));
        DessinerCurseurs(p, pane, Math.Log10, ColorOf, yMin, yMax);
        if (isActive) RefreshReadout(ColorOf);
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

    /// <summary>Pincement sur une courbe : zoom homogène (temps ou fréquence et axe vertical) autour du pointeur, borné aux données.</summary>
    void OnPlotMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (sender is not AvaPlot ap) return;
        e.Handled = true;
        ZoomerCourbe(ap, e.GetPosition(ap), SchematicCanvas.FacteurPincement(e.Delta.X));
    }

    /// <summary>Zoom homogène d'un graphique d'un facteur <paramref name="f"/> autour d'un point (coordonnées du contrôle).</summary>
    public static void ZoomerCourbe(AvaPlot ap, Point pos, double f)
    {
        var sc = ap.DisplayScale;
        var c = ap.Plot.GetCoordinates(new ScottPlot.Pixel((float)(pos.X * sc), (float)(pos.Y * sc)));
        var l = ap.Plot.Axes.GetLimits();
        ap.Plot.Axes.SetLimits(c.X - (c.X - l.Left) / f, c.X + (l.Right - c.X) / f, c.Y - (c.Y - l.Bottom) / f, c.Y + (l.Top - c.Y) / f);
        ap.Refresh();
    }

    static double[] Unwrap(double[] deg)
    {
        var r = (double[])deg.Clone();
        for (int i = 1; i < r.Length; i++) { while (r[i] - r[i - 1] > 180) r[i] -= 360; while (r[i] - r[i - 1] < -180) r[i] += 360; }
        return r;
    }
}
