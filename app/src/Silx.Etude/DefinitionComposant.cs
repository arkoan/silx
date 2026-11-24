using System.Text.Json;
using System.Text.Json.Serialization;
namespace Silx.Etude;

public enum FormePrimitive { Trait, Rectangle, Cercle, Polygone, Fleche, Texte }

/// <summary>
/// Élément graphique d'un symbole, en pas de grille (réels), dans le repère du symbole (origine = point d'accroche).
/// Trait/Rectangle/Flèche : (X1,Y1)→(X2,Y2) ; Cercle : centre (X1,Y1), rayon X2 ; Polygone : Points ; Texte : (X1,Y1).
/// </summary>
public sealed class Primitive
{
    public FormePrimitive Forme { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public bool Plein { get; set; }
    public string? Texte { get; set; }
    public List<double[]>? Points { get; set; }

    public static Primitive Trait(double x1, double y1, double x2, double y2) => new() { Forme = FormePrimitive.Trait, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
    public static Primitive Fleche(double x1, double y1, double x2, double y2) => new() { Forme = FormePrimitive.Fleche, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
    public static Primitive Cercle(double cx, double cy, double r) => new() { Forme = FormePrimitive.Cercle, X1 = cx, Y1 = cy, X2 = r };
    public static Primitive Rect(double x1, double y1, double x2, double y2) => new() { Forme = FormePrimitive.Rectangle, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
    public static Primitive Poly(bool plein, params double[] xy) => new() { Forme = FormePrimitive.Polygone, Plein = plein, Points = Enumerable.Range(0, xy.Length / 2).Select(i => new[] { xy[2 * i], xy[2 * i + 1] }).ToList() };
    public static Primitive Txt(double x, double y, string t) => new() { Forme = FormePrimitive.Texte, X1 = x, Y1 = y, Texte = t };

    public Primitive Clone() => new() { Forme = Forme, X1 = X1, Y1 = Y1, X2 = X2, Y2 = Y2, Plein = Plein, Texte = Texte, Points = Points?.Select(p => p.ToArray()).ToList() };
}

/// <summary>Broche du symbole : point de grille où l'on branche un fil, et broche du modèle qu'elle représente.</summary>
public sealed class BrocheSymbole
{
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>Nom de la broche du modèle reliée (null = non reliée).</summary>
    public string? BrocheModele { get; set; }
    public BrocheSymbole Clone() => new() { X = X, Y = Y, BrocheModele = BrocheModele };
}

/// <summary>Gabarit de dessin d'un symbole.</summary>
public enum GabaritSymbole { Boite, Diode, Npn, Pnp, Nmos, Pmos, Njf, Pjf }

public sealed class Symbole
{
    public GabaritSymbole Gabarit { get; set; } = GabaritSymbole.Boite;
    /// <summary>Corps rectangulaire (gabarit Boîte) : les broches hors du corps y sont reliées par un trait, et leur nom est écrit à l'intérieur.</summary>
    public double[]? Corps { get; set; }
    public List<Primitive> Traits { get; set; } = new();
    public List<BrocheSymbole> Broches { get; set; } = new();
    public Symbole Clone() => new() { Gabarit = Gabarit, Corps = Corps?.ToArray(), Traits = Traits.Select(t => t.Clone()).ToList(), Broches = Broches.Select(b => b.Clone()).ToList() };

    /// <summary>Encombrement en pas de grille : (xmin, ymin, xmax, ymax), broches et dessin compris.</summary>
    public (double X0, double Y0, double X1, double Y1) Encombrement()
    {
        var xs = new List<double>(); var ys = new List<double>();
        foreach (var b in Broches) { xs.Add(b.X); ys.Add(b.Y); }
        if (Corps is { } c) { xs.Add(c[0]); xs.Add(c[2]); ys.Add(c[1]); ys.Add(c[3]); }
        foreach (var t in Traits)
            switch (t.Forme)
            {
                case FormePrimitive.Cercle: xs.Add(t.X1 - t.X2); xs.Add(t.X1 + t.X2); ys.Add(t.Y1 - t.X2); ys.Add(t.Y1 + t.X2); break;
                case FormePrimitive.Polygone: foreach (var p in t.Points ?? []) { xs.Add(p[0]); ys.Add(p[1]); } break;
                case FormePrimitive.Texte: xs.Add(t.X1); ys.Add(t.Y1); break;
                default: xs.Add(t.X1); xs.Add(t.X2); ys.Add(t.Y1); ys.Add(t.Y2); break;
            }
        if (xs.Count == 0) return (0, 0, 1, 1);
        return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }
}

/// <summary>
/// Composant défini par l'utilisateur à partir d'un modèle SPICE importé : texte SPICE, broches du modèle, symbole et
/// correspondance broches du symbole → broches du modèle. Enregistré en JSON (.silxcomp).
/// </summary>
public sealed class DefinitionComposant
{
    public string Nom { get; set; } = "";
    public TypeModele Type { get; set; }
    /// <summary>Nom du .model ou du .subckt, écrit sur la ligne d'instance.</summary>
    public string Modele { get; set; } = "";
    /// <summary>Texte SPICE d'origine (.model … ou .subckt … .ends), recopié une fois dans la netlist.</summary>
    public string TexteSpice { get; set; } = "";
    /// <summary>Broches du modèle, dans l'ordre de la ligne d'instance SPICE.</summary>
    public List<string> BrochesModele { get; set; } = new();
    /// <summary>Paramètres par défaut d'un sous-circuit (« params: » du fichier), pour information.</summary>
    public string Parametres { get; set; } = "";
    public string Description { get; set; } = "";
    public Symbole Symbole { get; set; } = new();

    [JsonIgnore] public string Lettre => ModelesSpice.Lettre(Type);

    public DefinitionComposant Clone() => new()
    {
        Nom = Nom, Type = Type, Modele = Modele, TexteSpice = TexteSpice, BrochesModele = BrochesModele.ToList(),
        Parametres = Parametres, Description = Description, Symbole = Symbole.Clone()
    };

    /// <summary>Crée une définition depuis un modèle importé, avec le symbole par défaut de son type et la correspondance automatique.</summary>
    public static DefinitionComposant Depuis(ModeleImporte m)
    {
        var d = new DefinitionComposant
        {
            Nom = m.Nom, Type = m.Type, Modele = m.Nom, TexteSpice = m.Texte, BrochesModele = m.Broches.ToList(), Parametres = m.Parametres,
            Description = ModelesSpice.Libelle(m.Type),
        };
        d.Symbole = GabaritsSymboles.PourType(m.Type, d.BrochesModele);
        d.RelierAutomatiquement();
        return d;
    }

    /// <summary>Relie par nom les broches du symbole encore libres (casse ignorée), puis dans l'ordre pour celles qui restent.</summary>
    public void RelierAutomatiquement()
    {
        var libres = BrochesModele.Where(n => !Symbole.Broches.Any(b => Egal(b.BrocheModele, n))).ToList();
        foreach (var b in Symbole.Broches.Where(b => b.BrocheModele == null || !BrochesModele.Any(n => Egal(n, b.BrocheModele))))
            b.BrocheModele = null;
        foreach (var b in Symbole.Broches.Where(b => b.BrocheModele == null).ToList())
        {
            var n = libres.FirstOrDefault();
            if (n == null) break;
            b.BrocheModele = n; libres.Remove(n);
        }
    }

    static bool Egal(string? a, string? b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Problèmes de correspondance : erreurs (broche du modèle non reliée ou reliée deux fois) et avertissements.</summary>
    public IReadOnlyList<ErcIssue> Verifier()
    {
        var l = new List<ErcIssue>();
        if (string.IsNullOrWhiteSpace(Modele)) l.Add(new(Severity.Error, "Nom de modèle manquant"));
        foreach (var n in BrochesModele)
        {
            int k = Symbole.Broches.Count(b => Egal(b.BrocheModele, n));
            if (k == 0) l.Add(new(Severity.Error, $"La broche « {n} » du modèle n'est reliée à aucune broche du symbole"));
            else if (k > 1) l.Add(new(Severity.Error, $"La broche « {n} » du modèle est reliée à {k} broches du symbole"));
        }
        foreach (var b in Symbole.Broches.Where(b => b.BrocheModele == null))
            l.Add(new(Severity.Warning, $"Broche du symbole en ({b.X}, {b.Y}) reliée à aucune broche du modèle : elle sera ignorée"));
        foreach (var g in Symbole.Broches.GroupBy(b => (b.X, b.Y)).Where(g => g.Count() > 1))
            l.Add(new(Severity.Error, $"Deux broches du symbole au même point ({g.Key.X}, {g.Key.Y})"));
        return l;
    }

    /// <summary>Indice (dans <see cref="Symbole"/>.Broches) de la broche du symbole reliée à chaque broche du modèle, dans l'ordre SPICE.</summary>
    public IReadOnlyList<int> OrdreSpice()
        => BrochesModele.Select(n => Symbole.Broches.FindIndex(b => Egal(b.BrocheModele, n))).ToList();

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string VersJson() => JsonSerializer.Serialize(this, Json);
    public static DefinitionComposant DepuisJson(string json) => JsonSerializer.Deserialize<DefinitionComposant>(json, Json) ?? throw new InvalidDataException("Définition de composant vide");
}

/// <summary>Bibliothèque de composants de l'utilisateur : un fichier .silxcomp (JSON) par définition, dans un dossier.</summary>
public sealed class BibliothequeComposants
{
    public const string Extension = ".silxcomp";
    public List<DefinitionComposant> Definitions { get; } = new();
    public string? Dossier { get; init; }

    /// <summary>Dossier par défaut : SILX_COMPOSANTS, sinon ~/Documents/Silx/Composants.</summary>
    public static string DossierParDefaut()
        => Environment.GetEnvironmentVariable("SILX_COMPOSANTS") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Silx", "Composants");

    public static BibliothequeComposants Charger(string dossier)
    {
        var b = new BibliothequeComposants { Dossier = dossier };
        if (!Directory.Exists(dossier)) return b;
        foreach (var f in Directory.GetFiles(dossier, "*" + Extension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            try { b.Definitions.Add(DefinitionComposant.DepuisJson(File.ReadAllText(f))); } catch (Exception) { /* fichier illisible : ignoré */ }
        return b;
    }

    /// <param name="ancienNom">Nom précédent si la définition a été renommée : son fichier est retiré.</param>
    public void Enregistrer(DefinitionComposant d, string? ancienNom = null)
    {
        var i = Definitions.FindIndex(x => ReferenceEquals(x, d));
        if (i < 0) i = Definitions.FindIndex(x => string.Equals(x.Nom, d.Nom, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) Definitions[i] = d; else Definitions.Add(d);
        if (Dossier == null) return;
        Directory.CreateDirectory(Dossier);
        if (ancienNom != null && !string.Equals(ancienNom, d.Nom, StringComparison.OrdinalIgnoreCase))
        {
            var f = Path.Combine(Dossier, NomFichier(ancienNom) + Extension);
            if (File.Exists(f)) File.Delete(f);
        }
        File.WriteAllText(Path.Combine(Dossier, NomFichier(d.Nom) + Extension), d.VersJson());
    }

    static string NomFichier(string nom) => string.Concat(nom.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch));
}
