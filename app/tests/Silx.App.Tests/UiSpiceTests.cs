using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Silx.Etude;
using Xunit;
namespace Silx.App.Tests;

static class BibliothequeDeTest
{
    /// <summary>Les tests n'écrivent pas dans ~/Documents : bibliothèque de composants dans un dossier temporaire.</summary>
    [ModuleInitializer]
    internal static void Init()
    {
        var id = Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable("SILX_COMPOSANTS", Path.Combine(Path.GetTempPath(), "silx-tests-" + id));
        Environment.SetEnvironmentVariable("SILX_JOURNAL", Path.Combine(Path.GetTempPath(), "silx-tests-" + id));
        Environment.SetEnvironmentVariable("SILX_PARAMETRES", Path.Combine(Path.GetTempPath(), "silx-tests-" + id, "parametres.json"));   // pas les préférences de l'utilisateur
    }
}

/// <summary>Interface : fils élastiques à la souris, import de modèles SPICE, éditeur de symbole (captures avec SILX_SHOT_DIR).</summary>
public class UiSpiceTests
{
    static void Shot(Window w, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SILX_SHOT_DIR") is { } dir) frame!.Save(Path.Combine(dir, name));
    }
    static Point Ecran(MainWindow w, double gx, double gy)
    {
        var c = w.Schema; var a = c.ScreenOf(new GridPoint(0, 0)); var b = c.ScreenOf(new GridPoint(1, 1));
        return c.TranslatePoint(new Point(a.X + gx * (b.X - a.X), a.Y + gy * (b.Y - a.Y)), w)!.Value;
    }

    [AvaloniaFact]
    public void Glisser_un_composant_garde_ses_fils_et_ceux_des_bancs()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.SelectBanc(null);
        var e = w.EtudeCourante; var avant = e.Bancs[1].Fusion(e.Schema);
        Shot(w, "15-avant-glisser.png");
        var c1 = e.Schema.Components.Single(c => c.Ref == "C1");   // (11,8)-(11,11), vertical
        var p0 = Ecran(w, 11, 10);
        w.MouseMove(p0); w.MouseDown(p0, MouseButton.Left);
        foreach (var k in new[] { 1.0, 2.0, 3.0 }) { w.MouseMove(Ecran(w, 11 + k, 10 - k)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Shot(w, "16-pendant-glisser.png");
        w.MouseMove(Ecran(w, 14, 6)); w.MouseUp(Ecran(w, 14, 6), MouseButton.Left);   // (+3, −4) : la broche basse ne retombe pas sur le fil du banc
        Assert.Equal(new GridPoint(14, 4), c1.Position);
        Assert.All(e.Schema.Wires, x => Assert.True(x.A.X == x.B.X || x.A.Y == x.B.Y));
        Assert.True(Elastique.MemesConnexions(avant, e.Bancs[1].Fusion(e.Schema)));  // le banc Bode suit aussi
        Shot(w, "17-apres-glisser.png");
        w.Schema.Undo();
        Assert.Equal(new GridPoint(11, 8), e.Schema.Components.Single(c => c.Ref == "C1").Position);
        Assert.Equal(avant.Wires.ToHashSet(), e.Bancs[1].Fusion(e.Schema).Wires.ToHashSet());
    }

    [AvaloniaFact]
    public void Importer_placer_et_relier_un_transistor()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var defs = w.ImporterModeles(SpiceTests.Bibliotheque, "essai.lib");
        Assert.Equal(["Q2N2222", "BS170", "RCF"], defs.Select(d => d.Nom));
        w.SelectBanc(null);
        var q = defs[0];
        w.Schema.BeginPlace(q);
        var p = Ecran(w, 20, 8);
        w.MouseMove(p); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left);
        var inst = w.EtudeCourante.Schema.Components.Single(c => c.Kind == ComponentKind.Spice);
        Assert.Equal("Q1", inst.Ref);
        Assert.Equal(new GridPoint(20, 8), inst.Position);
        w.Schema.BeginPlace(defs[2]);
        var p2 = Ecran(w, 20, 14);
        w.MouseMove(p2); w.MouseDown(p2, MouseButton.Left); w.MouseUp(p2, MouseButton.Left);
        Assert.Contains(w.EtudeCourante.Schema.Components, c => c.Ref == "X1");
        w.Schema.FitAll();
        Shot(w, "18-composants-importes.png");

        // On change le symbole : le collecteur passe à droite ; un fil attaché à C suit.
        w.EtudeCourante.Schema.Wires.Add(new(new(24, 6), new(24, 1)));
        w.EtudeCourante.Schema.Components.Add(new() { Ref = "NET9", Kind = ComponentKind.Label, Position = new(24, 1), Value = "VC" });
        var ed = new EditeurSymbole(q);
        ed.DeplacerBroche(1, 4, -2);
        ed.Enregistrer();
        w.AppliquerSymbole(q, ed.Resultat!);
        Assert.Equal(new GridPoint(24, 6), inst.Pins[1]);
        var conn = Connectivity.Build(w.EtudeCourante.Schema);
        Assert.Equal("VC", conn.NodeOf[inst.Pins[1]]);
        Assert.True(File.Exists(Path.Combine(Environment.GetEnvironmentVariable("SILX_COMPOSANTS")!, "Q2N2222.silxcomp")));
    }

    [AvaloniaFact]
    public void Editeur_de_symbole_relier_les_broches()
    {
        var d = DefinitionComposant.Depuis(ModelesSpice.Analyser(SpiceTests.Bibliotheque).Modeles.Single(m => m.Nom == "RCF"));
        var ed = new EditeurSymbole(d); ed.Show();
        Shot(ed, "19-editeur-boite.png");
        // Croiser IN et GND : cliquer la broche du modèle, puis la broche du symbole.
        ed.ChoisirBrocheModele("GND"); ed.ChoisirBrocheSymbole(0);
        Assert.Equal("GND", ed.Definition.Symbole.Broches[0].BrocheModele);
        Assert.Null(ed.Definition.Symbole.Broches[2].BrocheModele);           // GND n'a qu'une broche de symbole
        Assert.Contains(ed.Definition.Verifier(), i => i.Message.Contains("« IN »"));
        Shot(ed, "20-editeur-a-relier.png");
        ed.Enregistrer();
        Assert.Null(ed.Resultat);                                             // erreur : IN non reliée
        ed.ChoisirBrocheModele("IN"); ed.ChoisirBrocheSymbole(2);
        ed.Enregistrer();
        Assert.NotNull(ed.Resultat);
        Assert.Equal([2, 1, 0], ed.Resultat!.OrdreSpice());
        Assert.Equal("IN", d.Symbole.Broches[0].BrocheModele);                // l'original n'est pas touché avant enregistrement

        var m = DefinitionComposant.Depuis(ModelesSpice.Analyser(SpiceTests.Bibliotheque).Modeles.Single(x => x.Nom == "BS170"));
        var em = new EditeurSymbole(m); em.Show();
        em.ChoisirBrocheSymbole(3);
        Shot(em, "21-editeur-mos.png");
        em.ChoisirGabarit(GabaritSymbole.Boite);
        Assert.Equal(4, em.Definition.Symbole.Broches.Count);
        Assert.Empty(em.Definition.Verifier());
    }

    /// <summary>Éditeur sur un MOSFET « fabricant » : infos lues dans le fichier, libellés des broches, broche thermique raccordée.</summary>
    [AvaloniaFact]
    public void Editeur_affiche_les_infos_du_modele_fabricant()
    {
        var d = DefinitionComposant.Depuis(ModelesSpice.Analyser(ModelesFabricantTests.Fichier).Modeles.First());
        var ed = new EditeurSymbole(d); ed.Show();
        Assert.True(ed.Definition.Verifier().All(p => p.Severity != Severity.Error));
        Shot(ed, "21-editeur-mosfet-fabricant.png");
        ed.RaccorderCachee("50", 0, "");          // remettre la broche thermique « à relier »
        Assert.Contains(ed.Definition.Verifier(), p => p.Severity == Severity.Error && p.Message.Contains("50 (VTEMP)"));
        ed.RaccorderCachee("50", 2, "25");
        Assert.Equal("DC 25", ed.Definition.BrochesCachees["50"]);
    }

    /// <summary>Symbole personnalisé : palette de dessin (rectangle, triangle, arc, cercle, broche), sélection, annulation.</summary>
    [AvaloniaFact]
    public void Dessiner_un_symbole_personnalise()
    {
        var d = DefinitionComposant.Depuis(ModelesSpice.Analyser(ModelesFabricantTests.Fichier).Modeles.First());
        var ed = new EditeurSymbole(d) { Width = 1200, Height = 620 }; ed.Show();
        int avant = ed.Definition.Symbole.Traits.Count;
        ed.ChoisirOutil(OutilDessin.Rectangle);
        Assert.Equal(GabaritSymbole.Personnalise, ed.Definition.Symbole.Gabarit);
        Assert.Equal(avant, ed.Definition.Symbole.Traits.Count);            // le dessin du MOS est conservé, modifiable
        ed.Tracer(OutilDessin.Rectangle, -1, -4, 6, 4);
        ed.Tracer(OutilDessin.Triangle, 6.5, -1, 8.5, -1);
        ed.Tracer(OutilDessin.Arc, -1, -2, -1, 2);
        ed.Tracer(OutilDessin.Cercle, 7.5, 2, 8, 2);
        Assert.Equal(avant + 4, ed.Definition.Symbole.Traits.Count);
        Assert.Equal(FormePrimitive.Arc, ed.Definition.Symbole.Traits[avant + 2].Forme);
        // Sélection au clic : le bord du rectangle
        Assert.Equal(avant, ed.PrimitiveEn(2.5, -4.1));
        ed.PrimitiveChoisie = avant; ed.SupprimerSelection();
        Assert.Equal(avant + 3, ed.Definition.Symbole.Traits.Count);
        ed.Annuler();
        Assert.Equal(avant + 4, ed.Definition.Symbole.Traits.Count);
        // Nouvelle broche, reliée à la broche thermique du modèle
        ed.ChoisirOutil(OutilDessin.Broche);
        ed.AjouterBrocheEn(-3, 4);
        ed.ChoisirBrocheModele("50");
        ed.ChoisirBrocheSymbole(ed.Definition.Symbole.Broches.Count - 1);
        Assert.Equal("50", ed.Definition.Symbole.Broches[^1].BrocheModele);
        Assert.False(ed.Definition.BrochesCachees.ContainsKey("50"));
        Assert.DoesNotContain(ed.Definition.Verifier(), p => p.Severity == Severity.Error);
        // Aller-retour JSON avec un arc
        Assert.Equal(FormePrimitive.Arc, DefinitionComposant.DepuisJson(ed.Definition.VersJson()).Symbole.Traits[avant + 2].Forme);
        ed.ChoisirOutil(OutilDessin.Selection);
        Shot(ed, "22-symbole-personnalise.png");
    }

    [AvaloniaFact]
    public void Renommer_une_broche_du_symbole()
    {
        var d = DefinitionComposant.Depuis(ModelesSpice.Analyser(ModelesFabricantTests.Fichier).Modeles.First());
        var ed = new EditeurSymbole(d); ed.Show();
        int g = ed.Definition.Symbole.Broches.FindIndex(b => b.BrocheModele == "10");
        ed.ChoisirBrocheSymbole(g);
        ed.RenommerBroche("Grille");
        Assert.Equal("Grille", ed.Definition.Symbole.Broches[g].Nom);
        Assert.Equal("10", ed.Definition.Symbole.Broches[g].BrocheModele);          // la liaison au modèle ne change pas
        Assert.Equal("Grille", ed.Definition.NomBroche(ed.Definition.Symbole.Broches[g]));
        Assert.Equal("Grille", DefinitionComposant.DepuisJson(ed.Definition.VersJson()).Symbole.Broches[g].Nom);
        Shot(ed, "25-broche-renommee.png");
        ed.RenommerBroche("");
        Assert.Null(ed.Definition.Symbole.Broches[g].Nom);
        Assert.Equal("GATE", ed.Definition.NomBroche(ed.Definition.Symbole.Broches[g]));
    }

    /// <summary>F1 : déposer une broche du modèle sur une broche du symbole (liaison) ou sur la grille d'une boîte (nouvelle broche).</summary>
    [AvaloniaFact]
    public void Glisser_deposer_des_broches()
    {
        var d = DefinitionComposant.Depuis(ModelesSpice.Analyser(ModelesFabricantTests.Fichier).Modeles.First());
        var ed = new EditeurSymbole(d); ed.Show();
        int g = ed.Definition.Symbole.Broches.FindIndex(b => b.BrocheModele == "10");
        int dr = ed.Definition.Symbole.Broches.FindIndex(b => b.BrocheModele == "20");
        ed.DeposerBrocheModele("20", g, 0, 0);                       // 20 (DRAIN) déposée sur la broche de grille
        Assert.Equal("20", ed.Definition.Symbole.Broches[g].BrocheModele);
        Assert.Null(ed.Definition.Symbole.Broches[dr].BrocheModele);  // une broche du modèle n'a qu'une broche de symbole
        ed.ChoisirGabarit(GabaritSymbole.Boite);
        int n = ed.Definition.Symbole.Broches.Count;
        ed.DeposerBrocheModele("50", null, 4, 6);                     // VTEMP déposée sur la grille : nouvelle broche
        Assert.Equal(n + 1, ed.Definition.Symbole.Broches.Count);
        Assert.Equal("50", ed.Definition.Symbole.Broches[^1].BrocheModele);
        Assert.False(ed.Definition.BrochesCachees.ContainsKey("50"));
    }

    /// <summary>Composants importés rangés par catégorie (diode, NPN, MOSFET N, sous-circuit…) puis par référence.</summary>
    [AvaloniaFact]
    public void Composants_importes_par_categorie()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.ImporterModeles(SpiceTests.Bibliotheque + "\n" + ModelesFabricantTests.Fichier, "essai.lib");
        var cats = w.Bibliotheque.Definitions.GroupBy(MainWindow.Categorie).ToDictionary(g => g.Key, g => g.Select(d => d.Nom).ToList());
        Assert.Contains("Q2N2222", cats["Bipolaire NPN"]);
        Assert.Contains("BS170", cats["MOSFET canal N"]);
        Assert.Contains("DEMO138", cats["MOSFET canal N"]);
        Assert.Contains("RCF", cats["Sous-circuit"]);
        w.ProposerReferences("MOSFET canal N", w.Canevas);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Shot(w, "38-references-mosfet.png");
    }

    /// <summary>Paramètres : fenêtre à sections, couleur des bancs de test appliquée tout de suite et enregistrée.</summary>
    [AvaloniaFact]
    public void Parametres_couleur_des_tests()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var f = w.OuvrirParametres();
        Assert.Same(f, w.OuvrirParametres());   // une seule fenêtre
        f.Afficher(FenetreParametres.Section.Affichage);
        Parametres.Courant.CouleurBanc = "#0891b2"; Parametres.Courant.Appliquer(); Parametres.Courant.Enregistrer();
        Assert.Equal(Avalonia.Media.Color.Parse("#0891b2"), ((Avalonia.Media.SolidColorBrush)SchematicCanvas.BenchBrush).Color);
        Assert.Contains("#0891b2", File.ReadAllText(Parametres.Fichier()));
        f.Afficher(FenetreParametres.Section.Affichage);
        Shot(f, "40-parametres-affichage.png");
        Shot(w, "41-banc-couleur-reglee.png");
        f.Afficher(FenetreParametres.Section.General);
        Shot(f, "42-parametres-general.png");
        f.Close();
        Parametres.Courant.Reinitialiser(); Parametres.Courant.Appliquer();
    }

    /// <summary>Grille fine : transistors à 2 pas ; une ancienne bibliothèque est redessinée en gardant les liaisons des broches.</summary>
    [AvaloniaFact]
    public void Symboles_sur_grille_fine()
    {
        var neuf = GabaritsSymboles.Dessin(GabaritSymbole.Nmos3, true);
        var (x0, y0, x1, y1) = neuf.Encombrement();
        Assert.True(y1 - y0 <= 4.01 && x1 - x0 <= 3.2);   // 4 pas de haut au lieu de 6
        var ancien = GabaritsSymboles.Dessin(GabaritSymbole.Npn, false);
        GabaritsSymboles.Reduire(ancien, 1.5); ancien.Version = 0;   // tel qu'enregistré avant la grille fine : broches à 3 pas
        ancien.Broches.Single(b => b.Role == "C").BrocheModele = "COL"; ancien.Broches.Single(b => b.Role == "C").Nom = "Vc";
        Assert.True(GabaritsSymboles.MettreAJour(ancien));
        var c = ancien.Broches.Single(b => b.Role == "C");
        Assert.Equal((2, -2, "COL", "Vc"), (c.X, c.Y, c.BrocheModele, c.Nom));
        Assert.False(GabaritsSymboles.MettreAJour(ancien));
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var defs = w.ImporterModeles(ModelesFabricantTests.Fichier, "demo.lib");
        var ed = new EditeurSymbole(defs[0]); ed.Show();
        Shot(ed, "43-editeur-grille-fine.png");
        w.SelectBanc(null);
        w.EtudeCourante.Schema.Components.Add(new() { Ref = "X1", Kind = ComponentKind.Spice, Definition = defs[0], Position = new(30, 4) });
        w.EtudeCourante.Schema.Components.Add(new() { Ref = "R9", Kind = ComponentKind.Resistor, Position = new(26, 10), Value = "10k" });
        w.Schema.Load(w.EtudeCourante.Schema); w.Schema.FitAll();
        Shot(w, "44-mosfet-et-resistance.png");
    }

    /// <summary>Chaque banc peut avoir sa couleur (réglages de sa pastille), enregistrée dans le projet.</summary>
    [AvaloniaFact]
    public void Couleur_propre_a_chaque_banc()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var bancs = w.EtudeCourante.Bancs;
        w.SelectBanc(bancs[0]);
        w.ColorerBanc("#0891b2");
        Assert.Equal("#0891b2", bancs[0].Couleur);
        Assert.Equal(Avalonia.Media.Color.Parse("#0891b2"), ((Avalonia.Media.ISolidColorBrush)w.Schema.CouleurCalque).Color);
        Assert.Same(Ui_BenchBrush(), w.CouleurDe(bancs[1]));   // l'autre banc garde la couleur des paramètres
        var relu = Projet.DepuisJson(Projet.VersJson(w.EtudeCourante));
        Assert.Equal("#0891b2", relu.Bancs[0].Couleur);
        w.OuvrirReglagesBanc(w.Schema);
        Shot(w, "45-couleur-du-banc.png");
    }
    static Avalonia.Media.IBrush Ui_BenchBrush() => SchematicCanvas.BenchBrush;

    /// <summary>Mode sombre et sensibilité du zoom (5 crans) depuis la fenêtre Paramètres.</summary>
    [AvaloniaFact]
    public async Task Mode_sombre_et_zoom()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        var p = Parametres.Courant;
        p.NiveauZoom = 1; Assert.InRange(SchematicCanvas.FacteurMolette(1), 1.0, 1.02);
        p.NiveauZoom = 5; Assert.InRange(SchematicCanvas.FacteurMolette(1), 1.14, 1.16);
        p.NiveauZoom = 3;
        p.Theme = "sombre"; p.Appliquer();
        var f = w.OuvrirParametres(); f.Afficher(FenetreParametres.Section.Navigation);
        Shot(f, "46-parametres-zoom-sombre.png");
        w.SelectBanc(w.EtudeCourante.Bancs[0]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Shot(w, "47-mode-sombre.png");
        f.Close();
        p.Theme = "clair"; p.Appliquer();
        Assert.Equal(Avalonia.Media.Colors.White, ((Avalonia.Media.ISolidColorBrush)Silx_Surface()).Color);
    }
    static Avalonia.Media.IBrush Silx_Surface() => (Avalonia.Media.IBrush)typeof(MainWindow).Assembly.GetType("Silx.App.Ui")!.GetField("Surface")!.GetValue(null)!;

    /// <summary>Composants logiques dans l'éditeur : catégorie « Logique » de la palette, bascule D et porte ET posées, courbes.</summary>
    [AvaloniaFact]
    public async Task Logique_xspice_dans_l_editeur()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var bib = w.Bibliotheque.Definitions.Where(d => d.CodeXspice != null).ToDictionary(d => d.Nom);
        Assert.Contains("DFF", bib.Keys); Assert.Contains("NAND2", bib.Keys);
        var e = new Silx.Etude.Etude(); var s = e.Schema;
        s.Components.Add(new() { Ref = "A1", Kind = ComponentKind.Spice, Definition = bib["DFF"], Position = new(10, 10) });
        s.Components.Add(new() { Ref = "A2", Kind = ComponentKind.Spice, Definition = bib["AND2"], Position = new(22, 10) });
        s.Components.Add(new() { Ref = "A3", Kind = ComponentKind.Spice, Definition = bib["INV"], Position = new(4, 11) });
        void Net(string r, int x, int y, string n) => s.Components.Add(new() { Ref = r, Kind = ComponentKind.Label, Position = new(x, y), Value = n });
        e.Schema.Wires.Add(new(new(15, 9), new(22, 9)));       // Q → A
        e.Schema.Wires.Add(new(new(8, 11), new(10, 11)));      // INV → CLK
        e.Schema.Wires.Add(new(new(8, 11), new(8, 14))); e.Schema.Wires.Add(new(new(8, 14), new(20, 14))); e.Schema.Wires.Add(new(new(20, 14), new(20, 11))); e.Schema.Wires.Add(new(new(20, 11), new(22, 11)));
        Net("NET1", 10, 9, "D"); Net("NET2", 4, 11, "H"); Net("NET3", 15, 9, "Q"); Net("NET4", 26, 10, "Y");
        var banc = new Banc { Nom = "Horloge 100 kHz" }; e.Bancs.Add(banc);
        void Source(string r, int x, string n, string v)
        {
            banc.Calque.Components.Add(new() { Ref = r, Kind = ComponentKind.VoltageSource, Position = new(x, 20), Rotation = 1, Value = v });
            banc.Calque.Components.Add(new() { Ref = "GND" + r, Kind = ComponentKind.Ground, Position = new(x, 24) });
            banc.Calque.Components.Add(new() { Ref = "NET" + r, Kind = ComponentKind.Label, Position = new(x, 20), Value = n });
        }
        Source("VD", 4, "D", "PULSE(0 5 3u 10n 10n 20u 40u)");
        Source("VH", 12, "H", "PULSE(5 0 1u 10n 10n 5u 10u)");
        banc.Transitoire.Duree = 40e-6; banc.Transitoire.Pas = 10e-9;
        banc.Graphiques.Add(["H", "D"]); banc.Graphiques.Add(["Q", "Y"]);
        Ancien.Raccorder(s, banc);   // sources tracées au pas de 4
        w.OuvrirDansOnglet(e, "Logique", null);
        w.SelectBanc(banc);
        await w.SimulateAsync();
        Assert.Null(w.Resultats!.Erreur);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        Shot(w, "49-logique-xspice.png");
        w.SelectBanc(null);
        w.ProposerReferences("Logique", w.Schema);
        Shot(w, "50-palette-logique.png");
        w.SelectBanc(banc);
        w.EditerCircuitDepuisBanc(w.EtudeCourante.Schema.Components.Single(k => k.Ref == "A1"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        w.ChampsBulle.ElementAt(1).Text = "3,3"; w.ValiderBulle();
        Assert.Equal(3.3, CircuitBuilder.TensionLogique(w.EtudeCourante.Schema.Components.Single(k => k.Ref == "A1")), 9);
        w.EditerCircuitDepuisBanc(w.EtudeCourante.Schema.Components.Single(k => k.Ref == "A1"));
        Shot(w, "56-tension-logique.png");
    }

    /// <summary>Depuis un banc : les valeurs du circuit sous test se modifient (clic sur le composant estompé, ou liste du panneau).</summary>
    [AvaloniaFact]
    public async Task Valeurs_du_circuit_modifiables_depuis_un_banc()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var banc = w.EtudeCourante.Bancs[0];
        w.SelectBanc(banc);
        await w.SimulateAsync();
        var tm = banc.Gabarits.Single(g => g.Mesure == TypeMesure.TempsDeMontee);
        double avant = w.Resultats!.Criteres.Single(c => c.Critere == tm).Resultat.Valeur!.Value;
        // Clic simple sur R1 (circuit sous test, estompé) : sa bulle s'ouvre.
        var p = Ecran(w, 6, 8);
        w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.BulleOuverte);
        var r1 = w.EtudeCourante.Schema.Components.Single(k => k.Ref == "R1");
        // La bulle est modifiable : on tape une valeur puis Annuler rétablit, OK garde.
        TextBox Valeur() => w.ChampsBulle.ElementAt(1);   // référence, puis valeur
        Valeur().Text = "3k"; w.AnnulerBulle();
        Assert.Equal("1k", r1.Value);
        p = Ecran(w, 6, 8); w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Valeur().Text = "2k"; Shot(w, "55-bulle-ok-annuler.png");
        w.ValiderBulle();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("2k", r1.Value);
        Assert.False(w.BulleOuverte);
        Assert.Same(banc, w.BancActif);   // on reste dans le banc
        await w.SimulateAsync();
        double apres = w.Resultats!.Criteres.Single(c => c.Critere == tm).Resultat.Valeur!.Value;
        Assert.InRange(apres / avant, 1.9, 2.1);   // τ = RC double
        Shot(w, "54-valeurs-depuis-banc.png");
    }

    /// <summary>Tailles : dipôles sur 3 pas, transistors sur 4 ; composants de base dans la palette.</summary>
    [AvaloniaFact]
    public void Composants_de_base_et_tailles()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var b = w.Bibliotheque.Definitions.ToDictionary(d => d.Nom);
        var e = new Silx.Etude.Etude(); var s = e.Schema;
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 0), Value = "10k" });
        s.Components.Add(new() { Ref = "C1", Kind = ComponentKind.Capacitor, Position = new(5, 0), Value = "100n" });
        s.Components.Add(new() { Ref = "L1", Kind = ComponentKind.Inductor, Position = new(10, 0), Value = "10u" });
        s.Components.Add(new() { Ref = "D1", Kind = ComponentKind.Diode, Position = new(15, 0), Value = "1N4148" });
        s.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(20, -1), Rotation = 1, Value = "DC 5" });
        s.Components.Add(new() { Ref = "Q1", Kind = ComponentKind.Spice, Definition = b["2N3904"], Position = new(0, 6) });
        s.Components.Add(new() { Ref = "Q2", Kind = ComponentKind.Spice, Definition = b["2N3906"], Position = new(5, 6) });
        s.Components.Add(new() { Ref = "X1", Kind = ComponentKind.Spice, Definition = b["2N7000"], Position = new(10, 6) });
        s.Components.Add(new() { Ref = "X2", Kind = ComponentKind.Spice, Definition = b["BS250"], Position = new(15, 6) });
        s.Components.Add(new() { Ref = "J1", Kind = ComponentKind.Spice, Definition = b["J201"], Position = new(20, 6) });
        s.Components.Add(new() { Ref = "X3", Kind = ComponentKind.Spice, Definition = b["Quartz 16 MHz"], Position = new(25, 6) });
        s.Components.Add(new() { Ref = "A1", Kind = ComponentKind.Spice, Definition = b["NAND2"], Position = new(25, 0) });
        w.OuvrirDansOnglet(e, "Composants", null);
        w.SelectBanc(null);
        w.Schema.FitAll();
        Shot(w, "57-tailles-composants.png");
        w.ProposerReferences("MOSFET canal N", w.Schema);
        Shot(w, "58-palette-base.png");
    }

    /// <summary>Alimentation dans le circuit sous test (polarisation commune à tous les bancs) : palette active, référence libre, simulée.</summary>
    [AvaloniaFact]
    public async Task Alimentation_dans_le_circuit_de_base()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.SelectBanc(null);
        Assert.True(w.BoutonPalette(ComponentKind.VoltageSource).IsEnabled);
        // VCC 5 V sur OUT à travers 1 MΩ, dans le circuit : tous les bancs la voient ; elle ne s'appelle pas V1 (pris par les bancs).
        w.Schema.BeginPlace(ComponentKind.VoltageSource);
        var p = Ecran(w, 20, 4);
        w.MouseMove(p); w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left);
        var vcc = w.EtudeCourante.Schema.Components.Single(k => k.Kind == ComponentKind.VoltageSource);
        Assert.NotEqual("V1", vcc.Ref);
        vcc.Value = "DC 5";
        w.EtudeCourante.Schema.Components.Add(new() { Ref = "GND9", Kind = ComponentKind.Ground, Position = new(vcc.Pins[1].X, vcc.Pins[1].Y) });
        w.EtudeCourante.Schema.Components.Add(new() { Ref = "NET9", Kind = ComponentKind.Label, Position = vcc.Pins[0], Value = "VCC" });
        w.EtudeCourante.Schema.Components.Add(new() { Ref = "R9", Kind = ComponentKind.Resistor, Position = vcc.Pins[0], Rotation = 3, Value = "1meg" });   // charge vers le haut
        w.EtudeCourante.Schema.Components.Add(new() { Ref = "GND8", Kind = ComponentKind.Ground, Position = new(vcc.Pins[0].X, vcc.Pins[0].Y - 3) });
        var banc = w.EtudeCourante.Bancs[0];
        w.SelectBanc(banc);
        await w.SimulateAsync();
        Assert.Null(w.Resultats!.Erreur);
        Assert.Equal(AnalyseKind.Transitoire, banc.Type);   // le type du banc vient toujours de ses propres stimuli
        Assert.InRange(w.Resultats.Resultats[AnalyseKind.Transitoire].Vector("v(vcc)")[^1], 4.99, 5.01);
        w.SelectBanc(null);
        Shot(w, "59-alimentation-circuit.png");
    }

    /// <summary>Impédance du quartz 32,768 kHz affichée en balayage linéaire étroit.</summary>
    [AvaloniaFact]
    public async Task Impedance_du_quartz_affichee()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var q = w.Bibliotheque.Definitions.Single(d => d.Nom == "Quartz 32,768 kHz");
        var e = new Silx.Etude.Etude(); var s = e.Schema;
        s.Components.Add(new() { Ref = "X1", Kind = ComponentKind.Spice, Definition = q, Position = new(10, 8) });
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(13, 8) });
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(10, 8), Value = "Z" });
        var banc = new Banc { Nom = "Impédance", Type = AnalyseKind.Ac }; e.Bancs.Add(banc);
        banc.Calque.Components.Add(new() { Ref = "I1", Kind = ComponentKind.CurrentSource, Position = new(4, 11), Rotation = 3, Value = "AC 1" });
        banc.Calque.Components.Add(new() { Ref = "GND2", Kind = ComponentKind.Ground, Position = new(4, 11) });
        banc.Calque.Wires.Add(new(new(4, 8), new(10, 8)));
        banc.Ac.Lineaire = true; banc.Ac.FDebut = 32.72e3; banc.Ac.FFin = 32.82e3; banc.Ac.PointsParDecade = 10001;
        banc.Graphiques.Add(["Z"]);
        w.OuvrirDansOnglet(e, "Quartz", null);
        w.SelectBanc(banc);
        await w.SimulateAsync();
        Assert.Null(w.Resultats!.Erreur);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        Shot(w, "60-impedance-quartz.png");
        w.OuvrirReglagesBanc(w.Schema);
        Shot(w, "61-reglage-lineaire.png");
    }

    /// <summary>
    /// Plantage signalé : analyse en décade de 32 670 à 32 680 Hz (50 points par décade → un seul point), le tracé levait
    /// une exception. Désormais au moins 100 points, et une erreur de tracé est signalée sans fermer l'application.
    /// </summary>
    [AvaloniaFact]
    public async Task Bande_etroite_en_decade_ne_plante_plus()
    {
        Assert.True(CircuitBuilder.PointsAc(new ReglagesAc { FDebut = 32670, FFin = 32680, PointsParDecade = 50 }) * Math.Log10(32680.0 / 32670) >= 99);
        Assert.Equal(50, CircuitBuilder.PointsAc(new ReglagesAc { FDebut = 1, FFin = 1e6, PointsParDecade = 50 }));
        Assert.Equal(2, CircuitBuilder.PointsAc(new ReglagesAc { Lineaire = true, PointsParDecade = 1 }));
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var bode = w.EtudeCourante.Bancs[1];
        bode.Ac.FDebut = 32670; bode.Ac.FFin = 32680; bode.Ac.PointsParDecade = 50; bode.Ac.Lineaire = false;
        w.SelectBanc(bode);
        await w.SimulateAsync();
        Assert.Null(w.Resultats!.Erreur);
        Assert.True(w.Resultats.Resultats[AnalyseKind.Ac].PointCount >= 100);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        // Filet de sécurité : une erreur de tracé est affichée, pas propagée.
        w.SignalerErreur(new InvalidOperationException("essai"), "test");
        Assert.Contains("essai", w.Statut);
    }
}
