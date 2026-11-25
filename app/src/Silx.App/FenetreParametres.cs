using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
namespace Silx.App;

/// <summary>
/// Fenêtre Paramètres, à la manière des Réglages de macOS : sections à gauche, réglages de la section à droite,
/// groupés dans des cartes. Chaque changement est appliqué et enregistré tout de suite.
/// </summary>
public sealed class FenetreParametres : Window
{
    public enum Section { General, Affichage, Navigation, Composants }
    static readonly (Section S, string Nom, string Icone)[] Sections =
    [
        (Section.General, "Général", Icones.Reglages),
        (Section.Affichage, "Affichage", Icones.VueMixte),
        (Section.Navigation, "Zoom et navigation", Icones.ZoomPlus),
        (Section.Composants, "Composants", Icones.OutilBroche),
    ];
    public static readonly string[] Nuancier = ["#7c3aed", "#2563eb", "#0891b2", "#15803d", "#ca8a04", "#ea580c", "#b91c1c", "#db2777", "#475569"];

    readonly Parametres _p = Parametres.Courant;
    readonly Action _changement;
    readonly StackPanel _liste = new() { Spacing = 2, Margin = new Thickness(10, 14) };
    readonly ContentControl _contenu = new();
    public Section Active { get; private set; }

    /// <param name="changement">Appelé après chaque réglage modifié (rafraîchir les vues).</param>
    public FenetreParametres(Action changement, string dossierComposants)
    {
        _changement = changement; _dossier = dossierComposants;
        Title = "Paramètres"; Width = 760; Height = 500; MinWidth = 620; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.Surface;
        var g = new Grid { ColumnDefinitions = new("200,*") };
        var gauche = new Border { Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = _liste };
        g.Children.Add(gauche);
        var droite = new ScrollViewer { Content = _contenu, Padding = new Thickness(24, 18) }; Grid.SetColumn(droite, 1);
        g.Children.Add(droite);
        Content = g;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Afficher(Section.General);
    }
    readonly string _dossier;

    public void Afficher(Section s)
    {
        Active = s;
        _liste.Children.Clear();
        foreach (var (k, nom, ic) in Sections)
        {
            bool on = k == s;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
            row.Children.Add(new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Background = on ? Brushes.White : Ui.AccentBrush, Child = Icones.Make(ic, 14, on ? Ui.AccentBrush : Brushes.White), HorizontalAlignment = HorizontalAlignment.Center });
            row.Children.Add(new TextBlock { Text = nom, FontSize = Ui.Onglet, VerticalAlignment = VerticalAlignment.Center, Foreground = on ? Brushes.White : Ui.Ink });
            var b = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5), CornerRadius = new CornerRadius(6), Background = on ? Ui.AccentBrush : Brushes.Transparent, BorderThickness = new Thickness(0) };
            var kk = k; b.Click += (_, _) => Afficher(kk);
            _liste.Children.Add(b);
        }
        var sp = new StackPanel { Spacing = 8, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(new TextBlock { Text = Sections.First(x => x.S == s).Nom, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        switch (s)
        {
            case Section.General:
                sp.Children.Add(Carte(
                    Ligne("Simulation automatique", "Relance la simulation après chaque modification quand le calcul est rapide.",
                        Interrupteur(_p.SimulationAuto, v => _p.SimulationAuto = v))));
                sp.Children.Add(Ui.Hint("La sensibilité du zoom se règle dans « Zoom et navigation »."));
                break;
            case Section.Navigation:
                sp.Children.Add(Carte(Ligne("Sensibilité du zoom", "Molette de la souris et glissement à deux doigts sur le pavé tactile, sur le schéma et sur les courbes.", CurseurZoom())));
                sp.Children.Add(Ui.Hint("Les loupes de la barre d'outils (+ et −) zooment toujours d'un cran fixe ; « 0 » cadre tout."));
                break;
            case Section.Affichage:
                sp.Children.Add(Ui.Section("Apparence"));
                sp.Children.Add(Carte(Ligne("Thème", "Clair, sombre, ou selon le réglage de macOS.",
                    Choix(["Clair", "Sombre", "Système"], _p.Theme switch { "sombre" => 1, "systeme" => 2, _ => 0 }, i => _p.Theme = i switch { 1 => "sombre", 2 => "systeme", _ => "clair" }))));
                sp.Children.Add(Ui.Section("Tests"));
                sp.Children.Add(Carte(
                    Ligne("Bancs de test", "Calque du banc, pastilles et éléments ajoutés par le banc.", Nuances(() => _p.CouleurBanc, v => _p.CouleurBanc = v)),
                    Ligne("Gabarits", "Limites dessinées et zones interdites hachurées.", Nuances(() => _p.CouleurGabarit, v => _p.CouleurGabarit = v)),
                    Ligne("Conforme", "Verdicts, badges PASS et marges respectées.", Nuances(() => _p.CouleurConforme, v => _p.CouleurConforme = v)),
                    Ligne("Non conforme", "Verdicts, badges FAIL et franchissements.", Nuances(() => _p.CouleurNonConforme, v => _p.CouleurNonConforme = v))));
                sp.Children.Add(Ui.Section("Courbes"));
                sp.Children.Add(Carte(
                    Ligne("Épaisseur des courbes", null,
                        Choix(["Fine", "Moyenne", "Épaisse"], _p.EpaisseurCourbes < 2 ? 0 : _p.EpaisseurCourbes > 3 ? 2 : 1, i => _p.EpaisseurCourbes = i switch { 0 => 1.5, 2 => 3.5, _ => 2.5 }))));
                var raz = new Button { Content = "Rétablir les couleurs par défaut", Margin = new Thickness(0, 6, 0, 0) };
                raz.Click += (_, _) =>
                {
                    var d = new Parametres();
                    _p.CouleurBanc = d.CouleurBanc; _p.CouleurGabarit = d.CouleurGabarit; _p.CouleurConforme = d.CouleurConforme; _p.CouleurNonConforme = d.CouleurNonConforme; _p.EpaisseurCourbes = d.EpaisseurCourbes;
                    Valider(); Afficher(Section.Affichage);
                };
                sp.Children.Add(raz);
                break;
            case Section.Composants:
                var dossier = new SelectableTextBlock { Text = _dossier, FontSize = Ui.Aide, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
                var ouvrir = new Button { Content = "Afficher dans le Finder" };
                ouvrir.Click += (_, _) => { try { Directory.CreateDirectory(_dossier); System.Diagnostics.Process.Start("open", $"\"{_dossier}\""); } catch (Exception) { } };
                var pile = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right }; pile.Children.Add(dossier); pile.Children.Add(ouvrir);
                sp.Children.Add(Carte(Ligne("Bibliothèque de composants", "Modèles SPICE importés et leurs symboles (.silxcomp).", pile)));
                sp.Children.Add(Ui.Hint("Les symboles se dessinent sur une grille quatre fois plus fine que celle du schéma ; les broches restent sur la grille du schéma pour se raccorder aux fils."));
                break;
        }
        _contenu.Content = sp;
    }

    void Valider() { _p.Appliquer(); _p.Enregistrer(); _changement(); }

    static Border Carte(params Control[] lignes)
    {
        var sp = new StackPanel();
        for (int i = 0; i < lignes.Length; i++)
        {
            if (i > 0) sp.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(12, 0) });
            sp.Children.Add(lignes[i]);
        }
        return new Border { Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = sp };
    }

    static Control Ligne(string titre, string? aide, Control champ)
    {
        var g = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(12, 9) };
        var t = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        t.Children.Add(new TextBlock { Text = titre, FontSize = Ui.Texte });
        if (aide != null) t.Children.Add(new TextBlock { Text = aide, FontSize = Ui.Aide, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap });
        g.Children.Add(t);
        champ.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(champ, 1); g.Children.Add(champ);
        return g;
    }

    /// <summary>Curseur à cinq crans, avec le nom du cran choisi.</summary>
    Control CurseurZoom()
    {
        var sp = new StackPanel { Spacing = 2, Width = 220 };
        var nom = new TextBlock { Text = Parametres.LibellesZoom[Math.Clamp(_p.NiveauZoom, 1, 5) - 1], FontSize = Ui.Aide, Foreground = Ui.AccentBrush, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var s = new Slider { Minimum = 1, Maximum = 5, TickFrequency = 1, IsSnapToTickEnabled = true, TickPlacement = TickPlacement.BottomRight, Value = Math.Clamp(_p.NiveauZoom, 1, 5) };
        s.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            var n = (int)Math.Round(s.Value); if (n == _p.NiveauZoom) return;
            _p.NiveauZoom = n; nom.Text = Parametres.LibellesZoom[n - 1]; Valider();
        };
        var bornes = new Grid { ColumnDefinitions = new("*,*") };
        bornes.Children.Add(new TextBlock { Text = "Douce", FontSize = 10.5, Foreground = Ui.Muted });
        var r = new TextBlock { Text = "Rapide", FontSize = 10.5, Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(r, 1); bornes.Children.Add(r);
        sp.Children.Add(nom); sp.Children.Add(s); sp.Children.Add(bornes);
        return sp;
    }

    Control Interrupteur(bool valeur, Action<bool> ecrire)
    {
        var c = new ToggleSwitch { IsChecked = valeur, OnContent = "", OffContent = "" };
        c.IsCheckedChanged += (_, _) => { ecrire(c.IsChecked == true); Valider(); };
        return c;
    }

    Control Choix(string[] options, int index, Action<int> ecrire)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        for (int i = 0; i < options.Length; i++)
        {
            bool on = i == index; int ii = i;
            var b = new Button
            {
                Content = options[i], FontSize = Ui.Aide, Padding = new Thickness(10, 4), BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
                Background = on ? Ui.AccentBrush : Ui.Surface, Foreground = on ? Brushes.White : Ui.Ink,
                CornerRadius = i == 0 ? new CornerRadius(6, 0, 0, 6) : i == options.Length - 1 ? new CornerRadius(0, 6, 6, 0) : new CornerRadius(0)
            };
            b.Click += (_, _) => { ecrire(ii); Valider(); Afficher(Active); };
            sp.Children.Add(b);
        }
        return sp;
    }

    /// <summary>Nuancier de couleurs prédéfinies et champ hexadécimal.</summary>
    Control Nuances(Func<string> lire, Action<string> ecrire)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var hex = new TextBox { Text = lire(), Width = 82, FontSize = Ui.Aide, FontFamily = new FontFamily("Menlo, monospace"), Margin = new Thickness(6, 0, 0, 0) };
        foreach (var n in Nuancier)
        {
            bool on = string.Equals(n, lire(), StringComparison.OrdinalIgnoreCase);
            var b = new Button
            {
                Width = 18, Height = 18, Padding = new Thickness(0), CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.Parse(n)),
                BorderBrush = on ? Ui.Ink : Brushes.Transparent, BorderThickness = new Thickness(on ? 2 : 0)
            };
            ToolTip.SetTip(b, n);
            var nn = n; b.Click += (_, _) => { ecrire(nn); Valider(); Afficher(Active); };
            sp.Children.Add(b);
        }
        void Saisie() { var t = (hex.Text ?? "").Trim(); if (!t.StartsWith('#')) t = "#" + t; if (Color.TryParse(t, out _) && t != lire()) { ecrire(t); Valider(); Afficher(Active); } }
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) Saisie(); };
        hex.LostFocus += (_, _) => Saisie();
        sp.Children.Add(hex);
        return sp;
    }
}
