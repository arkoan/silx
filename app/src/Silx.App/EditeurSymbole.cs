using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Silx.Etude;
namespace Silx.App;

/// <summary>
/// Éditeur de symbole d'un composant SPICE importé : gabarit de dessin, broches du symbole (glisser sur la grille),
/// et correspondance broche du modèle ↔ broche du symbole (cliquer l'une puis l'autre). Travaille sur une copie ;
/// <see cref="Resultat"/> est rempli quand on enregistre.
/// </summary>
public enum OutilDessin { Selection, Broche, Ligne, Rectangle, Triangle, Cercle, Arc }

public sealed class EditeurSymbole : Window
{
    /// <summary>Outil de dessin (symbole personnalisé).</summary>
    public OutilDessin Outil { get; private set; } = OutilDessin.Selection;
    /// <summary>Élément de dessin sélectionné (indice dans Symbole.Traits).</summary>
    public int? PrimitiveChoisie { get; set; }
    readonly Stack<Symbole> _annuler = new(), _retablir = new();
    readonly StackPanel _outils = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    readonly Dictionary<OutilDessin, Button> _boutonsOutils = new();
    readonly Border _texte;
    public DefinitionComposant Definition { get; }
    public DefinitionComposant? Resultat { get; private set; }
    readonly StackPanel _liste = new() { Spacing = 2 };
    readonly TextBlock _etat = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _ok;
    readonly Button _ajouter, _supprimer;
    readonly ComboBox _gabarit;
    readonly CheckBox _diode = new() { Content = "Diode de corps", FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Vue _vue;
    /// <summary>Broche du modèle choisie dans la liste, en attente d'une broche du symbole.</summary>
    public string? BrocheModeleChoisie { get; private set; }
    /// <summary>Indice de la broche du symbole sélectionnée.</summary>
    public int? BrocheSymboleChoisie { get; private set; }

    public EditeurSymbole(DefinitionComposant d)
    {
        Definition = d.Clone();
        Title = $"Symbole — {d.Nom}"; Width = 1200; Height = 620; MinWidth = 600; MinHeight = 380; Background = Ui.Window;
        _vue = new Vue(this);

        var nom = new TextBox { Text = Definition.Nom, FontSize = 12 };
        nom.TextChanged += (_, _) => Definition.Nom = nom.Text ?? "";
        var gauche = new StackPanel { Spacing = 6, Margin = new Thickness(10) };
        gauche.Children.Add(Ui.Labeled("Nom dans la palette", nom));
        gauche.Children.Add(Ui.Hint($"{Definition.Description} « {Definition.Modele} », lettre SPICE {Definition.Lettre}"));
        if (Definition.Notes.Length > 0 || Definition.Parametres.Length > 0)
        {
            // Ce que dit le fichier, pour s'y retrouver sans l'ouvrir.
            var infos = new StackPanel { Spacing = 3 };
            infos.Children.Add(Ui.Section("D'après le fichier"));
            if (Definition.Notes.Length > 0) infos.Children.Add(new TextBlock { Text = Definition.Notes, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            if (Definition.Parametres.Length > 0) infos.Children.Add(new TextBlock { Text = "Paramètres : " + Definition.Parametres, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted });
            var voir = new Button { Content = "Voir le texte SPICE", FontSize = 11.5, Padding = new Thickness(6, 2), Background = Brushes.Transparent, Foreground = Ui.AccentBrush };
            voir.Click += (_, _) => new Window { Title = $"Modèle {Definition.Modele}", Width = 640, Height = 520, Content = new TextBox { Text = Definition.TexteSpice, IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Menlo, Consolas, monospace"), FontSize = 12 } }.Show(this);
            infos.Children.Add(voir);
            gauche.Children.Add(new Border { Child = infos, Background = Brushes.White, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6) });
        }
        gauche.Children.Add(Ui.Section("Broches du modèle"));
        gauche.Children.Add(_liste);
        gauche.Children.Add(Ui.Hint("Cliquez une broche du modèle, puis la broche du symbole qui la représente. Recliquer une broche reliée la délie."));
        var left = new Border { Width = 270, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = gauche } };

        _gabarit = new ComboBox { ItemsSource = Enum.GetValues<GabaritSymbole>().Select(GabaritsSymboles.Libelle).ToList(), SelectedIndex = (int)Definition.Symbole.Gabarit, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_gabarit, "Gabarit de dessin : remplace le symbole et relie les broches par leur nom");
        _gabarit.SelectionChanged += (_, _) => { if (_gabarit.SelectedIndex >= 0 && _gabarit.SelectedIndex != (int)Definition.Symbole.Gabarit) ChoisirGabarit((GabaritSymbole)_gabarit.SelectedIndex); };
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4), ItemSpacing = 2 };
        bar.Children.Add(Icones.Make(Icones.Symbole, 16, Ui.Muted)); bar.Children.Add(_gabarit);
        _diode.IsCheckedChanged += (_, _) => DiodeDeCorps(_diode.IsChecked == true);
        ToolTip.SetTip(_diode, "Dessiner la diode de corps entre drain et source (le modèle SPICE n'est pas modifié)");
        bar.Children.Add(_diode);
        bar.Children.Add(Icones.Button(Icones.Crayon, "Dessiner un symbole personnalisé (part du dessin actuel)", () => ChoisirGabarit(GabaritSymbole.Personnalise)));
        foreach (var (o, ic, tip) in new[] {
            (OutilDessin.Selection, Icones.Selection, "Sélectionner, déplacer (Suppr pour effacer)"), (OutilDessin.Broche, Icones.OutilBroche, "Ajouter une broche (clic sur un point de grille)"),
            (OutilDessin.Ligne, Icones.OutilLigne, "Ligne (glisser)"), (OutilDessin.Rectangle, Icones.OutilRectangle, "Rectangle (glisser d'un coin à l'autre)"),
            (OutilDessin.Triangle, Icones.OutilTriangle, "Triangle (glisser : base au départ, pointe à l'arrivée)"), (OutilDessin.Cercle, Icones.OutilCercle, "Cercle (glisser du centre vers le bord)"),
            (OutilDessin.Arc, Icones.OutilArc, "Arc : demi-cercle, glisser d'une extrémité à l'autre") })
        {
            var oo = o; var b = Icones.Button(ic, tip, () => ChoisirOutil(oo), 16);
            _boutonsOutils[o] = b; _outils.Children.Add(b);
        }
        _outils.Children.Add(Icones.Button(Icones.Croix, "Effacer tout le dessin (les broches restent)", () => { Memo(); Definition.Symbole.Traits.Clear(); Definition.Symbole.Corps = null; PrimitiveChoisie = null; Rafraichir(); }, 14));
        bar.Children.Add(_outils);
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.Annuler, "Annuler (⌘Z)", Annuler, 16));
        bar.Children.Add(Icones.Button(Icones.Retablir, "Rétablir (⇧⌘Z)", Retablir, 16));
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.Relier, "Relier automatiquement par nom les broches encore libres", () => { Definition.RelierAutomatiquement(); Rafraichir(); }));
        _ajouter = Icones.Button(Icones.AjouterBroche, "Ajouter une broche au symbole (boîte)", () => { BrocheSymboleChoisie = Definition.Symbole.Broches.IndexOf(GabaritsSymboles.AjouterBroche(Definition.Symbole)); Lier(); Rafraichir(); });
        _supprimer = Icones.Button(Icones.Supprimer, "Supprimer la broche sélectionnée du symbole (Suppr)", SupprimerBroche);
        bar.Children.Add(_ajouter); bar.Children.Add(_supprimer);
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.ToutVoir, "Recadrer", () => _vue.InvalidateVisual()));
        bar.Children.Add(Icones.Button(Icones.TexteModele, "Afficher / masquer le texte du modèle SPICE", () => _texte!.IsVisible = !_texte.IsVisible));
        var center = new DockPanel { ClipToBounds = true };
        var barB = new Border { Child = bar, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = Ui.Panel };
        DockPanel.SetDock(barB, Dock.Top); center.Children.Add(barB); center.Children.Add(_vue);

        var annuler = new Button { Content = "Annuler", Padding = new Thickness(12, 4) };
        annuler.Click += (_, _) => Close();
        _ok = new Button { Content = "Enregistrer", Padding = new Thickness(12, 4), Background = Ui.AccentBrush, Foreground = Brushes.White };
        _ok.Click += (_, _) => Enregistrer();
        var foot = new DockPanel { Margin = new Thickness(10, 6) };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; btns.Children.Add(annuler); btns.Children.Add(_ok);
        DockPanel.SetDock(btns, Dock.Right); foot.Children.Add(btns); foot.Children.Add(_etat);
        var footB = new Border { Child = foot, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0) };

        // Texte du modèle, à côté du symbole : on s'y retrouve sans ouvrir le fichier.
        var txt = new TextBox { Text = Definition.TexteSpice, IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Menlo, Consolas, monospace"), FontSize = 11.5, TextWrapping = TextWrapping.NoWrap, BorderThickness = new Thickness(0), Background = Brushes.White };
        var txtDock = new DockPanel();
        var txtHead = new TextBlock { Text = $"Modèle SPICE · {Definition.Modele}", FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new Thickness(10, 6) };
        DockPanel.SetDock(txtHead, Dock.Top); txtDock.Children.Add(txtHead); txtDock.Children.Add(txt);
        _texte = new Border { Width = 360, Child = txtDock, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 0, 0) };

        var root = new DockPanel();
        DockPanel.SetDock(footB, Dock.Bottom); root.Children.Add(footB);
        DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        DockPanel.SetDock(_texte, Dock.Right); root.Children.Add(_texte);
        root.Children.Add(center);
        Content = root;
        KeyDown += (_, e) => {
            bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (cmd && e.Key == Key.Z) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Retablir(); else Annuler(); e.Handled = true; }
            else if (e.Key is Key.Delete or Key.Back && FocusManager?.GetFocusedElement() is not TextBox) { SupprimerSelection(); e.Handled = true; } else if (e.Key == Key.Escape) { BrocheModeleChoisie = null; BrocheSymboleChoisie = null; PrimitiveChoisie = null; Outil = OutilDessin.Selection; Rafraichir(); } };
        Rafraichir();
    }

    // ── dessin libre ──
    public void Memo() { _annuler.Push(Definition.Symbole.Clone()); _retablir.Clear(); }
    public void Annuler() { if (_annuler.Count == 0) return; _retablir.Push(Definition.Symbole.Clone()); Definition.Symbole = _annuler.Pop(); PrimitiveChoisie = null; BrocheSymboleChoisie = null; _gabarit.SelectedIndex = (int)Definition.Symbole.Gabarit; Rafraichir(); }
    public void Retablir() { if (_retablir.Count == 0) return; _annuler.Push(Definition.Symbole.Clone()); Definition.Symbole = _retablir.Pop(); PrimitiveChoisie = null; _gabarit.SelectedIndex = (int)Definition.Symbole.Gabarit; Rafraichir(); }

    public void ChoisirOutil(OutilDessin o)
    {
        if (Definition.Symbole.Gabarit != GabaritSymbole.Personnalise && o != OutilDessin.Selection) ChoisirGabarit(GabaritSymbole.Personnalise);
        Outil = o; PrimitiveChoisie = null; Rafraichir();
    }

    /// <summary>Ajoute l'élément tracé de (x0,y0) à (x1,y1), en pas de grille.</summary>
    public void Tracer(OutilDessin o, double x0, double y0, double x1, double y1)
    {
        if (Math.Abs(x1 - x0) < 1e-9 && Math.Abs(y1 - y0) < 1e-9) return;
        var p = new EditeurApercu(o, (x0, y0), (x1, y1)).Primitive();
        if (p == null) return;
        Memo(); Definition.Symbole.Gabarit = GabaritSymbole.Personnalise;
        Definition.Symbole.Traits.Add(p); PrimitiveChoisie = Definition.Symbole.Traits.Count - 1; Rafraichir();
    }

    /// <summary>Ajoute une broche (libre, à relier) en un point de grille.</summary>
    public void AjouterBrocheEn(int x, int y)
    {
        if (Definition.Symbole.Broches.Any(b => b.X == x && b.Y == y)) return;
        Memo(); Definition.Symbole.Broches.Add(new BrocheSymbole { X = x, Y = y });
        BrocheSymboleChoisie = Definition.Symbole.Broches.Count - 1; Lier(); Rafraichir();
    }

    public void DeplacerPrimitive(int i, Primitive origine, double dx, double dy)
    {
        var p = origine.Clone();
        p.X1 += dx; p.Y1 += dy;
        if (p.Forme != FormePrimitive.Cercle) { p.X2 += dx; p.Y2 += dy; }
        if (p.Points != null) foreach (var q in p.Points) { q[0] += dx; q[1] += dy; }
        Definition.Symbole.Traits[i] = p; _vue.InvalidateVisual();
    }

    public void SupprimerSelection()
    {
        if (PrimitiveChoisie is { } i && i < Definition.Symbole.Traits.Count) { Memo(); Definition.Symbole.Traits.RemoveAt(i); PrimitiveChoisie = null; Rafraichir(); }
        else SupprimerBroche();
    }

    /// <summary>Élément de dessin le plus proche de (x, y) en pas de grille, à 0,35 pas près.</summary>
    public int? PrimitiveEn(double x, double y)
    {
        static double Seg(double px, double py, double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
            double t = l2 < 1e-12 ? 0 : Math.Clamp(((px - ax) * vx + (py - ay) * vy) / l2, 0, 1);
            double cx = ax + t * vx - px, cy = ay + t * vy - py; return Math.Sqrt(cx * cx + cy * cy);
        }
        double Dist(Primitive p) => p.Forme switch
        {
            FormePrimitive.Trait or FormePrimitive.Fleche => Seg(x, y, p.X1, p.Y1, p.X2, p.Y2),
            FormePrimitive.Rectangle => new[] { Seg(x, y, p.X1, p.Y1, p.X2, p.Y1), Seg(x, y, p.X2, p.Y1, p.X2, p.Y2), Seg(x, y, p.X2, p.Y2, p.X1, p.Y2), Seg(x, y, p.X1, p.Y2, p.X1, p.Y1) }.Min(),
            FormePrimitive.Cercle => Math.Abs(Math.Sqrt((x - p.X1) * (x - p.X1) + (y - p.Y1) * (y - p.Y1)) - p.X2),
            FormePrimitive.Polygone when p.Points is { Count: > 1 } pts => Enumerable.Range(0, pts.Count).Min(k => Seg(x, y, pts[k][0], pts[k][1], pts[(k + 1) % pts.Count][0], pts[(k + 1) % pts.Count][1])),
            FormePrimitive.Arc => p.PointsArc().Zip(p.PointsArc().Skip(1)).Min(s => Seg(x, y, s.First.X, s.First.Y, s.Second.X, s.Second.Y)),
            FormePrimitive.Texte => Math.Abs(x - p.X1 - 1) < 1.2 && Math.Abs(y - p.Y1 - 0.3) < 0.6 ? 0 : 99,
            _ => 99
        };
        var tr = Definition.Symbole.Traits;
        int? best = null; double bd = 0.35;
        for (int k = tr.Count - 1; k >= 0; k--) { var dd = Dist(tr[k]); if (dd < bd) { bd = dd; best = k; } }
        return best;
    }

    // ── actions (publiques pour les tests) ──
    public void ChoisirGabarit(GabaritSymbole g)
    {
        if (g == Definition.Symbole.Gabarit && _gabarit.SelectedIndex == (int)g) return;
        Memo();
        Definition.AppliquerGabarit(g, Definition.Symbole.DiodeCorps);
        if (g != GabaritSymbole.Personnalise) Outil = OutilDessin.Selection;
        BrocheSymboleChoisie = null; _gabarit.SelectedIndex = (int)g; Rafraichir();
    }

    /// <summary>MOS 3 broches : afficher ou masquer la diode de corps (dessin seulement : le modèle ne change pas).</summary>
    public void DiodeDeCorps(bool oui)
    {
        if (Definition.Symbole.Gabarit is not (GabaritSymbole.Nmos3 or GabaritSymbole.Pmos3) || Definition.Symbole.DiodeCorps == oui) return;
        Definition.AppliquerGabarit(Definition.Symbole.Gabarit, oui); Rafraichir();
    }

    public void ChoisirBrocheModele(string? n)
    {
        BrocheModeleChoisie = BrocheModeleChoisie == n ? null : n;
        Lier(); Rafraichir();
    }

    public void ChoisirBrocheSymbole(int? i)
    {
        if (i is { } k && BrocheModeleChoisie == null && BrocheSymboleChoisie == k && Definition.Symbole.Broches[k].BrocheModele != null)
            Definition.Symbole.Broches[k].BrocheModele = null;  // recliquer une broche reliée la délie
        else BrocheSymboleChoisie = i;
        Lier(); Rafraichir();
    }

    /// <summary>Relie la broche du modèle choisie à la broche du symbole choisie (une broche du modèle n'a qu'une broche de symbole).</summary>
    void Lier()
    {
        if (BrocheModeleChoisie is not { } n || BrocheSymboleChoisie is not { } i || i >= Definition.Symbole.Broches.Count) return;
        foreach (var b in Definition.Symbole.Broches.Where(b => string.Equals(b.BrocheModele, n, StringComparison.OrdinalIgnoreCase))) b.BrocheModele = null;
        Definition.Symbole.Broches[i].BrocheModele = n;
        Definition.BrochesCachees.Remove(n);
        BrocheModeleChoisie = null; BrocheSymboleChoisie = null;
    }

    /// <summary>Raccordement d'une broche du modèle absente du symbole : 0 = sur le symbole (à relier), 1 = masse, 2 = tension fixe.</summary>
    public void RaccorderCachee(string broche, int mode, string valeur)
    {
        if (mode == 0) Definition.BrochesCachees.Remove(broche);
        else
        {
            foreach (var b in Definition.Symbole.Broches.Where(b => string.Equals(b.BrocheModele, broche, StringComparison.OrdinalIgnoreCase))) b.BrocheModele = null;
            Definition.BrochesCachees[broche] = mode == 1 ? "0" : "DC " + (SpiceNumber.TryParse(valeur, out var v) ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0");
        }
        Rafraichir();
    }

    public void DeplacerBroche(int i, int x, int y)
    {
        var b = Definition.Symbole.Broches[i];
        if (Definition.Symbole.Broches.Any(o => o != b && o.X == x && o.Y == y)) return;
        b.X = x; b.Y = y; Rafraichir();
    }

    void SupprimerBroche()
    {
        if (BrocheSymboleChoisie is not { } i || i >= Definition.Symbole.Broches.Count) return;
        Definition.Symbole.Broches.RemoveAt(i); BrocheSymboleChoisie = null; Rafraichir();
    }

    public void Enregistrer()
    {
        if (Definition.Verifier().Any(p => p.Severity == Severity.Error)) return;
        Resultat = Definition; Close(Resultat);
    }

    void Rafraichir()
    {
        _liste.Children.Clear();
        foreach (var n in Definition.BrochesModele)
        {
            var idx = Definition.Symbole.Broches.FindIndex(b => string.Equals(b.BrocheModele, n, StringComparison.OrdinalIgnoreCase));
            int count = Definition.Symbole.Broches.Count(b => string.Equals(b.BrocheModele, n, StringComparison.OrdinalIgnoreCase));
            var row = new Grid { ColumnDefinitions = new("18,*,Auto") };
            var ok = count == 1;
            var mark = new TextBlock { Text = ok ? "✓" : "!", Foreground = ok ? Ui.PassBrush : Ui.FailBrush, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
            bool cachee = Definition.BrochesCachees.TryGetValue(n, out var cx);
            if (cachee && count == 0) ok = true;
            mark.Text = ok ? "✓" : "!"; mark.Foreground = ok ? Ui.PassBrush : Ui.FailBrush;
            var name = new TextBlock { Text = Definition.NomAffiche(n), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var where = new TextBlock { Text = idx >= 0 ? $"broche {idx + 1}" : cachee ? (cx!.Trim() == "0" ? "masse" : cx.Trim()) : "à relier", Foreground = ok ? Ui.Muted : Ui.FailBrush, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(mark); row.Children.Add(name); Grid.SetColumn(name, 1); row.Children.Add(where); Grid.SetColumn(where, 2);
            var b = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 3), CornerRadius = new CornerRadius(5), Background = BrocheModeleChoisie == n ? Ui.ActiveBg : idx >= 0 && idx == BrocheSymboleChoisie ? new SolidColorBrush(Color.Parse("#fef3c7")) : Brushes.Transparent };
            ToolTip.SetTip(b, $"Broche « {n} » du modèle (position {Definition.BrochesModele.IndexOf(n) + 1} sur la ligne SPICE)");
            var nn = n; b.Click += (_, _) => ChoisirBrocheModele(nn);
            _liste.Children.Add(b);
            if (idx < 0)
            {
                // Broche absente du symbole : à relier sur le symbole, ou raccordée d'office (masse, tension fixe).
                var mode = new ComboBox { ItemsSource = new[] { "sur le symbole", "à la masse", "tension fixe" }, FontSize = 11.5, MinHeight = 24, Padding = new Thickness(6, 1),
                    SelectedIndex = !cachee ? 0 : cx!.Trim() == "0" ? 1 : 2 };
                var val = new TextBox { Text = cachee && cx!.Trim() != "0" ? cx.Trim()[3..].Trim() : "0", Width = 54, FontSize = 11.5, MinHeight = 24, Padding = new Thickness(4, 1), IsVisible = mode.SelectedIndex == 2 };
                ToolTip.SetTip(val, "Tension continue en V (ex. 25 pour une broche de température en °C)");
                mode.SelectionChanged += (_, _) => RaccorderCachee(nn, mode.SelectedIndex, val.Text ?? "0");
                val.LostFocus += (_, _) => RaccorderCachee(nn, 2, val.Text ?? "0");
                val.KeyDown += (_, e) => { if (e.Key == Key.Enter) RaccorderCachee(nn, 2, val.Text ?? "0"); };
                var r2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(24, 0, 0, 4) };
                r2.Children.Add(mode); r2.Children.Add(val);
                if (val.IsVisible) r2.Children.Add(new TextBlock { Text = "V", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
                _liste.Children.Add(r2);
            }
        }
        var pb = Definition.Verifier();
        var err = pb.FirstOrDefault(p => p.Severity == Severity.Error);
        _etat.Text = BrocheModeleChoisie is { } c ? $"Cliquez la broche du symbole qui représente « {c} »."
            : Definition.Symbole.Gabarit == GabaritSymbole.Personnalise && Outil != OutilDessin.Selection && err == null ? Outil switch
            {
                OutilDessin.Broche => "Cliquez sur un point de grille pour ajouter une broche, puis reliez-la à une broche du modèle.",
                OutilDessin.Cercle => "Glissez du centre vers le bord.", OutilDessin.Arc => "Glissez d'une extrémité de l'arc à l'autre (demi-cercle, bombé à gauche du tracé).",
                OutilDessin.Triangle => "Glissez de la base vers la pointe.", _ => "Glissez pour tracer. Échap : outil Sélection."
            }
            : err != null ? "⚠ " + err.Message
            : pb.FirstOrDefault() is { } w ? "ⓘ " + w.Message
            : "✓ Toutes les broches du modèle sont reliées.";
        _etat.Foreground = BrocheModeleChoisie != null ? Ui.AccentBrush : err != null ? Ui.FailBrush : pb.Count > 0 ? Ui.WarnBrush : Ui.PassBrush;
        _ok.IsEnabled = err == null;
        _ajouter.IsVisible = Definition.Symbole.Gabarit == GabaritSymbole.Boite;
        _diode.IsVisible = Definition.Symbole.Gabarit is GabaritSymbole.Nmos3 or GabaritSymbole.Pmos3;
        _outils.IsVisible = Definition.Symbole.Gabarit == GabaritSymbole.Personnalise;
        foreach (var (o, b) in _boutonsOutils) Ui.SetActive(b, Outil == o);
        _diode.IsChecked = Definition.Symbole.DiodeCorps;
        _supprimer.IsEnabled = BrocheSymboleChoisie != null;
        _vue.InvalidateVisual();
    }

    /// <summary>Aperçu du symbole sur la grille : clic pour choisir une broche, glisser pour la déplacer.</summary>
    sealed class Vue : Control
    {
        readonly EditeurSymbole _e;
        int? _drag; bool _moved; int? _hover;
        // Dessin : point de départ et point courant (demi-pas de grille) ; déplacement d'un élément.
        (double X, double Y)? _p0, _p1; int? _dragPrim; Primitive? _primOrigine; (double X, double Y) _primDepart;
        public Vue(EditeurSymbole e) { _e = e; ClipToBounds = true; Focusable = true; }
        Symbole S => _e.Definition.Symbole;

        (double D, Point O) Cadre()
        {
            var (x0, y0, x1, y1) = S.Encombrement();
            x0 -= 4; y0 -= 2; x1 += 4; y1 += 2;   // place pour les noms de broches
            var d = Math.Clamp(Math.Min(Bounds.Width / (x1 - x0), Bounds.Height / (y1 - y0)), 8, 46);
            return (d, new Point(Bounds.Width / 2 - (x0 + x1) / 2 * d, Bounds.Height / 2 - (y0 + y1) / 2 * d));
        }
        Point Ecran(double x, double y) { var (d, o) = Cadre(); return new(o.X + x * d, o.Y + y * d); }
        (int X, int Y) Grille(Point p) { var (d, o) = Cadre(); return ((int)Math.Round((p.X - o.X) / d), (int)Math.Round((p.Y - o.Y) / d)); }
        (double X, double Y) DemiGrille(Point p) { var (d, o) = Cadre(); return (Math.Round((p.X - o.X) / d * 2) / 2, Math.Round((p.Y - o.Y) / d * 2) / 2); }
        (double X, double Y) Reel(Point p) { var (d, o) = Cadre(); return ((p.X - o.X) / d, (p.Y - o.Y) / d); }
        bool Libre => _e.Definition.Symbole.Gabarit == GabaritSymbole.Personnalise;
        int? Broche(Point p)
        {
            var (d, _) = Cadre();
            for (int i = S.Broches.Count - 1; i >= 0; i--)
            { var q = Ecran(S.Broches[i].X, S.Broches[i].Y); if (Math.Abs(q.X - p.X) < d * .45 && Math.Abs(q.Y - p.Y) < d * .45) return i; }
            return null;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            Focus();
            var pos = e.GetPosition(this);
            if (Libre && _e.Outil == OutilDessin.Broche) { var (x, y) = Grille(pos); _e.AjouterBrocheEn(x, y); return; }
            if (Libre && _e.Outil != OutilDessin.Selection) { _p0 = _p1 = DemiGrille(pos); InvalidateVisual(); return; }
            var i = Broche(pos);
            _drag = i; _moved = false;
            if (i != null) { _e.PrimitiveChoisie = null; return; }
            var (rx, ry) = Reel(pos);
            if (Libre && _e.PrimitiveEn(rx, ry) is { } k)
            {
                _e.Memo(); _e.PrimitiveChoisie = k; _dragPrim = k; _primOrigine = _e.Definition.Symbole.Traits[k].Clone(); _primDepart = DemiGrille(pos);
                _e.ChoisirBrocheSymbole(null); return;
            }
            _e.PrimitiveChoisie = null;
            _e.ChoisirBrocheSymbole(null);
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_p0 != null) { _p1 = DemiGrille(p); InvalidateVisual(); return; }
            if (_dragPrim is { } k && _primOrigine != null)
            {
                var (x, y) = DemiGrille(p);
                _e.DeplacerPrimitive(k, _primOrigine, x - _primDepart.X, y - _primDepart.Y); return;
            }
            if (_drag is { } i)
            {
                var (x, y) = Grille(p); var b = S.Broches[i];
                if (x != b.X || y != b.Y) { if (!_moved) _e.Memo(); _moved = true; _e.BrocheSymboleChoisie = i; _e.DeplacerBroche(i, x, y); }
            }
            var h = Broche(p); if (h != _hover) { _hover = h; InvalidateVisual(); }
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_p0 is { } a && _p1 is { } b) { _p0 = _p1 = null; _e.Tracer(_e.Outil, a.X, a.Y, b.X, b.Y); return; }
            if (_dragPrim != null) { _dragPrim = null; _primOrigine = null; InvalidateVisual(); return; }
            if (_drag is { } i && !_moved) _e.ChoisirBrocheSymbole(i);
            _drag = null;
        }

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));
            var (d, o) = Cadre();
            var dot = new SolidColorBrush(Color.Parse("#c9ccd3"));
            for (double x = o.X % d; x < Bounds.Width; x += d) for (double y = o.Y % d; y < Bounds.Height; y += d) ctx.FillRectangle(dot, new Rect(x - .75, y - .75, 1.5, 1.5));
            FormattedText T(string s, double size, IBrush b, FontWeight w) => new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, w), size * d / 24 + 5, b);
            Symboles.DessinerSymbole(ctx, S, Ecran, d, Ui.Ink, new Pen(Ui.Ink, Math.Max(1.3, d / 14)), T);
            var acc = new Pen(Ui.AccentBrush, Math.Max(1.3, d / 14));   // même épaisseur que le dessin : seule la couleur signale la sélection
            if (_e.PrimitiveChoisie is { } ps && ps < S.Traits.Count)
                Symboles.DessinerSymbole(ctx, new Symbole { Traits = [S.Traits[ps]] }, Ecran, d, Ui.AccentBrush, acc, T);
            if (_p0 is { } a0 && _p1 is { } a1)
            {
                // Aperçu de l'élément en cours de tracé.
                var tmp = new EditeurApercu(_e.Outil, a0, a1).Primitive();
                if (tmp != null) Symboles.DessinerSymbole(ctx, new Symbole { Traits = [tmp] }, Ecran, d, Ui.AccentBrush, new Pen(Ui.AccentBrush, Math.Max(1.5, d / 12), new DashStyle([4, 3], 0)), T);
            }
            for (int i = 0; i < S.Broches.Count; i++)
            {
                var b = S.Broches[i]; var p = Ecran(b.X, b.Y);
                bool sel = _e.BrocheSymboleChoisie == i, hov = _hover == i, lie = b.BrocheModele != null;
                var col = sel ? Ui.AccentBrush : lie ? new SolidColorBrush(Color.Parse("#1f6f43")) : Ui.FailBrush;
                var r = d * (sel || hov ? .32 : .24);
                ctx.DrawEllipse(sel ? new SolidColorBrush(Color.Parse("#dbe6fe")) : Brushes.White, new Pen(col, 2), p, r, r);
                var t = new FormattedText($"{i + 1}" + (lie ? " · " + _e.Definition.NomAffiche(b.BrocheModele!) : " · ?"), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 11.5, col);
                // Étiquette du côté extérieur du symbole.
                var (x0, y0, x1, y1) = S.Encombrement(); double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
                double dx = b.X - cx, dy = b.Y - cy;
                var at = Math.Abs(dx) >= Math.Abs(dy)
                    ? new Point(dx >= 0 ? p.X + r + 4 : p.X - r - 4 - t.Width, p.Y - t.Height - 2)
                    : new Point(p.X + r + 4, dy >= 0 ? p.Y + 2 : p.Y - t.Height - 2);
                ctx.DrawRectangle(new SolidColorBrush(Colors.White, 0.85), null, new Rect(at, new Size(t.Width, t.Height)));
                ctx.DrawText(t, at);
            }
        }
    }
}

/// <summary>Élément que produirait un tracé (pour l'aperçu, mêmes règles que <see cref="EditeurSymbole.Tracer"/>).</summary>
readonly record struct EditeurApercu(OutilDessin Outil, (double X, double Y) A, (double X, double Y) B)
{
    public Primitive? Primitive()
    {
        var (x0, y0) = A; var (x1, y1) = B;
        return Outil switch
        {
            OutilDessin.Ligne => Silx.Etude.Primitive.Trait(x0, y0, x1, y1),
            OutilDessin.Rectangle => Silx.Etude.Primitive.Rect(x0, y0, x1, y1),
            OutilDessin.Cercle => Silx.Etude.Primitive.Cercle(x0, y0, Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0))),
            OutilDessin.Arc => Silx.Etude.Primitive.ArcDe(x0, y0, x1, y1),
            OutilDessin.Triangle => Math.Abs(x1 - x0) >= Math.Abs(y1 - y0)
                ? Silx.Etude.Primitive.Poly(false, x0, y0 - Math.Abs(x1 - x0) / 2, x0, y0 + Math.Abs(x1 - x0) / 2, x1, y0)
                : Silx.Etude.Primitive.Poly(false, x0 - Math.Abs(y1 - y0) / 2, y0, x0 + Math.Abs(y1 - y0) / 2, y0, x0, y1),
            _ => null
        };
    }
}
