using System.Text.Json;
using System.Text.Json.Serialization;
namespace Silx.Etude;

public enum FormePrimitive { Trait, Rectangle, Cercle, Polygone, Fleche, Texte, Arc }

/// <summary>
/// Élément graphique d'un symbole, en pas de grille (réels), dans le repère du symbole (origine = point d'accroche).
/// Trait/Rectangle/Flèche : (X1,Y1)→(X2,Y2) ; Cercle : centre (X1,Y1), rayon X2 ; Polygone : Points ; Texte : (X1,Y1) ;
/// Arc : demi-cercle de diamètre (X1,Y1)→(X2,Y2), bombé à gauche du sens de tracé.
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
    public static Primitive ArcDe(double x1, double y1, double x2, double y2) => new() { Forme = FormePrimitive.Arc, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };

    /// <summary>Points d'un arc (demi-cercle), pour le dessin et l'encombrement.</summary>
    public IEnumerable<(double X, double Y)> PointsArc(int n = 20)
    {
        double cx = (X1 + X2) / 2, cy = (Y1 + Y2) / 2, r = Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1)) / 2;
        double a0 = Math.Atan2(Y1 - cy, X1 - cx);
        for (int i = 0; i <= n; i++) { var a = a0 - Math.PI * i / n; yield return (cx + r * Math.Cos(a), cy + r * Math.Sin(a)); }
    }

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
    /// <summary>Rôle de la broche dans un gabarit de dessin (D, G, S, B, C, E, A, K) ; null pour une boîte.</summary>
    public string? Role { get; set; }
    /// <summary>Nom affiché sur le symbole, choisi par l'utilisateur (ex. « IN+ ») ; null = nom de la broche du modèle.</summary>
    public string? Nom { get; set; }
    public BrocheSymbole Clone() => new() { X = X, Y = Y, BrocheModele = BrocheModele, Role = Role, Nom = Nom };
}

/// <summary>Gabarit de dessin d'un symbole.</summary>
/// <summary>Gabarit de dessin d'un symbole. Nmos/Pmos : 4 broches (substrat B) ; Nmos3/Pmos3 : substrat relié à la source.</summary>
public enum GabaritSymbole { Boite, Diode, Npn, Pnp, Nmos, Pmos, Njf, Pjf, Nmos3, Pmos3, Personnalise, Logique, Quartz }

public sealed class Symbole
{
    public GabaritSymbole Gabarit { get; set; } = GabaritSymbole.Boite;
    /// <summary>Version du dessin : 2 = grille fine (transistors à 2 pas) ; 3 = transistors affinés, diode sur 3 pas ; 4 = MOSFET compact (1 case de large). Absente des fichiers anciens (0).</summary>
    public int Version { get; set; }
    public const int VersionCourante = 4;
    /// <summary>MOS 3 broches : dessiner la diode de corps entre drain et source.</summary>
    public bool DiodeCorps { get; set; }
    /// <summary>Corps rectangulaire (gabarit Boîte) : les broches hors du corps y sont reliées par un trait, et leur nom est écrit à l'intérieur.</summary>
    public double[]? Corps { get; set; }
    public List<Primitive> Traits { get; set; } = new();
    public List<BrocheSymbole> Broches { get; set; } = new();
    public Symbole Clone() => new() { Gabarit = Gabarit, Version = Version, DiodeCorps = DiodeCorps, Corps = Corps?.ToArray(), Traits = Traits.Select(t => t.Clone()).ToList(), Broches = Broches.Select(b => b.Clone()).ToList() };

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
                case FormePrimitive.Arc: foreach (var (x, y) in t.PointsArc(8)) { xs.Add(x); ys.Add(y); } break;
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
    /// <summary>Libellé lu dans le fichier pour chaque broche du modèle (ex. « 20 » → « DRAIN »).</summary>
    public Dictionary<string, string> Libelles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Résumé lu dans le fichier : commentaires d'en-tête, composition interne.</summary>
    public string Notes { get; set; } = "";
    /// <summary>
    /// Broches du modèle absentes du symbole, raccordées d'office : « 0 » (masse) ou « DC v » (tension fixe, ex. broche
    /// de température VTEMP = 25 pour 25 °C). Chaque instance reçoit sa propre source.
    /// </summary>
    public Dictionary<string, string> BrochesCachees { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Symbole Symbole { get; set; } = new();
    /// <summary>Composant logique : modèle de code XSPICE (d_and, d_dff…). Null pour un composant analogique.</summary>
    public string? CodeXspice { get; set; }
    /// <summary>Composant logique : tension d'alimentation de la famille (seuils à 30 % et 70 %, sorties 0 / Vcc).</summary>
    public double TensionLogique { get; set; } = 5;

    /// <summary>Nom d'une broche du symbole : celui donné par l'utilisateur, sinon le libellé du fichier, sinon la broche du modèle.</summary>
    public string NomBroche(BrocheSymbole b) => !string.IsNullOrWhiteSpace(b.Nom) ? b.Nom.Trim()
        : b.BrocheModele is { } m ? (Libelles.TryGetValue(m, out var l) ? l : m) : "?";

    /// <summary>Nom affiché d'une broche du modèle : « 20 (DRAIN) » si un libellé est connu.</summary>
    public string NomAffiche(string broche) => Libelles.TryGetValue(broche, out var l) && !string.Equals(l, broche, StringComparison.OrdinalIgnoreCase) ? $"{broche} ({l})" : broche;

    [JsonIgnore] public string Lettre => ModelesSpice.Lettre(Type);

    public DefinitionComposant Clone() => new()
    {
        Nom = Nom, Type = Type, Modele = Modele, TexteSpice = TexteSpice, BrochesModele = BrochesModele.ToList(),
        Parametres = Parametres, Description = Description, Symbole = Symbole.Clone(),
        Libelles = new(Libelles, StringComparer.OrdinalIgnoreCase), Notes = Notes, BrochesCachees = new(BrochesCachees, StringComparer.OrdinalIgnoreCase),
        CodeXspice = CodeXspice, TensionLogique = TensionLogique
    };

    /// <summary>Crée une définition depuis un modèle importé, avec le symbole par défaut de son type et la correspondance automatique.</summary>
    public static DefinitionComposant Depuis(ModeleImporte m)
    {
        var d = new DefinitionComposant
        {
            Nom = m.Nom, Type = m.Type, Modele = m.Nom, TexteSpice = m.Texte, BrochesModele = m.Broches.ToList(), Parametres = m.Parametres,
            Description = ModelesSpice.Libelle(m.Type),
        };
        if (m.Type == TypeModele.Logique && m.Code is { } code)
        {
            d.CodeXspice = code; d.Description = ModelesSpice.CodesLogiques[code] + $" (XSPICE {code})";
            d.Notes = "Composant numérique : Silx place un pont analogique → numérique sur chaque entrée et numérique → analogique sur chaque sortie (seuils 30 % / 70 % de la tension logique, sortie 0 / Vcc derrière 50 Ω).";
            d.Symbole = GabaritsSymboles.Logique(code, d.BrochesModele);
            return d;
        }
        var infos = m.Infos ?? InfosModele.Vide;
        foreach (var (k, v) in infos.Libelles) d.Libelles[k] = v;
        d.Notes = string.Join(" — ", new[] { infos.Description, infos.Composition }.Where(x => x.Length > 0));
        if (m.Type == TypeModele.SousCircuit && infos.NatureProbable is { } nat)
        {
            // Sous-circuit reconnu (ex. MOSFET fabricant) : symbole du composant, broches reliées par rôle.
            d.Description = ModelesSpice.Libelle(nat) + " (sous-circuit)";
            var g = GabaritsSymboles.PourType(nat);
            bool sansSubstrat = nat is TypeModele.Nmos or TypeModele.Pmos && !infos.Roles.Values.Contains("B");
            if (sansSubstrat) g = nat == TypeModele.Nmos ? GabaritSymbole.Nmos3 : GabaritSymbole.Pmos3;
            // Diode de corps dessinée si le modèle en contient une (élément ou modèle nommé « BODY »), sinon s'il contient des diodes.
            bool diode = sansSubstrat && (System.Text.RegularExpressions.Regex.IsMatch(m.Texte, @"^\s*D\w*BODY|\.model\s+\w*BODY", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline)
                                          || infos.Composition.Contains("diode"));
            d.AppliquerGabarit(g, diode, infos.Roles);
            foreach (var b in d.BrochesModele.Where(b => !infos.Roles.ContainsKey(b)))
                if (d.Libelles.TryGetValue(b, out var l) && l.Contains("TEMP", StringComparison.OrdinalIgnoreCase))
                    d.BrochesCachees[b] = "DC 25";   // HYPOTHÈSE : broche thermique = température en °C (25 = ambiante)
            return d;
        }
        d.Symbole = GabaritsSymboles.PourType(m.Type, d.BrochesModele);
        d.RelierAutomatiquement();
        return d;
    }

    /// <summary>Rôle connu de chaque broche du modèle : rôle déjà porté par le symbole, libellé du fichier, ou nom de la broche.</summary>
    public Dictionary<string, string> RolesConnus()
    {
        var r = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in Symbole.Broches.Where(b => b.Role != null && b.BrocheModele != null)) r.TryAdd(b.Role!, b.BrocheModele!);
        foreach (var p in BrochesModele)
            if (ModelesSpice.Role(Libelles.GetValueOrDefault(p, p)) is { } role) r.TryAdd(role, p);
        return r;
    }

    /// <summary>
    /// Remplace le symbole par un gabarit, en reliant chaque broche du gabarit à la broche du modèle de même rôle.
    /// Une broche du modèle qui revient sur le symbole n'est plus raccordée d'office.
    /// </summary>
    public void AppliquerGabarit(GabaritSymbole g, bool diodeCorps = false, IReadOnlyDictionary<string, string>? rolesParBroche = null)
    {
        if (g == GabaritSymbole.Logique)
        {
            Symbole = CodeXspice is { } code ? GabaritsSymboles.Logique(code, BrochesModele) : GabaritsSymboles.Boite(BrochesModele);
            if (CodeXspice == null) RelierAutomatiquement();
            return;
        }
        if (g == GabaritSymbole.Personnalise)
        {
            // Dessin libre : on part du symbole actuel, dont le corps de boîte devient des éléments modifiables.
            var s = Symbole.Clone(); s.Gabarit = g;
            if (s.Corps is { } c)
            {
                s.Traits.Add(Primitive.Rect(c[0], c[1], c[2], c[3]));
                foreach (var b in s.Broches)
                {
                    double ex = Math.Clamp(b.X, c[0], c[2]), ey = Math.Clamp(b.Y, c[1], c[3]);
                    if (ex != b.X || ey != b.Y) s.Traits.Add(Primitive.Trait(b.X, b.Y, ex, ey));
                }
                s.Corps = null;
            }
            Symbole = s; return;
        }
        var parRole = RolesConnus();
        if (rolesParBroche != null) foreach (var (p, role) in rolesParBroche) parRole[role] = p;
        var visibles = BrochesModele.Where(n => !BrochesCachees.ContainsKey(n)).ToList();
        Symbole = g == GabaritSymbole.Boite ? GabaritsSymboles.Boite(visibles) : GabaritsSymboles.Dessin(g, diodeCorps);
        if (g != GabaritSymbole.Boite)
            foreach (var b in Symbole.Broches)
                b.BrocheModele = b.Role != null && parRole.TryGetValue(b.Role, out var p) ? p : BrochesModele.FirstOrDefault(n => string.Equals(n, b.Role, StringComparison.OrdinalIgnoreCase));
        else RelierAutomatiquement();
        foreach (var b in Symbole.Broches.Where(b => b.BrocheModele != null)) BrochesCachees.Remove(b.BrocheModele!);
    }

    /// <summary>Relie par nom les broches du symbole encore libres (casse ignorée), puis dans l'ordre pour celles qui restent.</summary>
    public void RelierAutomatiquement()
    {
        var libres = BrochesModele.Where(n => !BrochesCachees.ContainsKey(n) && !Symbole.Broches.Any(b => Egal(b.BrocheModele, n))).ToList();
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
            bool cachee = BrochesCachees.TryGetValue(n, out var cx);
            if (cachee && k > 0) l.Add(new(Severity.Error, $"La broche « {NomAffiche(n)} » est à la fois sur le symbole et raccordée d'office"));
            else if (cachee && !ConnexionValide(cx!)) l.Add(new(Severity.Error, $"Raccordement de « {NomAffiche(n)} » illisible : « {cx} » (attendu 0 ou DC valeur)"));
            else if (k == 0 && !cachee) l.Add(new(Severity.Error, $"La broche « {NomAffiche(n)} » du modèle n'est reliée à aucune broche du symbole"));
            else if (k > 1) l.Add(new(Severity.Error, $"La broche « {NomAffiche(n)} » du modèle est reliée à {k} broches du symbole"));
        }
        foreach (var b in Symbole.Broches.Where(b => b.BrocheModele == null))
            l.Add(new(Severity.Warning, $"Broche du symbole en ({b.X}, {b.Y}) reliée à aucune broche du modèle : elle sera ignorée"));
        foreach (var g in Symbole.Broches.GroupBy(b => (b.X, b.Y)).Where(g => g.Count() > 1))
            l.Add(new(Severity.Error, $"Deux broches du symbole au même point ({g.Key.X}, {g.Key.Y})"));
        return l;
    }

    public static bool ConnexionValide(string c) => c.Trim() == "0" || (c.Trim().StartsWith("DC ", StringComparison.OrdinalIgnoreCase) && SpiceNumber.TryParse(c.Trim()[3..], out _));

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
        // Composants intégrés (bipolaires, JFET, MOSFET, quartz, portes et bascules XSPICE) : toujours présents ; un fichier de même nom les remplace.
        var b = new BibliothequeComposants { Dossier = dossier };
        b.Definitions.AddRange(BibliothequeBase.Definitions());
        b.Definitions.AddRange(BibliothequeLogique.Definitions());
        if (!Directory.Exists(dossier)) return b;
        foreach (var f in Directory.GetFiles(dossier, "*" + Extension).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            try
            {
                var d = DefinitionComposant.DepuisJson(File.ReadAllText(f));
                b.Definitions.RemoveAll(x => string.Equals(x.Nom, d.Nom, StringComparison.OrdinalIgnoreCase));   // remplace un composant logique intégré
                b.Definitions.Add(d);
                if (GabaritsSymboles.MettreAJour(d.Symbole)) File.WriteAllText(f, d.VersJson());   // gabarits redessinés sur la grille fine
            }
            catch (Exception) { /* fichier illisible : ignoré */ }
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
