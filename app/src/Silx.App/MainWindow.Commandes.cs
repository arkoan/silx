using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Silx.Etude;
using EtudeModel = Silx.Etude.Etude;
namespace Silx.App;

/// <summary>Palette de commandes (⌘K) et aide-mémoire des gestes et raccourcis (« ? »).</summary>
public sealed partial class MainWindow
{
    public sealed record Commande(string Nom, string Raccourci, string Groupe, Action Executer);

    /// <summary>Toutes les actions accessibles par leur nom.</summary>
    public IReadOnlyList<Commande> Commandes()
    {
        var l = new List<Commande>
        {
            new("Simuler", "F5", "Simulation", () => Lancer(() => SimulateAsync(), "simulation")),
            new("Simulation automatique (activer / couper)", "", "Simulation", () => SimulationAutomatique = !SimulationAutomatique),
            new("Nouveau projet", "⌘N", "Fichier", () => NouveauProjet()),
            new("Ouvrir un projet", "⌘O", "Fichier", () => Lancer(() => OuvrirProjetAsync(), "ouverture d'un projet")),
            new("Enregistrer", "⌘S", "Fichier", () => Lancer(() => EnregistrerAsync(_projet!, false), "enregistrement")),
            new("Enregistrer sous", "⇧⌘S", "Fichier", () => Lancer(() => EnregistrerAsync(_projet!, true), "enregistrement")),
            new("Exporter le rapport de test", "", "Fichier", () => Lancer(() => ExporterRapportAsync(), "export du rapport")),
            new("Ouvrir l'exemple RC", "", "Fichier", () => OuvrirDansOnglet(EtudeModel.Demo(), "Exemple RC", null)),
            new("Tracer un fil", "W", "Dessin", () => _canvas.Tool = Tool.Wire),
            new("Sonde : afficher une tension", "P", "Dessin", () => _canvas.Tool = Tool.Probe),
            new("Pivoter la sélection", "⌘R", "Dessin", () => _canvas.RotateSelected()),
            new("Miroir de la sélection", "X", "Dessin", () => _canvas.MirrorSelected()),
            new("Dupliquer la sélection", "⌘D", "Dessin", () => _canvas.DuplicateSelection()),
            new("Supprimer la sélection", "Suppr", "Dessin", () => _canvas.DeleteSelected()),
            new("Annuler", "⌘Z", "Dessin", () => _canvas.Undo()),
            new("Rétablir", "⇧⌘Z", "Dessin", () => _canvas.Redo()),
            new("Schéma seul", "F1", "Affichage", () => Disposer(Disposition.Schema)),
            new("Schéma et courbes", "F2", "Affichage", () => Disposer(Disposition.Mixte)),
            new("Courbes seules", "F3", "Affichage", () => Disposer(Disposition.Courbes)),
            new("Tout voir", "0", "Affichage", ToutVoir),
            new("Afficher la netlist", "", "Affichage", () => OuvrirDetail(Detail.Netlist)),
            new("Afficher les vérifications", "", "Affichage", () => OuvrirDetail(Detail.Verifications)),
            new("Afficher le journal ngspice", "", "Affichage", () => OuvrirDetail(Detail.Journal)),
            new("Ajouter un graphique", "", "Courbes", () => AddPane()),
            new("Explorateur de projet (afficher / masquer)", "", "Affichage", () => ExplorateurVisible = !ExplorateurVisible),
            new("Retirer tous les curseurs", "", "Courbes", () => { _cursors.Clear(); RefreshPlot(keepLimits: true); }),
            new("Aide-mémoire des gestes et raccourcis", "?", "Aide", () => AfficherAide()),
            new("Circuit sous test", "", "Bancs", () => SelectBanc(null)),
        };
        foreach (var d in Demos.Toutes) { var dd = d; l.Add(new($"Ouvrir la démo « {d.Nom} »", "", "Fichier", () => OuvrirDansOnglet(dd.Creer(), dd.Nom, null))); }
        foreach (var m in Enum.GetValues<ModeleBanc>())
        {
            var mm = m;
            l.Add(new($"Nouveau banc : {ModelesBanc.Libelle(m)}", "", "Bancs", () => CreerBanc(mm, EntreeParDefaut(), SortieParDefaut())));
        }
        foreach (var b in _etude.Bancs) { var bb = b; l.Add(new($"Aller au banc « {b.Nom} »", "", "Bancs", () => SelectBanc(bb))); }
        foreach (var (k, nom, touche) in new[] { (ComponentKind.Resistor, "résistance", "R"), (ComponentKind.Capacitor, "condensateur", "C"), (ComponentKind.Inductor, "inductance", "L"),
                     (ComponentKind.Diode, "diode", "D"), (ComponentKind.VoltageSource, "source de tension", "V"), (ComponentKind.CurrentSource, "source de courant", "I"),
                     (ComponentKind.Ground, "masse", "G"), (ComponentKind.Label, "étiquette de nœud", "N") })
        { var kk = k; l.Add(new($"Placer : {nom}", touche, "Composants", () => _canvas.BeginPlace(kk))); }
        foreach (var d in _bib.Definitions) { var dd = d; l.Add(new($"Placer : {d.Nom} ({d.Description})", "", "Composants", () => _canvas.BeginPlace(dd))); }
        return l;
    }

    string? EntreeParDefaut() { var c = Connectivity.Build(_etude.Schema); return c.AllNodes.FirstOrDefault(n => n.Equals("IN", StringComparison.OrdinalIgnoreCase)) ?? c.NamedNodes.FirstOrDefault() ?? c.AllNodes.FirstOrDefault(); }
    string? SortieParDefaut() { var c = Connectivity.Build(_etude.Schema); return c.AllNodes.FirstOrDefault(n => n.Equals("OUT", StringComparison.OrdinalIgnoreCase)) ?? c.NamedNodes.LastOrDefault() ?? c.AllNodes.LastOrDefault(); }

    /// <summary>Commandes dont le nom contient tous les mots tapés (casse et accents ignorés), les plus courtes d'abord.</summary>
    public IReadOnlyList<Commande> ChercherCommandes(string texte)
    {
        static string N(string x) => new string(x.Normalize(System.Text.NormalizationForm.FormD).Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        var mots = N(texte).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Commandes().Where(c => mots.All(m => N(c.Nom + " " + c.Groupe).Contains(m))).OrderBy(c => c.Nom.Length).Take(12).ToList();
    }

    Window? _palette2;
    public bool PaletteOuverte => _palette2?.IsVisible == true;

    /// <summary>⌘K : chercher une action ou un composant par son nom, Entrée pour l'exécuter.</summary>
    public void OuvrirPaletteCommandes()
    {
        var champ = new TextBox { PlaceholderText = "Chercher une action ou un composant…", FontSize = 14, Padding = new Thickness(10, 6) };
        var liste = new ListBox { MaxHeight = 360, FontSize = Ui.Texte };
        void Filtrer() { liste.ItemsSource = ChercherCommandes(champ.Text ?? "").Select(c => new ListBoxItem { Content = Ligne(c), Tag = c }).ToList(); liste.SelectedIndex = 0; }
        Control Ligne(Commande c)
        {
            var g = new Grid { ColumnDefinitions = new("*,Auto") };
            g.Children.Add(new TextBlock { Text = c.Nom, VerticalAlignment = VerticalAlignment.Center });
            var r = new TextBlock { Text = c.Raccourci.Length > 0 ? c.Raccourci : c.Groupe, Foreground = Ui.Muted, FontSize = Ui.Aide, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(r, 1); g.Children.Add(r);
            return g;
        }
        var w = new Window
        {
            Width = 520, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Panel, Title = "Commandes"
        };
        void Executer() { if (liste.SelectedItem is ListBoxItem { Tag: Commande c }) { w.Close(); c.Executer(); } }
        champ.TextChanged += (_, _) => Filtrer();
        champ.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { liste.SelectedIndex = Math.Min(liste.SelectedIndex + 1, liste.ItemCount - 1); e.Handled = true; }
            else if (e.Key == Key.Up) { liste.SelectedIndex = Math.Max(liste.SelectedIndex - 1, 0); e.Handled = true; }
            else if (e.Key == Key.Enter) { Executer(); e.Handled = true; }
            else if (e.Key == Key.Escape) { w.Close(); e.Handled = true; }
        };
        liste.DoubleTapped += (_, _) => Executer();
        var sp = new StackPanel { Spacing = 6, Margin = new Thickness(10) };
        sp.Children.Add(champ); sp.Children.Add(liste);
        w.Content = sp;
        Filtrer();
        _palette2 = w;
        w.Opened += (_, _) => champ.Focus();
        w.Deactivated += (_, _) => w.Close();
        w.Show(this);
    }

    /// <summary>« ? » : aide-mémoire des gestes et des raccourcis, par thème.</summary>
    public void AfficherAide()
    {
        var groupes = new (string Titre, (string Geste, string Effet)[] Lignes)[]
        {
            ("Schéma", [("R C L D V I G N", "Placer une résistance, un condensateur… (Maj+clic : plusieurs)"), ("Glisser depuis une broche", "Tracer un fil en L"), ("W", "Outil fil, coin par coin"),
                ("Double-clic sur un composant", "Modifier sa valeur sur place"), ("Glisser depuis le fond", "Sélection par rectangle (Maj : ajouter)"),
                ("⌘C ⌘X ⌘V ⌘D", "Copier, couper, coller sous la souris, dupliquer"), ("⌘R · X", "Pivoter · miroir"), ("Suppr", "Effacer la sélection"), ("⌘Z ⇧⌘Z", "Annuler, rétablir")]),
            ("Bancs", [("+ à droite des bancs", "Nouveau banc depuis un modèle"), ("Roue dentée d'un banc", "Nom et analyse"), ("Double-clic sur le circuit atténué", "Modifier le circuit sans quitter le banc")]),
            ("Courbes", [("P (sonde)", "Ajouter la tension d'un fil au graphique actif"), ("Double-clic sur une courbe · A · B", "Poser un curseur"),
                ("Limite haute / basse", "Dessiner un gabarit ; glisser un point pour le modifier"), ("Clic sur une ligne du plan de test", "Voir la contrainte sur la courbe")]),
            ("Affichage", [("F1 F2 F3", "Schéma seul, mixte, courbes seules"), ("+ − 0", "Zoom de la zone active, tout voir"), ("⌘K", "Chercher une commande"), ("F5", "Simuler")]),
        };
        var sp = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        sp.Children.Add(Ui.Title("Gestes et raccourcis"));
        var cols = new Grid { ColumnDefinitions = new("*,24,*") };
        var gauche = new StackPanel { Spacing = 12 }; var droite = new StackPanel { Spacing = 12 };
        for (int i = 0; i < groupes.Length; i++)
        {
            var bloc = new StackPanel { Spacing = 4 };
            bloc.Children.Add(Ui.Section(groupes[i].Titre));
            foreach (var (geste, effet) in groupes[i].Lignes)
            {
                var g = new Grid { ColumnDefinitions = new("170,*") };
                g.Children.Add(new TextBlock { Text = geste, FontWeight = FontWeight.SemiBold, FontSize = Ui.Aide, TextWrapping = TextWrapping.Wrap });
                var t = new TextBlock { Text = effet, FontSize = Ui.Aide, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(t, 1); g.Children.Add(t);
                bloc.Children.Add(g);
            }
            (i % 2 == 0 ? gauche : droite).Children.Add(bloc);
        }
        cols.Children.Add(gauche); Grid.SetColumn(droite, 2); cols.Children.Add(droite);
        sp.Children.Add(cols);
        var w = new Window { Title = "Aide-mémoire", Width = 760, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Panel, Content = sp };
        w.KeyDown += (_, e) => { if (e.Key is Key.Escape) w.Close(); };
        _aide = w;
        w.Show(this);
    }
    Window? _aide;
    public bool AideOuverte => _aide?.IsVisible == true;

    /// <summary>Exporte le rapport de test (implémenté plus bas : E2).</summary>
    public partial Task ExporterRapportAsync();
}
