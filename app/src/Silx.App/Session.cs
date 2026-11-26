using System.Text.Json;
namespace Silx.App;

/// <summary>
/// État de travail conservé d'une ouverture à l'autre : onglets de projets (fichier, contenu non enregistré, démo),
/// onglet et banc actifs, disposition de l'écran, tiroir du bas. Fichier : ~/Library/Application Support/Silx/session.json
/// (SILX_SESSION pour un autre fichier ; « off » désactive, utilisé par les tests).
/// </summary>
public sealed class Session
{
    public sealed class Onglet
    {
        public string Nom { get; set; } = "";
        /// <summary>Fichier .silx du projet (null s'il n'a jamais été enregistré).</summary>
        public string? Chemin { get; set; }
        /// <summary>Contenu à restaurer : projet jamais enregistré, ou modifications non enregistrées.</summary>
        public string? Contenu { get; set; }
        /// <summary>Démonstration intégrée non modifiée : recréée telle qu'à jour.</summary>
        public string? Demo { get; set; }
        /// <summary>Banc affiché (indice), −1 : circuit sous test.</summary>
        public int Banc { get; set; } = -1;
    }

    public List<Onglet> Onglets { get; set; } = new();
    public int Actif { get; set; }
    public string Disposition { get; set; } = "Mixte";
    public string? Detail { get; set; }
    public double HauteurDetails { get; set; } = 200;

    public static string? Fichier()
    {
        var env = Environment.GetEnvironmentVariable("SILX_SESSION");
        if (env == "off") return null;
        return env is { Length: > 0 } ? env : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Silx", "session.json");
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Session? Charger(string? fichier = null)
    {
        fichier ??= Fichier();
        try { return fichier != null && File.Exists(fichier) ? JsonSerializer.Deserialize<Session>(File.ReadAllText(fichier)) : null; }
        catch (Exception) { return null; }   // session illisible : démarrage par défaut
    }

    public void Enregistrer(string? fichier = null)
    {
        fichier ??= Fichier();
        if (fichier == null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(fichier)!); File.WriteAllText(fichier, JsonSerializer.Serialize(this, Json)); }
        catch (Exception) { /* état non conservé : sans gravité */ }
    }
}
