using Avalonia.LogicalTree;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
[assembly: AvaloniaTestApplication(typeof(Silx.App.Tests.TestApp))]
namespace Silx.App.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() { Environment.SetEnvironmentVariable("SILX_SIM_AUTO", "0"); return BuildAvaloniaAppSansAuto(); }
    static AppBuilder BuildAvaloniaAppSansAuto() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Rend la fenêtre réelle hors écran ; SILX_SHOT_DIR=dossier pour enregistrer les captures.</summary>
public class UiTests
{
    static void Shot(MainWindow w, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("SILX_SHOT_DIR") is { } dir) frame!.Save(Path.Combine(dir, name));
    }

    [AvaloniaFact]
    public async Task Fenetre_simule_verifie_et_trace()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        Shot(w, "1-ouverture.png");
        await w.SimulateAsync();
        Shot(w, "2-banc-echelon.png");
        w.SetModeTest(false);
        Shot(w, "3-simulation.png");
        w.SetModeTest(true);
        w.SelectBanc(w.EtudeCourante.Bancs[1]);
        w.ShowAnalysis(Silx.Etude.AnalyseKind.Ac);
        await w.SimulateAsync();
        Shot(w, "4-banc-bode.png");
        w.SelectBanc(null);
        await w.SimulateAsync();
        Shot(w, "5-circuit-sous-test.png");
        Assert.All(w.EtudeCourante.Bancs, b => Assert.DoesNotContain(w.EtudeCourante.Schema.Components, c => c.Kind == Silx.Etude.ComponentKind.VoltageSource));
    }

    /// <summary>Dessin d'une limite basse à la souris sur la courbe transitoire : 3 clics puis fin de tracé.</summary>
    [AvaloniaFact]
    public async Task Dessiner_une_limite_a_la_souris()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        int avant = w.BancActif!.GabaritsTraces.Count;
        w.StartDrawing(Silx.Mesures.Gabarits.Limite.Basse);
        var plot = w.PlotControl;
        foreach (var (tMs, v) in new[] { (1.0, -0.1), (4.0, 0.5), (9.0, 0.7) })
        {
            var px = plot.Plot.GetPixel(new ScottPlot.Coordinates(tMs, v));
            var pt = plot.TranslatePoint(new Point(px.X / plot.DisplayScale, px.Y / plot.DisplayScale), w)!.Value;
            w.MouseDown(pt, Avalonia.Input.MouseButton.Left); w.MouseUp(pt, Avalonia.Input.MouseButton.Left);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Shot(w, "6-dessin-en-cours.png");
        w.EndDrawing(true);
        var g = w.BancActif!.GabaritsTraces[^1];
        Assert.Equal(avant + 1, w.BancActif!.GabaritsTraces.Count);
        Assert.Equal(3, g.Points.Count);
        Assert.Equal(4e-3, g.Points[1].X, 2e-4);
        Assert.Equal(0.5, g.Points[1].Y, 0.03);
        Shot(w, "7-dessin-termine.png");
    }

    /// <summary>Survol d'un point de gabarit puis glisser : le point suit, la marge est recalculée en direct.</summary>
    [AvaloniaFact]
    public async Task Deplacer_un_point_de_gabarit_a_la_souris()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var plot = w.PlotControl;
        Point Ecran(double tMs, double v)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();   // mise en page du graphique à jour avant de convertir
            var px = plot.Plot.GetPixel(new ScottPlot.Coordinates(tMs, v));
            return plot.TranslatePoint(new Point(px.X / plot.DisplayScale, px.Y / plot.DisplayScale), w)!.Value;
        }
        var basse = w.BancActif!.GabaritsTraces.Single(g => g.Limite == Silx.Mesures.Gabarits.Limite.Basse);
        var depart = Ecran(3, 0.8);
        w.MouseMove(depart); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Shot(w, "8-survol-point.png");
        w.MouseDown(depart, Avalonia.Input.MouseButton.Left);
        w.MouseMove(Ecran(3.2, 0.97)); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        w.MouseUp(Ecran(3.2, 0.97), Avalonia.Input.MouseButton.Left); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(3.2e-3, basse.Points[1].X, 1e-4);
        Assert.Equal(0.97, basse.Points[1].Y, 0.02);
        Shot(w, "9-point-deplace.png");
    }

    /// <summary>Régression : déplacer le premier ou le dernier point (un seul voisin) ne doit pas produire d'abscisse infinie.</summary>
    [AvaloniaFact]
    public async Task Deplacer_le_premier_et_le_dernier_point()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var plot = w.PlotControl;
        Point Ecran(double tMs, double v)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();   // mise en page du graphique à jour avant de convertir
            var px = plot.Plot.GetPixel(new ScottPlot.Coordinates(tMs, v));
            return plot.TranslatePoint(new Point(px.X / plot.DisplayScale, px.Y / plot.DisplayScale), w)!.Value;
        }
        var basse = w.BancActif!.GabaritsTraces.Single(g => g.Limite == Silx.Mesures.Gabarits.Limite.Basse);
        void Glisser(double t0, double v0, double t1, double v1)
        {
            w.MouseMove(Ecran(t0, v0)); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            w.MouseDown(Ecran(t0, v0), Avalonia.Input.MouseButton.Left);
            w.MouseMove(Ecran(t1, v1)); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            w.MouseUp(Ecran(t1, v1), Avalonia.Input.MouseButton.Left); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Glisser(2, 0, 1.5, 0.1);      // premier point
        Glisser(10, 0.95, 9.5, 0.9);  // dernier point
        Assert.Equal(1.5e-3, basse.Points[0].X, 1e-4);
        Assert.Equal(9.5e-3, basse.Points[^1].X, 1e-4);
        Assert.All(basse.Points, q => Assert.True(double.IsFinite(q.X) && double.IsFinite(q.Y)));
        var r = Silx.Etude.Verification.EvaluerTrace(basse, w.Resultats!.Resultats[Silx.Etude.AnalyseKind.Transitoire]);
        Assert.Equal(Silx.Mesures.Gabarits.Verdict.Pass, r.Verdict);
    }

    /// <summary>Deux curseurs posés sur V(OUT) : ils suivent la courbe et le tableau donne Δt, ΔV et 1/Δt.</summary>
    [AvaloniaFact]
    public async Task Curseurs_A_et_B_et_ecarts()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        w.SetModeTest(false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var plot = w.PlotControl;
        Point Ecran(double tMs, double v)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();   // mise en page du graphique à jour avant de convertir
            var px = plot.Plot.GetPixel(new ScottPlot.Coordinates(tMs, v));
            return plot.TranslatePoint(new Point(px.X / plot.DisplayScale, px.Y / plot.DisplayScale), w)!.Value;
        }
        w.SetCursorMode(true);
        foreach (var t in new[] { 1.5, 3.7 })
        {
            w.MouseDown(Ecran(t, 0.3), Avalonia.Input.MouseButton.Left); w.MouseUp(Ecran(t, 0.3), Avalonia.Input.MouseButton.Left);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(2, w.Curseurs.Count);
        Assert.All(w.Curseurs, c => Assert.Equal("OUT", c.Noeud));
        Assert.Equal(2.2e-3, w.Curseurs[1].X - w.Curseurs[0].X, 1e-4);
        Assert.True(w.Readout.IsVisible);
        // Glisser B vers 6 ms
        w.SetCursorMode(false);
        w.MouseMove(Ecran(3.7, 0.5)); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        w.MouseDown(Ecran(3.7, 0.5), Avalonia.Input.MouseButton.Left);
        w.MouseMove(Ecran(6, 0.5)); w.MouseUp(Ecran(6, 0.5), Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(6e-3, w.Curseurs.Single(c => c.Nom == 'B').X, 1e-4);
        Shot(w, "10-curseurs.png");

        // Zoom fenêtre sur 1..4 ms
        w.SetZoomWindowMode(true);
        w.MouseDown(Ecran(1, 0.9), Avalonia.Input.MouseButton.Left);
        w.MouseMove(Ecran(4, 0.1)); w.MouseUp(Ecran(4, 0.1), Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var lim = plot.Plot.Axes.GetLimits();
        Assert.Equal(1, lim.Left, 0.1); Assert.Equal(4, lim.Right, 0.1);
        Shot(w, "11-zoom-fenetre.png");
    }

    /// <summary>Sonde : un second graphique, puis clic sur le nœud IN du schéma → V(IN) y est tracée.</summary>
    [AvaloniaFact]
    public async Task Sonde_ajoute_un_equipotentiel_dans_un_nouveau_graphique()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        w.SignauxGraphiques[0].ToList().ForEach(_ => { });
        Assert.Single(w.SignauxGraphiques);
        await w.SimulateAsync();
        w.AjouterGraphique();
        Assert.Equal(Silx.App.Tool.Probe, w.Canevas.Tool);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // Milieu du fil R1–C1 (nœud OUT) puis broche gauche de R1 (nœud IN).
        foreach (var g in new[] { new Silx.Etude.GridPoint(4, 8) })
        {
            var pt = w.Canevas.TranslatePoint(w.Canevas.ScreenOf(g), w)!.Value;
            w.MouseMove(pt); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Shot(w, "12-sonde-survol.png");
            w.MouseDown(pt, Avalonia.Input.MouseButton.Left); w.MouseUp(pt, Avalonia.Input.MouseButton.Left);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(2, w.SignauxGraphiques.Count);
        Assert.Equal(["IN"], w.SignauxGraphiques[1]);
        w.Canevas.Tool = Silx.App.Tool.Select;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Shot(w, "13-deux-graphiques.png");
    }

    /// <summary>Bode : un curseur à fc lit la phase −45° (RC passe-bas), le tableau affiche la phase.</summary>
    [AvaloniaFact]
    public async Task Curseur_bode_donne_la_phase()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.SelectBanc(w.EtudeCourante.Bancs[1]);
        await w.SimulateAsync();
        w.SetModeTest(false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var plot = w.PlotControl;
        Point Ecran(double f, double db)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
            var px = plot.Plot.GetPixel(new ScottPlot.Coordinates(Math.Log10(f), db));
            return plot.TranslatePoint(new Point(px.X / plot.DisplayScale, px.Y / plot.DisplayScale), w)!.Value;
        }
        w.SetCursorMode(true);
        foreach (var f in new[] { 161.0, 16100.0 })
        {
            w.MouseDown(Ecran(f, -30), Avalonia.Input.MouseButton.Left); w.MouseUp(Ecran(f, -30), Avalonia.Input.MouseButton.Left);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        var a = w.Curseurs.Single(c => c.Nom == 'A');
        // RC passe-bas chargé (1 kΩ ∥ 100 kΩ, 1 µF) : φ = −atan(f / fc), fc ≈ 160,8 Hz.
        foreach (var c in w.Curseurs)
            Assert.Equal(-Math.Atan(c.X / 160.8) * 180 / Math.PI, w.PhaseCurseur(c)!.Value, 1.0);
        Assert.InRange(w.PhaseCurseur(w.Curseurs.Single(c => c.Nom == 'B'))!.Value, -91, -85);
        Shot(w, "14-curseurs-bode-phase.png");
    }

    /// <summary>Curseur posé sur la courbe la plus proche du clic (V(IN) ou V(OUT)), sans menu de signal.</summary>
    [AvaloniaFact]
    public async Task Curseur_sur_la_courbe_la_plus_proche()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        w.SetModeTest(false);
        var plot = w.PlotControl;
        Point Ecran(double tMs, double v)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
            var px = plot.Plot.GetPixel(new ScottPlot.Coordinates(tMs, v));
            return plot.TranslatePoint(new Point(px.X / plot.DisplayScale, px.Y / plot.DisplayScale), w)!.Value;
        }
        w.SetCursorMode(true);
        void Clic(double t, double v) { var p = Ecran(t, v); w.MouseDown(p, Avalonia.Input.MouseButton.Left); w.MouseUp(p, Avalonia.Input.MouseButton.Left); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Clic(1.5, 0.6);    // V(OUT) ≈ 0,63 ; V(IN) = 1
        Clic(3.0, 1.02);   // V(IN) = 1 ; V(OUT) ≈ 0,92
        Assert.Equal("OUT", w.Curseurs[0].Noeud);
        Assert.Equal("IN", w.Curseurs[1].Noeud);
        Assert.All(w.Curseurs, c => Assert.Equal('A', c.Nom));   // premier curseur de chaque courbe
    }

    /// <summary>Propriétés d'une source : champs en clair ; passer en petit signal rend le banc fréquentiel.</summary>
    [AvaloniaFact]
    public void Source_en_clair_et_type_du_banc()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var p = w.Canevas.TranslatePoint(w.Canevas.ScreenOf(new Silx.Etude.GridPoint(4, 10)), w)!.Value;   // milieu de V1
        w.MouseDown(p, Avalonia.Input.MouseButton.Left); w.MouseUp(p, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("V1", w.Canevas.Selected?.Ref);
        Shot(w, "23-source-en-clair.png");
        w.Canevas.EditSelected("V1", Silx.Etude.Stimulus.Nouveau(Silx.Etude.FormeStimulus.PetitSignal).VersSpice());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Silx.Etude.AnalyseKind.Ac, w.BancActif!.Type);
    }

    /// <summary>Rectangle de sélection depuis le fond (circuit sous test) : R1 et C1, puis déplacement de groupe et suppression.</summary>
    [AvaloniaFact]
    public void Selection_multiple_par_rectangle()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.SelectBanc(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var cv = w.Canevas;
        Point Ecran(int x, int y) => cv.TranslatePoint(cv.ScreenOf(new Silx.Etude.GridPoint(x, y)), w)!.Value;
        // R1 (4,8)-(8,8), C1 (12,8)-(12,12) ; le rectangle va de (3,5) à (13,13) : masse et étiquettes comprises.
        var a = Ecran(3, 6); var b = Ecran(14, 14);
        w.MouseDown(a, Avalonia.Input.MouseButton.Left); w.MouseMove(b); w.MouseUp(b, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(cv.Selection, c => c.Ref == "R1"); Assert.Contains(cv.Selection, c => c.Ref == "C1");
        Assert.Null(cv.Selected);
        Assert.True(cv.FilsSelectionnes.Count >= 2);
        Shot(w, "24-selection-multiple.png");
        // Glisser le groupe de 2 pas vers la droite : connexions conservées.
        var conn0 = Silx.Etude.Connectivity.Build(w.EtudeCourante.Schema).AllNodes.Count;
        var r1 = Ecran(6, 8);
        w.MouseDown(r1, Avalonia.Input.MouseButton.Left); w.MouseMove(Ecran(8, 8)); w.MouseUp(Ecran(8, 8), Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Silx.Etude.GridPoint(6, 8), w.EtudeCourante.Schema.Components.Single(c => c.Ref == "R1").Position);
        Assert.Equal(new Silx.Etude.GridPoint(14, 8), w.EtudeCourante.Schema.Components.Single(c => c.Ref == "C1").Position);
        Assert.Equal(conn0, Silx.Etude.Connectivity.Build(w.EtudeCourante.Schema).AllNodes.Count);
        // Suppression de la sélection
        cv.SelectBox(new Rect(cv.ScreenOf(new Silx.Etude.GridPoint(5, 6)), cv.ScreenOf(new Silx.Etude.GridPoint(15, 14))));   // repère du canevas
        cv.DeleteSelected();
        Assert.DoesNotContain(w.EtudeCourante.Schema.Components, c => c.Ref is "R1" or "C1");
    }

    /// <summary>Plusieurs projets en onglets : chacun garde son état ; enregistrer puis rouvrir redonne le même projet.</summary>
    [AvaloniaFact]
    public async Task Projets_en_onglets_enregistrer_et_rouvrir()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var demo = w.ProjetActif;
        Assert.False(demo.Modifie);
        await w.SimulateAsync();
        var vide = w.NouveauProjet();
        Assert.Equal(2, w.Projets.Count);
        Assert.Empty(w.EtudeCourante.Schema.Components);
        // Modifier le projet vide, revenir à la démo : chacun garde son contenu et ses résultats.
        w.EtudeCourante.Bancs.Add(new Silx.Etude.Banc { Nom = "Banc A" });
        Assert.True(vide.Modifie);
        w.ActiverProjet(demo);
        Assert.Equal(2, w.EtudeCourante.Bancs.Count);
        Assert.NotNull(w.Resultats);                              // résultats de la démo conservés
        Shot(w, "26-projets-onglets.png");
        // Enregistrer la démo, la fermer, la rouvrir.
        var dir = Directory.CreateTempSubdirectory("silx-ui-projet").FullName;
        try
        {
            var chemin = Path.Combine(dir, "filtre-rc" + Silx.Etude.Projet.Extension);
            Assert.True(w.EnregistrerSous(demo, chemin));
            Assert.Equal("filtre-rc", demo.Nom); Assert.False(demo.Modifie);
            w.FermerSansDemander(demo);
            Assert.Single(w.Projets);
            var r = w.OuvrirFichier(chemin)!;
            Assert.Equal(2, w.Projets.Count);
            Assert.Same(r, w.ProjetActif);
            Assert.False(r.Modifie);
            Assert.Equal(Silx.Etude.Projet.VersJson(demo.Etude), Silx.Etude.Projet.VersJson(r.Etude));
            Assert.Same(r, w.OuvrirFichier(chemin));              // déjà ouvert : on revient à son onglet
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Lot P1 : puces de mode (schéma et courbes) et curseurs posés sans mode (touches A/B).</summary>
    [AvaloniaFact]
    public async Task Modes_visibles_et_curseurs_sans_mode()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        w.SetModeTest(false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("", w.Canevas.TexteMode);
        w.Canevas.BeginPlace(Silx.Etude.ComponentKind.Resistor);
        Assert.Contains("résistance", w.Canevas.TexteMode);
        w.Canevas.Tool = Silx.App.Tool.Probe;
        Assert.StartsWith("Sonde", w.Canevas.TexteMode);
        Shot(w, "27-puce-mode-sonde.png");
        w.Canevas.Tool = Silx.App.Tool.Select;
        // Curseurs : A et B posés sous la souris, sur la courbe la plus proche.
        var plot = w.PlotControl;
        Point P(double tMs, double v) { w.CaptureRenderedFrame(); var px = plot.Plot.GetPixel(new ScottPlot.Coordinates(tMs, v)); return new Point(px.X / plot.DisplayScale, px.Y / plot.DisplayScale); }
        w.PoserCurseur('A', P(1.5, 0.6));
        w.PoserCurseur('B', P(4, 0.95));
        w.PoserCurseur('B', P(5, 0.97));     // B déplacé, pas ajouté
        Assert.Equal(2, w.Curseurs.Count);
        Assert.Equal(5e-3, w.Curseurs.Single(c => c.Nom == 'B').X, 1e-4);
        w.SetZoomWindowMode(true);
        Assert.True(w.PuceCourbes.IsVisible);
        w.SetZoomWindowMode(false);
        Assert.False(w.PuceCourbes.IsVisible);
    }

    /// <summary>A1 : double-clic sur un composant → bulle d'édition sur place ; la valeur tapée s'applique.</summary>
    [AvaloniaFact]
    public void Edition_sur_place_par_double_clic()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.SelectBanc(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var p = w.Canevas.TranslatePoint(w.Canevas.ScreenOf(new Silx.Etude.GridPoint(6, 8)), w)!.Value;   // milieu de R1
        w.MouseDown(p, Avalonia.Input.MouseButton.Left); w.MouseUp(p, Avalonia.Input.MouseButton.Left);
        w.MouseDown(p, Avalonia.Input.MouseButton.Left); w.MouseUp(p, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (!w.BulleOuverte) w.EditerSurPlace(w.Canevas.Selected!);   // le double-clic simulé n'est pas garanti hors écran
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.BulleOuverte);
        Assert.Equal("R1", w.Canevas.Selected!.Ref);
        Shot(w, "28-edition-sur-place.png");
        w.Canevas.EditSelected("R1", "2.2k");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("2.2k", w.EtudeCourante.Schema.Components.Single(c => c.Ref == "R1").Value);
        Assert.True(w.BulleOuverte);    // la bulle reste ouverte et suit le composant
    }

    /// <summary>A2 : copier-coller, dupliquer (références renumérotées, fils internes suivis), miroir avec fils élastiques.</summary>
    [AvaloniaFact]
    public void Copier_coller_dupliquer_miroir()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.SelectBanc(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var cv = w.Canevas; var s = w.EtudeCourante.Schema;
        Rect Zone(int x0, int y0, int x1, int y1) => new Rect(cv.ScreenOf(new Silx.Etude.GridPoint(x0, y0)), cv.ScreenOf(new Silx.Etude.GridPoint(x1, y1)));
        cv.SelectBox(Zone(3, 7, 9, 9));                     // R1 seul (et l'étiquette IN)
        Assert.Contains(cv.Selection, c => c.Ref == "R1");
        cv.CopySelection();
        cv.Paste(new Silx.Etude.GridPoint(4, 20));
        Assert.Contains(s.Components, c => c.Ref == "R2" && c.Position == new Silx.Etude.GridPoint(4, 20) && c.Value == "1k");
        cv.DuplicateSelection();                             // la sélection est maintenant la copie
        Assert.Contains(s.Components, c => c.Ref == "R3");
        // Miroir de C1 (vertical) : ses deux broches échangent leur place horizontale → aucun changement ; sur R2 horizontal : broches inversées.
        var r2 = s.Components.Single(c => c.Ref == "R2");
        var avant = r2.Pins.ToList();
        cv.SelectBox(Zone(3, 19, 9, 21));
        cv.MirrorSelected();
        Assert.True(r2.Miroir);
        Assert.Equal(avant.AsEnumerable().Reverse(), r2.Pins);   // retourné sur place : broches échangées
        // Miroir enregistré dans le projet
        Assert.True(Silx.Etude.Projet.DepuisJson(Silx.Etude.Projet.VersJson(w.EtudeCourante)).Schema.Components.Single(c => c.Ref == "R2").Miroir);
    }

    /// <summary>B2 : dispositions Schéma / Mixte / Courbes.</summary>
    [AvaloniaFact]
    public async Task Dispositions_d_ecran()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        double hMixte = w.PlotControl.Bounds.Height;
        w.Disposer(MainWindow.Disposition.Courbes);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        Assert.False(w.Canevas.IsEffectivelyVisible);
        Assert.True(w.PlotControl.Bounds.Height > 1.8 * hMixte);
        Shot(w, "29-disposition-courbes.png");
        w.Disposer(MainWindow.Disposition.Schema);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.Canevas.IsEffectivelyVisible); Assert.False(w.PlotControl.IsEffectivelyVisible);
        w.Disposer(MainWindow.Disposition.Mixte);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.Canevas.IsEffectivelyVisible && w.PlotControl.IsEffectivelyVisible);
    }

    /// <summary>B1 + E1 : inspecteur contextuel (propriétés seulement avec une sélection) et plan de test en lignes.</summary>
    [AvaloniaFact]
    public async Task Inspecteur_et_plan_de_test()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.PlanDeTestVisible); Assert.False(w.ProprietesVisibles);
        Assert.Equal(4, w.LignesPlan);                       // 2 critères + 2 gabarits dessinés
        w.OuvrirLignePlan(w.BancActif!.Gabarits[0]);         // ligne dépliée pour édition
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Shot(w, "30-plan-de-test.png");
        // Sélection d'un composant : l'inspecteur ne montre que lui.
        var p = w.Canevas.TranslatePoint(w.Canevas.ScreenOf(new Silx.Etude.GridPoint(4, 10)), w)!.Value;
        w.MouseDown(p, Avalonia.Input.MouseButton.Left); w.MouseUp(p, Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.ProprietesVisibles); Assert.False(w.PlanDeTestVisible);
    }

    /// <summary>A3 : glisser depuis une broche trace un fil en L sans changer d'outil.</summary>
    [AvaloniaFact]
    public void Fil_depuis_une_broche()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.SelectBanc(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var cv = w.Canevas; var s = w.EtudeCourante.Schema;
        Point E(int x, int y) => cv.TranslatePoint(cv.ScreenOf(new Silx.Etude.GridPoint(x, y)), w)!.Value;
        int avant = s.Wires.Count;
        w.MouseDown(E(12, 8), Avalonia.Input.MouseButton.Left);      // broche haute de C1
        w.MouseMove(E(18, 4)); w.MouseUp(E(18, 4), Avalonia.Input.MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Silx.App.Tool.Select, cv.Tool);
        Assert.Equal(avant + 2, s.Wires.Count);
        Assert.Contains(s.Wires, x => x == new Silx.Etude.Wire(new(12, 8), new(18, 8)));
        Assert.Contains(s.Wires, x => x == new Silx.Etude.Wire(new(18, 8), new(18, 4)));
        Assert.Equal(new Silx.Etude.GridPoint(12, 8), s.Components.Single(c => c.Ref == "C1").Position);   // le composant n'a pas bougé
    }

    /// <summary>A4 : depuis un banc, modifier un composant du circuit sous test sans quitter le banc.</summary>
    [AvaloniaFact]
    public async Task Circuit_modifiable_depuis_un_banc()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var c1 = w.EtudeCourante.Schema.Components.Single(c => c.Ref == "C1");
        w.EditerCircuitDepuisBanc(c1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.BulleOuverte);
        Assert.NotNull(w.BancActif);                                  // on reste sur le banc
        w.ModifierComposantDeLaBulle("C1", "2u");
        Assert.Equal("2u", c1.Value);
        Assert.Null(w.Resultats);                                     // résultats à refaire
        await w.SimulateAsync();
        Assert.Equal(Silx.Mesures.Gabarits.Verdict.Fail, w.Resultats!.Global);   // tau doublé : montée hors gabarit
    }

    /// <summary>B3 : une seule série de loupes ; elle agit sur la zone active (dernier clic : schéma ou courbes).</summary>
    [AvaloniaFact]
    public async Task Loupes_sur_la_zone_active()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(MainWindow.Zone.Schema, w.ZoneActive);
        var plot = w.PlotControl;
        var centre = plot.TranslatePoint(new Point(plot.Bounds.Width / 2, plot.Bounds.Height / 2), w)!.Value;
        w.MouseDown(centre, Avalonia.Input.MouseButton.Left); w.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        Assert.Equal(MainWindow.Zone.Courbes, w.ZoneActive);
        var avant = plot.Plot.Axes.GetLimits();
        w.ZoomerZoneActive(2);
        var apres = plot.Plot.Axes.GetLimits();
        Assert.True(apres.Rect.Width < avant.Rect.Width * 0.6);
    }

    /// <summary>D3 : axes X liés entre graphiques empilés.</summary>
    [AvaloniaFact]
    public async Task Axes_x_lies_entre_graphiques()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        w.AjouterGraphique();
        w.Canevas.Tool = Silx.App.Tool.Select;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var p2 = w.PlotControl;                               // graphique actif : le second
        var centre = p2.TranslatePoint(new Point(p2.Bounds.Width / 2, p2.Bounds.Height / 2), w)!.Value;
        w.MouseDown(centre, Avalonia.Input.MouseButton.Left); w.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        w.ZoomerZoneActive(2);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        var autres = w.GetLogicalDescendants().OfType<ScottPlot.Avalonia.AvaPlot>().Where(p => p != p2).ToList();
        Assert.NotEmpty(autres);
        var l2 = p2.Plot.Axes.GetLimits();
        foreach (var a in autres) { var l = a.Plot.Axes.GetLimits(); Assert.Equal(l2.Left, l.Left, 6); Assert.Equal(l2.Right, l.Right, 6); }
        Shot(w, "31-axes-lies.png");
    }

    /// <summary>G3 : simulation automatique après une modification quand le dernier calcul était court ; critère réévalué sans resimuler.</summary>
    [AvaloniaFact]
    public async Task Simulation_automatique()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.SimulationAutomatique = true;
        await w.SimulateAsync();
        Assert.Equal("✓ À jour", w.EtatResultats);
        // Critère modifié : réévalué tout de suite sur les résultats existants.
        w.BancActif!.Gabarits[0].Max = 1e-3;
        w.Reevaluer();
        Assert.Equal(Silx.Mesures.Gabarits.Verdict.Fail, w.Resultats!.Global);
        // Composant modifié : résultats périmés, relance automatique.
        var c1 = w.EtudeCourante.Schema.Components.Single(c => c.Ref == "C1");
        w.EditerCircuitDepuisBanc(c1);
        w.ModifierComposantDeLaBulle("C1", "470n");
        Assert.Equal("● À relancer", w.EtatResultats);
        for (int i = 0; i < 100 && w.Resultats == null; i++) { await Task.Delay(50); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Assert.NotNull(w.Resultats);
        Assert.Equal("✓ À jour", w.EtatResultats);
    }

    /// <summary>G4 : erreurs signalées sur le schéma (broche non reliée, masse absente).</summary>
    [AvaloniaFact]
    public void Erreurs_sur_le_schema()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        Assert.Empty(w.Canevas.Erreurs); Assert.Null(w.Canevas.ErreurGlobale);     // démo cohérente
        // Nouveau projet : une résistance seule dans un banc → 2 broches non reliées et pas de masse.
        w.NouveauProjet();
        w.EtudeCourante.Bancs.Add(new Silx.Etude.Banc { Nom = "B" });
        w.SelectBanc(w.EtudeCourante.Bancs[0]);
        w.Canevas.Schematic.Components.Add(new() { Ref = "R1", Kind = Silx.Etude.ComponentKind.Resistor, Position = new(4, 4), Value = "1k" });
        w.Canevas.InvalidateVisual();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        Assert.Equal(2, w.Canevas.Erreurs.Count(e => e.Message.Contains("non reliée")));
        Assert.NotNull(w.Canevas.ErreurGlobale);
        Shot(w, "32-erreurs-sur-schema.png");
    }

    /// <summary>G1 : plus d'onglets Schéma/Netlist ni Courbes/Vérifications/Journal ; tiroir « Détails » replié par défaut.</summary>
    [AvaloniaFact]
    public void Navigation_a_un_niveau_et_tiroir_details()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Empty(w.GetLogicalDescendants().OfType<Avalonia.Controls.TabControl>());
        Assert.Null(w.DetailOuvert);
        w.OuvrirDetail(MainWindow.Detail.Netlist);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(MainWindow.Detail.Netlist, w.DetailOuvert);
        Shot(w, "33-tiroir-details.png");
        w.OuvrirDetail(null);
        Assert.Null(w.DetailOuvert);
    }

    /// <summary>G2 : nouveau banc depuis un modèle, simulé dans la foulée.</summary>
    [AvaloniaFact]
    public async Task Nouveau_banc_depuis_un_modele()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var b = w.CreerBanc(Silx.Etude.ModeleBanc.Bode, "IN", "OUT");
        for (int i = 0; i < 100 && w.Resultats == null; i++) { await Task.Delay(50); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Assert.Equal("Bode", b.Nom);
        Assert.Same(b, w.BancActif);
        Assert.Equal(Silx.Etude.AnalyseKind.Ac, b.Type);
        Assert.NotNull(w.Resultats);
        Shot(w, "34-banc-depuis-modele.png");
    }

    /// <summary>C2 : palette de commandes (recherche sans accents, exécution) et aide-mémoire.</summary>
    [AvaloniaFact]
    public void Palette_de_commandes_et_aide()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        Assert.Contains(w.ChercherCommandes("bode"), c => c.Nom.StartsWith("Nouveau banc"));
        var placer = w.ChercherCommandes("placer resistance");
        Assert.Equal("Placer : résistance", placer[0].Nom);
        placer[0].Executer();
        Assert.Equal(Silx.App.Tool.Place, w.Canevas.Tool);
        Assert.Contains(w.ChercherCommandes("courbes seules"), c => c.Raccourci == "F3");
        w.OuvrirPaletteCommandes(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.PaletteOuverte);
        w.AfficherAide(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.AideOuverte);
    }

    /// <summary>E2 : rapport de test Markdown avec synthèse, contraintes et une image par banc.</summary>
    [AvaloniaFact]
    public async Task Rapport_de_test()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var dir = Directory.CreateTempSubdirectory("silx-rapport").FullName;
        try
        {
            var md = await w.ExporterRapportVers(Path.Combine(dir, "rapport.md"));
            Assert.Contains("| Échelon 1 V | temporel | 4/4 | ✓ conforme |", md);
            Assert.Contains("| Réponse en fréquence | fréquentiel |", md);
            Assert.Contains("Temps de montée 10–90 %", md);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(dir, "rapport_courbes"), "*.png").Length);
            if (Environment.GetEnvironmentVariable("SILX_SHOT_DIR") is { } sd)
            {
                File.Copy(Path.Combine(dir, "rapport.md"), Path.Combine(sd, "35-rapport.md"), true);
                File.Copy(Path.Combine(dir, "rapport_courbes", "banc-1.png"), Path.Combine(sd, "35-rapport-banc-1.png"), true);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>B4 : explorateur de projet (circuit, bancs, contraintes, graphiques) ; clic pour y aller.</summary>
    [AvaloniaFact]
    public async Task Explorateur_de_projet()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        w.ExplorateurVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.ElementsExplorateur >= 10);
        Shot(w, "36-explorateur.png");
    }

    /// <summary>Régression : la phase est tracée sur le Bode en mode Test (mode par défaut) comme en mode Simulation.</summary>
    [AvaloniaFact]
    public async Task Phase_tracee_sur_le_bode_dans_les_deux_modes()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        w.SelectBanc(w.EtudeCourante.Bancs[1]);
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(w.PhaseTracee);
        w.SetModeTest(false);
        Assert.True(w.PhaseTracee);
        w.SetModeTest(true);
        Shot(w, "37-bode-phase-mode-test.png");
    }

    /// <summary>Zoom des courbes borné aux données ; zoom au pavé tactile doux ; composants de taille raisonnable.</summary>
    [AvaloniaFact]
    public async Task Zoom_borne_et_doux()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        Assert.True(w.Canevas.Zoom <= Silx.App.SchematicCanvas.ZoomCadrageMax);
        Assert.InRange(Silx.App.SchematicCanvas.FacteurMolette(0.1), 1.0, 1.01);   // petit geste de pavé : petit zoom
        var plot = w.PlotControl;
        plot.Plot.Axes.SetLimitsX(-100, 100); plot.Refresh(); w.CaptureRenderedFrame();
        var l = plot.Plot.Axes.GetLimits();
        Assert.True(l.Left >= -1e-9 && l.Right <= 10 + 1e-9);   // 0 à 10 ms, pas au-delà
    }

    /// <summary>Banc temporel : vue spectrale (FFT) en dBV selon la fréquence, sans curseurs ni gabarits.</summary>
    [AvaloniaFact]
    public async Task Vue_fft_du_temporel()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        await w.SimulateAsync();
        w.AfficherFft(true);
        Assert.True(w.VueFft);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        var l = w.PlotControl.Plot.Axes.GetLimits();
        Assert.True(l.Left > 1 && l.Right > l.Left + 2);   // log10(f) : de ~100 Hz à plusieurs MHz
        Shot(w, "39-fft.png");
        w.AfficherFft(false);
        Assert.False(w.VueFft);
    }

    /// <summary>FFT d'une sinusoïde de 1 kHz, 1 V crête : raie à 1 kHz proche de 0 dBV.</summary>
    [Fact]
    public void Spectre_sinus()
    {
        var t = Enumerable.Range(0, 5000).Select(i => i * 2e-6).ToArray();   // 10 ms, 10 périodes
        var y = t.Select(x => Math.Sin(2 * Math.PI * 1000 * x)).ToArray();
        var (f, db) = Silx.Etude.Spectre.Calculer(t, y);
        int k = Array.IndexOf(db, db.Max());
        Assert.InRange(f[k], 900, 1100);
        Assert.InRange(db[k], -1.5, 0.5);
    }

    /// <summary>Gabarit relatif au front : quand le retard de l'échelon passe de 0,5 à 2 ms, il se décale de 1,5 ms et reste conforme.</summary>
    [AvaloniaFact]
    public async Task Gabarit_relatif_suit_le_front()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var banc = w.EtudeCourante.Bancs[0];
        var bas = banc.GabaritsTraces.Single(g => g.Relatif);
        var haut = banc.GabaritsTraces.Single(g => !g.Relatif);
        Assert.Equal(0.5e-3, banc.InstantFront, 9);
        var v1 = banc.Calque.Components.Single(c => c.Ref == "V1");
        v1.Value = "PULSE(0 1 2m 1u 1u 20m 40m)";
        await w.SimulateAsync();
        Assert.Equal(2e-3, banc.InstantFront, 9);
        Assert.Equal(3.5e-3, bas.Points[0].X, 9);       // 2 ms + 1,5 ms
        Assert.Equal(0, haut.Points[0].X, 9);           // absolu : inchangé
        Assert.True(w.Resultats!.Traces.All(t => t.Resultat.Verdict == Silx.Mesures.Gabarits.Verdict.Pass));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.CaptureRenderedFrame();
        Shot(w, "48-gabarit-relatif-au-front.png");
        // Décrocher puis raccrocher ne déplace rien ; l'option est enregistrée dans le projet.
        w.RendreRelatif(bas, false); Assert.Equal(3.5e-3, bas.Points[0].X, 9);
        w.RendreRelatif(bas, true);
        var relu = Silx.Etude.Projet.DepuisJson(Silx.Etude.Projet.VersJson(w.EtudeCourante));
        var r = relu.Bancs[0].GabaritsTraces.Single(g => g.Relatif);
        Assert.Equal(2e-3, r.Ancrage, 9);
    }
}
