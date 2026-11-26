using System.Text.Json;
using Avalonia.Media;
namespace Silx.App;

/// <summary>
/// Préférences de l'application (fenêtre Paramètres), enregistrées en JSON dans le dossier de préférences de l'utilisateur
/// (SILX_PARAMETRES pour un autre fichier, utilisé par les tests).
/// </summary>
public sealed class Parametres
{
    // Général
    public bool SimulationAuto { get; set; } = true;
    /// <summary>Sensibilité du zoom à la molette et au pavé tactile : de 1 (très douce) à 5 (très rapide).</summary>
    public int NiveauZoom { get; set; } = 3;
    /// <summary>Apparence : « clair », « sombre » ou « systeme » (suit macOS).</summary>
    public string Theme { get; set; } = "clair";
    // Affichage
    public string CouleurBanc { get; set; } = "#7c3aed";
    public string CouleurGabarit { get; set; } = "#b91c1c";
    public string CouleurConforme { get; set; } = "#15803d";
    public string CouleurNonConforme { get; set; } = "#b91c1c";
    public double EpaisseurCourbes { get; set; } = 2.5;
    // Dispersion : tolérances par défaut (± %) utilisées par Monte-Carlo et les coins quand un composant n'a pas la sienne.
    public double ToleranceResistance { get; set; } = 1;
    public double ToleranceCondensateur { get; set; } = 10;
    public double ToleranceInductance { get; set; } = 10;
    public double ToleranceSource { get; set; } = 0;

    public static Parametres Courant { get; private set; } = Charger();

    public static string Fichier()
        => Environment.GetEnvironmentVariable("SILX_PARAMETRES") is { Length: > 0 } f ? f
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Silx", "parametres.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    static Parametres Charger()
    {
        Parametres p;
        try { p = File.Exists(Fichier()) ? JsonSerializer.Deserialize<Parametres>(File.ReadAllText(Fichier())) ?? new() : new(); }
        catch (Exception) { p = new(); }   // fichier illisible : valeurs par défaut
        p.Appliquer();
        return p;
    }

    public void Enregistrer()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(Fichier())!); File.WriteAllText(Fichier(), JsonSerializer.Serialize(this, Json)); }
        catch (Exception) { /* préférences non enregistrées : l'application continue avec les valeurs en mémoire */ }
    }

    /// <summary>Remet les valeurs par défaut d'une section.</summary>
    public void Reinitialiser()
    {
        var d = new Parametres();
        SimulationAuto = d.SimulationAuto; NiveauZoom = d.NiveauZoom; Theme = d.Theme;
        CouleurBanc = d.CouleurBanc; CouleurGabarit = d.CouleurGabarit; CouleurConforme = d.CouleurConforme; CouleurNonConforme = d.CouleurNonConforme;
        EpaisseurCourbes = d.EpaisseurCourbes;
        ToleranceResistance = d.ToleranceResistance; ToleranceCondensateur = d.ToleranceCondensateur; ToleranceInductance = d.ToleranceInductance; ToleranceSource = d.ToleranceSource;
    }

    public static Color Lire(string hex, string defaut) => Color.TryParse(hex, out var c) ? c : Color.Parse(defaut);
    public Color Banc => Lire(CouleurBanc, "#7c3aed");
    public Color Gabarit => Lire(CouleurGabarit, "#b91c1c");
    public Color Conforme => Lire(CouleurConforme, "#15803d");
    public Color NonConforme => Lire(CouleurNonConforme, "#b91c1c");
    public static ScottPlot.Color VersPlot(Color c) => new(c.R, c.G, c.B, c.A);
    /// <summary>Facteur de zoom par cran de molette.</summary>
    public double BaseZoom => Math.Clamp(NiveauZoom, 1, 5) switch { 1 => 1.015, 2 => 1.03, 4 => 1.10, 5 => 1.15, _ => 1.06 };
    public static readonly string[] LibellesZoom = ["Très douce", "Douce", "Normale", "Rapide", "Très rapide"];

    /// <summary>Recolore les pinceaux partagés : tout ce qui les utilise suit sans reconstruction.</summary>
    public void Appliquer()
    {
        Silx.Etude.Campagnes.ToleranceParDefaut = k => (k switch
        {
            Silx.Etude.ComponentKind.Resistor => ToleranceResistance, Silx.Etude.ComponentKind.Capacitor => ToleranceCondensateur,
            Silx.Etude.ComponentKind.Inductor => ToleranceInductance,
            Silx.Etude.ComponentKind.VoltageSource or Silx.Etude.ComponentKind.CurrentSource => ToleranceSource, _ => 0
        }) is > 0 and var t ? t : null;
        if (Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = Theme switch { "sombre" => Avalonia.Styling.ThemeVariant.Dark, "systeme" => Avalonia.Styling.ThemeVariant.Default, _ => Avalonia.Styling.ThemeVariant.Light };
            Ui.AppliquerTheme(app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark);
        }
        else Ui.AppliquerTheme(Theme == "sombre");
        ((SolidColorBrush)SchematicCanvas.BenchBrush).Color = Banc;
        var b = Banc;
        ((SolidColorBrush)Ui.BenchBg).Color = Color.FromRgb((byte)(255 - (255 - b.R) * 0.09), (byte)(255 - (255 - b.G) * 0.09), (byte)(255 - (255 - b.B) * 0.09));
        ((SolidColorBrush)Ui.PassBrush).Color = Conforme;
        ((SolidColorBrush)Ui.FailBrush).Color = NonConforme;
    }
}
