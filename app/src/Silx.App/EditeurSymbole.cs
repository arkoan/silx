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
public sealed class EditeurSymbole : Window
{
    public DefinitionComposant Definition { get; }
    public DefinitionComposant? Resultat { get; private set; }
    readonly StackPanel _liste = new() { Spacing = 2 };
    readonly TextBlock _etat = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _ok;
    readonly Button _ajouter, _supprimer;
    readonly ComboBox _gabarit;
    readonly Vue _vue;
    /// <summary>Broche du modèle choisie dans la liste, en attente d'une broche du symbole.</summary>
    public string? BrocheModeleChoisie { get; private set; }
    /// <summary>Indice de la broche du symbole sélectionnée.</summary>
    public int? BrocheSymboleChoisie { get; private set; }

    public EditeurSymbole(DefinitionComposant d)
    {
        Definition = d.Clone();
        Title = $"Symbole — {d.Nom}"; Width = 860; Height = 560; MinWidth = 600; MinHeight = 380; Background = Ui.Window;
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
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 4) };
        bar.Children.Add(Icones.Make(Icones.Symbole, 16, Ui.Muted)); bar.Children.Add(_gabarit);
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.Relier, "Relier automatiquement par nom les broches encore libres", () => { Definition.RelierAutomatiquement(); Rafraichir(); }));
        _ajouter = Icones.Button(Icones.AjouterBroche, "Ajouter une broche au symbole (boîte)", () => { BrocheSymboleChoisie = Definition.Symbole.Broches.IndexOf(GabaritsSymboles.AjouterBroche(Definition.Symbole)); Lier(); Rafraichir(); });
        _supprimer = Icones.Button(Icones.Supprimer, "Supprimer la broche sélectionnée du symbole (Suppr)", SupprimerBroche);
        bar.Children.Add(_ajouter); bar.Children.Add(_supprimer);
        bar.Children.Add(Ui.Separator());
        bar.Children.Add(Icones.Button(Icones.ToutVoir, "Recadrer", () => _vue.InvalidateVisual()));
        var center = new DockPanel();
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

        var root = new DockPanel();
        DockPanel.SetDock(footB, Dock.Bottom); root.Children.Add(footB);
        DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);
        root.Children.Add(center);
        Content = root;
        KeyDown += (_, e) => { if (e.Key is Key.Delete or Key.Back && FocusManager?.GetFocusedElement() is not TextBox) { SupprimerBroche(); e.Handled = true; } else if (e.Key == Key.Escape) { BrocheModeleChoisie = null; BrocheSymboleChoisie = null; Rafraichir(); } };
        Rafraichir();
    }

    // ── actions (publiques pour les tests) ──
    public void ChoisirGabarit(GabaritSymbole g)
    {
        Definition.Symbole = GabaritsSymboles.Creer(g, Definition.BrochesModele);
        if (g == GabaritSymbole.Boite) Definition.RelierAutomatiquement();
        BrocheSymboleChoisie = null; _gabarit.SelectedIndex = (int)g; Rafraichir();
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
            : err != null ? "⚠ " + err.Message
            : pb.FirstOrDefault() is { } w ? "ⓘ " + w.Message
            : "✓ Toutes les broches du modèle sont reliées.";
        _etat.Foreground = BrocheModeleChoisie != null ? Ui.AccentBrush : err != null ? Ui.FailBrush : pb.Count > 0 ? Ui.WarnBrush : Ui.PassBrush;
        _ok.IsEnabled = err == null;
        _ajouter.IsVisible = Definition.Symbole.Gabarit == GabaritSymbole.Boite;
        _supprimer.IsEnabled = BrocheSymboleChoisie != null;
        _vue.InvalidateVisual();
    }

    /// <summary>Aperçu du symbole sur la grille : clic pour choisir une broche, glisser pour la déplacer.</summary>
    sealed class Vue : Control
    {
        readonly EditeurSymbole _e;
        int? _drag; bool _moved; int? _hover;
        public Vue(EditeurSymbole e) { _e = e; ClipToBounds = true; Focusable = true; }
        Symbole S => _e.Definition.Symbole;

        (double D, Point O) Cadre()
        {
            var (x0, y0, x1, y1) = S.Encombrement();
            x0 -= 2; y0 -= 2; x1 += 2; y1 += 2;
            var d = Math.Clamp(Math.Min(Bounds.Width / (x1 - x0), Bounds.Height / (y1 - y0)), 8, 46);
            return (d, new Point(Bounds.Width / 2 - (x0 + x1) / 2 * d, Bounds.Height / 2 - (y0 + y1) / 2 * d));
        }
        Point Ecran(double x, double y) { var (d, o) = Cadre(); return new(o.X + x * d, o.Y + y * d); }
        (int X, int Y) Grille(Point p) { var (d, o) = Cadre(); return ((int)Math.Round((p.X - o.X) / d), (int)Math.Round((p.Y - o.Y) / d)); }
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
            var i = Broche(e.GetPosition(this));
            _drag = i; _moved = false;
            if (i == null) _e.ChoisirBrocheSymbole(null);
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag is { } i)
            {
                var (x, y) = Grille(p); var b = S.Broches[i];
                if (x != b.X || y != b.Y) { _moved = true; _e.BrocheSymboleChoisie = i; _e.DeplacerBroche(i, x, y); }
            }
            var h = Broche(p); if (h != _hover) { _hover = h; InvalidateVisual(); }
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
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
