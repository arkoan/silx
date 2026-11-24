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
    internal static void Init() => Environment.SetEnvironmentVariable("SILX_COMPOSANTS", Path.Combine(Path.GetTempPath(), "silx-tests-" + Guid.NewGuid().ToString("N")));
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
        w.EtudeCourante.Schema.Wires.Add(new(new(23, 5), new(23, 1)));
        w.EtudeCourante.Schema.Components.Add(new() { Ref = "NET9", Kind = ComponentKind.Label, Position = new(23, 1), Value = "VC" });
        var ed = new EditeurSymbole(q);
        ed.DeplacerBroche(1, 5, -3);
        ed.Enregistrer();
        w.AppliquerSymbole(q, ed.Resultat!);
        Assert.Equal(new GridPoint(25, 5), inst.Pins[1]);
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
}
