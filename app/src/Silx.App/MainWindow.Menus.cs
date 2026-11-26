using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Silx.Etude;
using EtudeModel = Silx.Etude.Etude;
namespace Silx.App;

/// <summary>
/// Barre de menus native (en haut de l'écran sur macOS) : Silx, Fichier, Édition, Affichage, Placer, Simulation, Aide.
/// Les raccourcis restent ceux de la fenêtre ; dans un champ texte, Édition agit sur le texte.
/// </summary>
public sealed partial class MainWindow
{
    sealed class CommandeMenu(Action a) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => a();
    }

    static KeyModifiers Cmd => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    static NativeMenuItem Item(string titre, Action a, KeyGesture? g = null) => new(titre) { Command = new CommandeMenu(a), Gesture = g };
    static NativeMenuItem Sous(string titre, params NativeMenuItemBase[] items) { var m = new NativeMenu(); foreach (var i in items) m.Items.Add(i); return new NativeMenuItem(titre) { Menu = m }; }
    static KeyGesture G(Key k, KeyModifiers m = KeyModifiers.None) => new(k, m);

    NativeMenu? _menuRecents;
    public NativeMenu BarreMenus { get; private set; } = null!;

    /// <summary>Champ texte qui a le focus : Édition agit sur lui plutôt que sur le schéma.</summary>
    TextBox? TexteActif => FocusManager?.GetFocusedElement() as TextBox;

    void CreerMenus()
    {
        var sep = () => new NativeMenuItemSeparator();
        _menuRecents = new NativeMenu();
        var fichier = Sous("Fichier",
            Item("Nouveau projet", () => NouveauProjet(), G(Key.N, Cmd)),
            Item("Ouvrir…", () => Lancer(() => OuvrirProjetAsync(), "ouverture d'un projet"), G(Key.O, Cmd)),
            new NativeMenuItem("Ouvrir un projet récent") { Menu = _menuRecents },
            sep(),
            Item("Enregistrer", () => Lancer(() => EnregistrerAsync(_projet!, false), "enregistrement"), G(Key.S, Cmd)),
            Item("Enregistrer sous…", () => Lancer(() => EnregistrerAsync(_projet!, true), "enregistrement"), G(Key.S, Cmd | KeyModifiers.Shift)),
            sep(),
            Sous("Exporter",
                Item("Le schéma (PDF, SVG, PNG)…", () => Lancer(() => ExporterAsync(ContenuExport.Schema), "export")),
                Item("Les courbes du banc (PDF, SVG, PNG)…", () => Lancer(() => ExporterAsync(ContenuExport.Courbes), "export")),
                Item("Le schéma et les courbes (PDF, SVG, PNG)…", () => Lancer(() => ExporterAsync(ContenuExport.Tout), "export")),
                new NativeMenuItemSeparator(),
                Item("Les points des courbes (CSV)…", () => Lancer(() => ExporterCsvAsync(DonneesCsv.Courbes), "export CSV")),
                Item("Les mesures et verdicts (CSV)…", () => Lancer(() => ExporterCsvAsync(DonneesCsv.Mesures), "export CSV")),
                Item("Le rapport de test (Markdown)…", () => Lancer(() => ExporterRapportAsync(), "export du rapport"))),
            sep(),
            Item("Fermer le projet", () => Lancer(() => FermerProjetAsync(_projet!), "fermeture"), G(Key.W, Cmd)));

        var edition = Sous("Édition",
            Item("Annuler", () => { if (TexteActif is { } t) t.Undo(); else _canvas.Undo(); }, G(Key.Z, Cmd)),
            Item("Rétablir", () => { if (TexteActif is { } t) t.Redo(); else _canvas.Redo(); }, G(Key.Z, Cmd | KeyModifiers.Shift)),
            sep(),
            Item("Couper", () => { if (TexteActif is { } t) t.Cut(); else _canvas.CutSelection(); }, G(Key.X, Cmd)),
            Item("Copier", () => { if (TexteActif is { } t) t.Copy(); else _canvas.CopySelection(); }, G(Key.C, Cmd)),
            Item("Coller", () => { if (TexteActif is { } t) t.Paste(); else _canvas.Paste(); }, G(Key.V, Cmd)),
            Item("Dupliquer", () => _canvas.DuplicateSelection(), G(Key.D, Cmd)),
            Item("Supprimer", () => _canvas.DeleteSelected()),
            Item("Tout sélectionner", () => { if (TexteActif is { } t) t.SelectAll(); else _canvas.SelectBox(new Rect(-1e7, -1e7, 2e7, 2e7)); }, G(Key.A, Cmd)),
            sep(),
            Item("Pivoter", () => _canvas.RotateSelected(), G(Key.R, Cmd)),
            Item("Miroir", () => _canvas.MirrorSelected()),
            sep(),
            Item("Rechercher une commande ou un composant…", OuvrirPaletteCommandes, G(Key.K, Cmd)));

        var affichage = Sous("Affichage",
            Item("Schéma seul", () => Disposer(Disposition.Schema), G(Key.F1)),
            Item("Schéma et courbes", () => Disposer(Disposition.Mixte), G(Key.F2)),
            Item("Courbes seules", () => Disposer(Disposition.Courbes), G(Key.F3)),
            sep(),
            Item("Zoom avant", () => _canvas.ZoomBy(1.25), G(Key.OemPlus, Cmd)),
            Item("Zoom arrière", () => _canvas.ZoomBy(0.8), G(Key.OemMinus, Cmd)),
            Item("Tout voir", ToutVoir, G(Key.D0, Cmd)),
            sep(),
            Sous("Tiroir du bas",
                Item("Netlist", () => OuvrirDetail(Detail.Netlist)),
                Item("Vérifications", () => OuvrirDetail(Detail.Verifications)),
                Item("Journal ngspice", () => OuvrirDetail(Detail.Journal)),
                Item("Campagne", () => OuvrirDetail(Detail.Campagne)),
                new NativeMenuItemSeparator(),
                Item("Réduire", () => OuvrirDetail(null))),
            Item("Explorateur de projet", () => ExplorateurVisible = !ExplorateurVisible),
            Item("Palette de composants", () => _left.IsVisible = !_left.IsVisible),
            Item("Panneau de propriétés", () => _right.IsVisible = !_right.IsVisible),
            sep(),
            Sous("Thème",
                Item("Clair", () => ChoisirTheme("clair")),
                Item("Sombre", () => ChoisirTheme("sombre")),
                Item("Comme le système", () => ChoisirTheme("systeme"))));

        var placer = Sous("Placer",
            Item("Résistance (R)", () => _canvas.BeginPlace(ComponentKind.Resistor)),
            Item("Condensateur (C)", () => _canvas.BeginPlace(ComponentKind.Capacitor)),
            Item("Inductance (L)", () => _canvas.BeginPlace(ComponentKind.Inductor)),
            Item("Diode (D)", () => _canvas.BeginPlace(ComponentKind.Diode)),
            Item("Source de tension (V)", () => _canvas.BeginPlace(ComponentKind.VoltageSource)),
            Item("Source de courant (I)", () => _canvas.BeginPlace(ComponentKind.CurrentSource)),
            sep(),
            Item("Masse (G)", () => _canvas.BeginPlace(ComponentKind.Ground)),
            Item("Étiquette de net (N)", () => _canvas.BeginPlace(ComponentKind.Label)),
            Item("Fil (W)", () => _canvas.Tool = Tool.Wire),
            Item("Sonde (P)", () => _canvas.Tool = Tool.Probe),
            sep(),
            Item("Importer un modèle SPICE…", () => Lancer(() => ImporterFichierAsync(), "import SPICE")));

        var simulation = Sous("Simulation",
            Item("Lancer la simulation", () => Lancer(() => SimulateAsync(), "simulation"), G(Key.Enter, Cmd)),
            Item("Arrêter", () => _cts?.Cancel(), G(Key.OemPeriod, Cmd)),
            Item("Simulation automatique (activer / couper)", () => SimulationAutomatique = !SimulationAutomatique),
            sep(),
            Item("Point de fonctionnement DC (afficher / masquer)", () => Lancer(() => AfficherPointFonctionnementAsync(!_opVisible), "point de fonctionnement")),
            sep(),
            Sous("Nouveau banc", Enum.GetValues<ModeleBanc>().Select(m => (NativeMenuItemBase)Item(ModelesBanc.Libelle(m), () => CreerBanc(m, EntreeParDefaut(), SortieParDefaut()))).ToArray()),
            Item("Réglages du banc et campagne…", () => { if (_banc != null) OuvrirReglagesBanc(_benchBar); else _status.Text = "Choisissez d'abord un banc."; }),
            Item("Circuit sous test", () => SelectBanc(null)));

        var aide = Sous("Aide",
            Item("Manuel de Silx (en ligne)", () => Lancer(() => Launcher.LaunchUriAsync(new Uri("https://github.com/arkoan/silx#readme")), "aide")),
            Item("Gestes et raccourcis", AfficherAide),
            Item("Palette de commandes", OuvrirPaletteCommandes),
            sep(),
            Sous("Démos", new[] { "Exemple RC" }.Concat(Demos.Toutes.Select(d => d.Nom)).Select(n => (NativeMenuItemBase)Item(n, () => OuvrirDemo(n))).ToArray()));

        BarreMenus = new NativeMenu();
        foreach (var m in new[] { fichier, edition, affichage, placer, simulation, aide }) BarreMenus.Items.Add(m);
        NativeMenu.SetMenu(this, BarreMenus);

        // Menu de l'application (« Silx ») : À propos et Paramètres, avant Masquer / Quitter ajoutés par macOS.
        if (Application.Current is { } app)
        {
            var mApp = new NativeMenu();
            mApp.Items.Add(Item("À propos de Silx", AfficherAPropos));
            mApp.Items.Add(new NativeMenuItemSeparator());
            mApp.Items.Add(Item("Paramètres…", () => OuvrirParametres(), G(Key.OemComma, Cmd)));
            NativeMenu.SetMenu(app, mApp);
        }
        RafraichirRecents();
    }

    void ChoisirTheme(string t) { Parametres.Courant.Theme = t; Parametres.Courant.Appliquer(); Parametres.Courant.Enregistrer(); AppliquerParametres(); }

    /// <summary>Retient un projet ouvert ou enregistré dans les récents (10 au plus, le plus récent d'abord).</summary>
    void RetenirRecent(string chemin)
    {
        var l = Parametres.Courant.Recents;
        l.RemoveAll(x => string.Equals(x, chemin, StringComparison.Ordinal));
        l.Insert(0, chemin);
        if (l.Count > 10) l.RemoveRange(10, l.Count - 10);
        Parametres.Courant.Enregistrer();
        RafraichirRecents();
    }

    void RafraichirRecents()
    {
        if (_menuRecents == null) return;
        _menuRecents.Items.Clear();
        var l = Parametres.Courant.Recents.Where(File.Exists).ToList();
        foreach (var c in l) { var cc = c; _menuRecents.Items.Add(Item($"{Path.GetFileNameWithoutExtension(c)} — {Path.GetDirectoryName(c)}", () => OuvrirFichier(cc))); }
        if (l.Count == 0) _menuRecents.Items.Add(new NativeMenuItem("Aucun projet récent") { IsEnabled = false });
        else
        {
            _menuRecents.Items.Add(new NativeMenuItemSeparator());
            _menuRecents.Items.Add(Item("Effacer la liste", () => { Parametres.Courant.Recents.Clear(); Parametres.Courant.Enregistrer(); RafraichirRecents(); }));
        }
    }

    void AfficherAPropos()
    {
        var version = typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "dev";
        var sp = new StackPanel { Spacing = 8, Margin = new Thickness(28), HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = "Silx", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = $"Version {version}", Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = "Simulation électronique et bancs de test, avec ngspice.", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        var w = new Window { Title = "À propos de Silx", Width = 380, Height = 190, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = sp, Background = Ui.Window };
        w.Show(this);
    }
}
