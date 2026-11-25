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
        var c1 = e.Schema.Components.Single(c => c.Ref == "C1");   // (12,8)-(12,12), vertical
        var p0 = Ecran(w, 12, 10);
        w.MouseMove(p0); w.MouseDown(p0, MouseButton.Left);
        foreach (var k in new[] { 1.0, 2.0, 3.0 }) { w.MouseMove(Ecran(w, 12 + k, 10 - k)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Shot(w, "16-pendant-glisser.png");
        w.MouseUp(Ecran(w, 15, 7), MouseButton.Left);
        Assert.Equal(new GridPoint(15, 5), c1.Position);
        Assert.All(e.Schema.Wires, x => Assert.True(x.A.X == x.B.X || x.A.Y == x.B.Y));
        Assert.True(Elastique.MemesConnexions(avant, e.Bancs[1].Fusion(e.Schema)));  // le banc Bode suit aussi
        Shot(w, "17-apres-glisser.png");
        w.Schema.Undo();
        Assert.Equal(new GridPoint(12, 8), e.Schema.Components.Single(c => c.Ref == "C1").Position);
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
}
