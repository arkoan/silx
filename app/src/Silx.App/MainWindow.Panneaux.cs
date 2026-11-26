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

/// <summary>Panneau de droite : propriétés et bulle d'édition des composants, réglages des bancs.</summary>
public sealed partial class MainWindow
{
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
        else if (m != ModeleBanc.Vide) { _status.Text = $"Banc « {n} » créé : source sur {entree}."; Lancer(() => SimulateAsync(), "simulation"); }
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
        var pts = Ui.Field(Banc.Ac.Lineaire ? "Points" : "Pts/déc.", Banc.Ac.PointsParDecade.ToString(), v => { if (int.TryParse(v, out var x) && x > 0) Banc.Ac.PointsParDecade = x; });
        Grid.SetColumn(pts, 4); ac.Children.Add(pts);
        var lin = new CheckBox { Content = "Balayage linéaire (bande étroite)", IsChecked = Banc.Ac.Lineaire, FontSize = Ui.Aide };
        ToolTip.SetTip(lin, "Pour une résonance très étroite (quartz : quelques hertz de large) : balayez linéairement une bande étroite autour de la fréquence, avec assez de points (ex. 32,70 k à 32,85 kHz, 20 000 points).");
        lin.IsCheckedChanged += (_, _) =>
        {
            Banc.Ac.Lineaire = lin.IsChecked == true;
            if (Banc.Ac.Lineaire && Banc.Ac.PointsParDecade < 1000) Banc.Ac.PointsParDecade = 10000;
            if (!Banc.Ac.Lineaire && Banc.Ac.PointsParDecade > 1000) Banc.Ac.PointsParDecade = 50;
            ResultatsPerimes();
            if (_bulleBanc?.IsOpen == true && _bulleBanc.Target is Control ancre) { _bulleBanc.Hide(); OuvrirReglagesBanc(ancre); }
        };

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
        void Rouvrir() { ResultatsPerimes(); if (_bulleBanc?.IsOpen == true && _bulleBanc.Target is Control anc) { _bulleBanc.Hide(); OuvrirReglagesBanc(anc); } }
        if (Banc.Type == AnalyseKind.Transitoire)
        {
            s.Children.Add(Ui.SubTitle("Transitoire (.tran)"));
            var auto = new CheckBox { Content = "Pas automatique (fin aux fronts)", IsChecked = Banc.Transitoire.Auto, FontSize = Ui.Aide };
            auto.IsCheckedChanged += (_, _) => { Banc.Transitoire.Auto = auto.IsChecked == true; Rouvrir(); };
            s.Children.Add(auto);
            if (Banc.Transitoire.Auto)
            {
                s.Children.Add(Ui.Field("Durée", SpiceNumber.Format(Banc.Transitoire.Duree, "s"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) Banc.Transitoire.Duree = x; }));
                s.Children.Add(Ui.Hint("ngspice adapte son pas : serré autour des fronts des stimuli, large sur les paliers. Tous ses points sont gardés."));
            }
            else s.Children.Add(tran);
        }
        else
        {
            s.Children.Add(Ui.SubTitle("Petit signal (.ac)"));
            var auto = new CheckBox { Content = "Échantillonnage automatique", IsChecked = Banc.Ac.Auto, FontSize = Ui.Aide };
            ToolTip.SetTip(auto, "Balayage grossier, puis points ajoutés là où le gain ou la phase varient vite ou cachent une résonance (coupures, quartz…).");
            auto.IsCheckedChanged += (_, _) => { Banc.Ac.Auto = auto.IsChecked == true; Rouvrir(); };
            s.Children.Add(auto);
            if (Banc.Ac.Auto)
            {
                var plage = new Grid { ColumnDefinitions = new("*,8,*") };
                plage.Children.Add(Ui.Field("De", SpiceNumber.Format(Banc.Ac.FDebut, "Hz"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) Banc.Ac.FDebut = x; }));
                var a2 = Ui.Field("À", SpiceNumber.Format(Banc.Ac.FFin, "Hz"), v => { if (SpiceNumber.ParseOrNull(v) is > 0 and var x) Banc.Ac.FFin = x; });
                Grid.SetColumn(a2, 2); plage.Children.Add(a2);
                s.Children.Add(plage);
                s.Children.Add(Ui.Hint("Points ajoutés automatiquement aux coupures et résonances, même très étroites."));
            }
            else { s.Children.Add(ac); s.Children.Add(lin); }
        }
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
        // OK / Annuler : les champs s'appliquent en direct ; Annuler rétablit l'état d'ouverture. Entrée = OK, Échap = Annuler.
        var (ref0, val0, tol0) = (c.Ref, c.Value, c.Tolerance);
        var ok = new Button { Content = "OK", Padding = new Thickness(14, 3), Background = Ui.AccentBrush, Foreground = Brushes.White, IsDefault = false };
        var annuler = new Button { Content = "Annuler", Padding = new Thickness(10, 3) };
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 2, 4, 4) };
        boutons.Children.Add(annuler); boutons.Children.Add(ok);
        var cadre = new StackPanel { Spacing = 4 }; cadre.Children.Add(_bulleContenu); cadre.Children.Add(boutons);
        _bulle = new Flyout { Content = cadre, Placement = PlacementMode.Pointer, ShowMode = FlyoutShowMode.Standard };
        var bulle = _bulle;
        void Valider() { ok.Focus(); bulle.Hide(); }   // le focus quitte le champ en cours : sa valeur est enregistrée
        void Annuler()
        {
            ok.Focus();
            if (c.Ref != ref0 || c.Value != val0) EditerComposant(c, ref0, val0);
            if (c.Tolerance != tol0) { c.Tolerance = tol0; OublierCampagne(); }
            bulle.Hide(); _status.Text = $"{c.Ref} : modification annulée.";
        }
        ok.Click += (_, _) => Valider(); annuler.Click += (_, _) => Annuler();
        cadre.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Annuler(); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        cadre.AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Enter && e.Source is TextBox) { e.Handled = true; Avalonia.Threading.Dispatcher.UIThread.Post(Valider); } }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        _bulleValider = Valider; _bulleAnnuler = Annuler;
        _bulle.ShowAt(_canvas, showAtPointer: true);
        // Première zone de saisie active : on tape directement la nouvelle valeur.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var tb = _bulleContenu.GetLogicalDescendants().OfType<TextBox>().Skip(c.Kind is ComponentKind.Label ? 0 : 1).FirstOrDefault();
            tb?.Focus(); tb?.SelectAll();
        });
    }
    public bool BulleOuverte => _bulle?.IsOpen == true;
    public string Statut => _status.Text ?? "";

    Action? _bulleValider, _bulleAnnuler;
    public IEnumerable<TextBox> ChampsBulle => _bulleContenu?.GetLogicalDescendants().OfType<TextBox>() ?? [];
    /// <summary>Boutons de la bulle d'édition (tests).</summary>
    public void ValiderBulle() => _bulleValider?.Invoke();
    public void AnnulerBulle() => _bulleAnnuler?.Invoke();
    public void ModifierComposantDeLaBulle(string reference, string valeur) { if (_bulleComposant != null) EditerComposant(_bulleComposant, reference, valeur); }

    /// <summary>Vrai pendant la reconstruction d'un panneau : les zones de saisie retirées perdent le focus et ne doivent rien réécrire.</summary>
    bool _reconstruction;
    /// <summary>Modifie la référence et la valeur du composant de la sélection, ou de la bulle (voir <see cref="EditerComposant"/>).</summary>
    void EditerSelection(string reference, string valeur) { if (_composantCible is { } c) EditerComposant(c, reference, valeur); }

    /// <summary>
    /// Modifie un composant. Composant sélectionné : par le canevas (annulable). Composant du circuit sous test ouvert depuis
    /// un banc (non sélectionnable, topologie verrouillée) : sa valeur change directement et vaut pour tous les bancs.
    /// </summary>
    void EditerComposant(PlacedComponent c, string reference, string valeur)
    {
        if (_reconstruction) return;
        if (c == _canvas.Selected) { _canvas.EditSelected(reference, valeur); return; }
        if (c.Ref == reference.Trim() && c.Value == valeur) return;
        c.Ref = reference.Trim(); c.Value = valeur;
        _results.Clear(); SetVerdict(null); _canvas.InvalidateVisual();
        RefreshGabarits(); RefreshBenchBar(); RefreshPlot();
        _status.Text = $"{c.Ref} = {c.Value} dans le circuit sous test (vaut pour tous les bancs).";
        ResultatsPerimes();
        if (_bulle?.IsOpen == true && _bulleContenu != null && _bulleComposant == c) { _cible = _bulleContenu; _composantCible = c; Proprietes(); _cible = _props; _composantCible = _canvas.Selected; }
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
            cb.SelectionChanged += (_, _) => EditerComposant(c, refBox.Text ?? c.Ref, cb.SelectedItem as string ?? c.Value);
            valueEditor = cb; value = () => cb.SelectedItem as string ?? c.Value;
        }
        else
        {
            var vb = new TextBox { Text = c.Value };
            vb.KeyDown += (_, e) => { if (e.Key == Key.Enter) EditerComposant(c, refBox.Text ?? c.Ref, vb.Text ?? ""); };
            vb.LostFocus += (_, _) => EditerComposant(c, refBox.Text ?? c.Ref, vb.Text ?? "");
            valueEditor = vb; value = () => vb.Text ?? "";
        }
        refBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) EditerComposant(c, refBox.Text ?? c.Ref, value()); };
        refBox.LostFocus += (_, _) => EditerComposant(c, refBox.Text ?? c.Ref, value());

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
        void Appliquer() => EditerComposant(c, refBox.Text ?? c.Ref, st.VersSpice());
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
            bool duCircuit = _etude.Schema.Components.Contains(c);
            _cible.Children.Add(Ui.Hint((duCircuit ? "Source du circuit sous test : présente dans tous les bancs (alimentation, polarisation). "
                : st.Forme == FormeStimulus.PetitSignal ? "Source petit signal : le banc devient fréquentiel (Bode) si c'est son premier stimulus. "
                : "Le banc est temporel si c'est son premier stimulus. ") + "Valeurs en notation ingénieur : 4,7k · 100n · 1meg." + (courant ? " Courant de la broche 1 vers la broche 2 dans la source." : "")));
        }
        _cible.Children.Add(new TextBlock { Text = "SPICE : " + st.VersSpice(), FontSize = 11, Foreground = Ui.Muted, FontFamily = new FontFamily("Menlo, Consolas, monospace"), TextWrapping = TextWrapping.Wrap });
        if (st.Forme == FormeStimulus.Continu) _cible.Children.Add(ChampTolerance(c));
    }

    /// <summary>Porte ou bascule XSPICE : tension d'alimentation de l'instance (seuils des ponts à 30 % / 70 %, sorties 0 / Vcc).</summary>
    void ProprietesLogique(PlacedComponent c, DefinitionComposant d, TextBox refBox)
    {
        var vcc = CircuitBuilder.TensionLogique(c);
        var tb = new TextBox { Text = SpiceNumber.Format(vcc, "V"), PlaceholderText = $"{SpiceNumber.Format(d.TensionLogique, "V")} (défaut du composant)" };
        void Commit()
        {
            var t = (tb.Text ?? "").Trim();
            string valeur = t.Length == 0 ? "" : SpiceNumber.ParseOrNull(t.TrimEnd('V', 'v').Trim()) is > 0 and <= 50 and var x
                ? (Math.Abs(x - d.TensionLogique) < 1e-9 ? "" : SpiceNumber.Format(x, "V").Replace(" ", "")) : c.Value;
            EditerComposant(c, refBox.Text ?? c.Ref, valeur);
        }
        foreach (var t in new[] { refBox, tb }) { t.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); }; t.LostFocus += (_, _) => Commit(); }
        _cible.Children.Add(Ui.Label("Référence")); _cible.Children.Add(refBox);
        _cible.Children.Add(Ui.Label("Tension d'alimentation logique")); _cible.Children.Add(tb);
        _cible.Children.Add(Ui.Hint($"Entrées : bas sous {SpiceNumber.Format(0.3 * vcc, "V")}, haut au-dessus de {SpiceNumber.Format(0.7 * vcc, "V")} (30 % / 70 %). Sorties : 0 V ou {SpiceNumber.Format(vcc, "V")} derrière 50 Ω. Ex. 3,3 · 5 · 12."));
    }

    void ProprietesSpice(PlacedComponent c)
    {
        var d = c.Definition;
        _cible.Children.Add(new TextBlock { Text = d == null ? "Composant SPICE" : $"{d.Nom} · {d.Description}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var refBox = new TextBox { Text = c.Ref };
        if (d?.CodeXspice != null) { ProprietesLogique(c, d, refBox); return; }
        var vb = new TextBox { Text = c.Value, PlaceholderText = d?.Type == TypeModele.SousCircuit ? d.Parametres : "" };
        void Commit() => EditerComposant(c, refBox.Text ?? c.Ref, vb.Text ?? "");
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
}
