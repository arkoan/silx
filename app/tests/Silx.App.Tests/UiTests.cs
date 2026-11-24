using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
[assembly: AvaloniaTestApplication(typeof(Silx.App.Tests.TestApp))]
namespace Silx.App.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
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
}
