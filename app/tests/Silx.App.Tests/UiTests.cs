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
}
