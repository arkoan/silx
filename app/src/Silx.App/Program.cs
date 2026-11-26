using Avalonia;
namespace Silx.App;
static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Dernier filet : une erreur hors de l'interface (tâche oubliée, thread) est au moins journalisée.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Journal.Ecrire("erreur fatale", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Journal.Ecrire("tâche non observée", e.Exception); e.SetObserved(); };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}

/// <summary>Journal des erreurs : ~/Library/Logs/Silx/silx.log (SILX_JOURNAL pour un autre dossier).</summary>
public static class Journal
{
    public static string Dossier() => Environment.GetEnvironmentVariable("SILX_JOURNAL") is { Length: > 0 } j ? j
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "Silx");

    /// <summary>Ajoute une entrée datée ; renvoie le chemin du journal, ou null s'il n'a pas pu être écrit.</summary>
    public static string? Ecrire(string ou, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Dossier()); var f = Path.Combine(Dossier(), "silx.log");
            File.AppendAllText(f, $"── {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {ou}\n{ex}\n\n");
            return f;
        }
        catch (Exception) { return null; }   // journal impossible : rien de plus à faire
    }
}
