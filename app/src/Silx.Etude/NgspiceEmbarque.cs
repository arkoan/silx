namespace Silx.Etude;

/// <summary>
/// ngspice livré avec l'application (installable) : Silx.app/Contents/Resources/ngspice (macOS) ou &lt;application&gt;/ngspice
/// (Windows, Linux), avec bin/ngspice et lib/ngspice/*.cm (modèles de code XSPICE). Au premier usage, Silx écrit un
/// spinit qui charge ces modèles depuis l'emplacement réel de l'application (l'installation peut être déplacée) et
/// l'indique à chaque processus ngspice par SPICE_SCRIPTS (variables propres au processus, pas à toute l'application).
/// </summary>
public static class NgspiceEmbarque
{
    static readonly Lazy<string?> _executable = new(() => Preparer(Racine()));

    /// <summary>Exécutable ngspice embarqué, prêt à l'emploi ; null si l'application n'en contient pas.</summary>
    public static string? Executable => _executable.Value;

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Environnements = new();

    /// <summary>Variables d'environnement à donner au processus ngspice (ngspice embarqué préparé) ; vide sinon.</summary>
    public static IReadOnlyDictionary<string, string> Environnement(string executable) =>
        Environnements.TryGetValue(executable, out var e) ? e : new Dictionary<string, string>();

    static string NomExe => OperatingSystem.IsWindows() ? "ngspice_con.exe" : "ngspice";

    public static string? Racine()
    {
        var b = AppContext.BaseDirectory;
        foreach (var c in new[] { Path.Combine(b, "..", "Resources", "ngspice"), Path.Combine(b, "ngspice") })
            if (File.Exists(Path.Combine(c, "bin", NomExe))) return Path.GetFullPath(c);
        return null;
    }

    /// <summary>Écrit le spinit des modèles de code de <paramref name="racine"/> et renvoie l'exécutable (null sans racine).</summary>
    public static string? Preparer(string? racine, string? dossierScripts = null)
    {
        if (racine == null) return null;
        dossierScripts ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Silx", "ngspice");
        try
        {
            Directory.CreateDirectory(dossierScripts);
            var modeles = Directory.Exists(Path.Combine(racine, "lib", "ngspice"))
                ? Directory.GetFiles(Path.Combine(racine, "lib", "ngspice"), "*.cm").OrderBy(f => f, StringComparer.Ordinal).ToList() : [];
            var lignes = new List<string> { "* spinit écrit par Silx pour le ngspice embarqué", "alias exit quit", "if $?xspice_enabled" };
            lignes.AddRange(modeles.Select(m => $" codemodel {m}"));
            lignes.Add("end");
            File.WriteAllLines(Path.Combine(dossierScripts, "spinit"), lignes);
            Environnements[Path.Combine(racine, "bin", NomExe)] = new Dictionary<string, string>
            {
                ["SPICE_SCRIPTS"] = dossierScripts, ["SPICE_LIB_DIR"] = Path.Combine(racine, "share", "ngspice"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sans spinit, ngspice reste utilisable pour l'analogique ; seuls les composants logiques XSPICE manqueraient.
        }
        return Path.Combine(racine, "bin", NomExe);
    }
}
