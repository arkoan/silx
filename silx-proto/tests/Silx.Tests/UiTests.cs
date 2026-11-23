using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Silx.Editor;
using Xunit;
[assembly: AvaloniaTestApplication(typeof(Silx.Tests.TestApp))]
namespace Silx.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class UiTests
{
    [AvaloniaFact]
    public async Task Fenetre_simule_et_trace_la_courbe()
    {
        var w = new MainWindow(); w.Show();
        await w.SimulateAsync();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var outDir = Environment.GetEnvironmentVariable("SILX_SHOT_DIR");
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (outDir != null) frame!.Save(Path.Combine(outDir, "editor.png"));
    }
}
