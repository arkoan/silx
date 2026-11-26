using System.Text.Json;
using System.Text.Json.Serialization;
namespace Silx.Etude;

/// <summary>
/// Projet Silx : tout ce qu'il faut pour rouvrir et resimuler une étude — circuit sous test, bancs de test (calques,
/// réglages d'analyse, critères, gabarits dessinés, graphiques) et définitions des composants SPICE importés
/// (texte du modèle et symbole recopiés : le projet ne dépend pas de la bibliothèque de l'utilisateur).
/// Fichier JSON, extension .silx. Les résultats de simulation ne sont pas enregistrés : on resimule.
/// </summary>
public static class Projet
{
    public const string Extension = ".silx";
    /// <summary>2 : dipôles sur 3 pas (4 avant) ; un projet plus ancien reçoit des fils de raccord à l'ouverture.</summary>
    public const int VersionFormat = 2;

    // ── format de fichier (objets simples, indépendants des classes de travail) ──
    public sealed class Fichier
    {
        public int Version { get; set; } = VersionFormat;
        public string Application { get; set; } = "Silx";
        public List<DefinitionComposant> Composants { get; set; } = new();
        public SchemaF Circuit { get; set; } = new();
        public List<BancF> Bancs { get; set; } = new();
    }
    public sealed class SchemaF { public List<ComposantF> Composants { get; set; } = new(); public List<int[]> Fils { get; set; } = new(); }
    public sealed class ComposantF
    {
        public string Ref { get; set; } = ""; public ComponentKind Type { get; set; } public int X { get; set; } public int Y { get; set; }
        public int Rotation { get; set; } public bool Miroir { get; set; } public string Valeur { get; set; } = ""; public string? Composant { get; set; }
        public double? Tolerance { get; set; }
    }
    public sealed class BancF
    {
        public string Nom { get; set; } = ""; public AnalyseKind TypeParDefaut { get; set; }
        public string? Couleur { get; set; }
        public Campagne? Campagne { get; set; }
        public SchemaF Calque { get; set; } = new();
        public double Pas { get; set; } public double Duree { get; set; }
        public double FDebut { get; set; } public double FFin { get; set; } public int PointsParDecade { get; set; }
        public List<CritereGabarit> Criteres { get; set; } = new();
        public List<TraceF> GabaritsDessines { get; set; } = new();
        public List<List<string>> Graphiques { get; set; } = new();
    }
    public sealed class TraceF { public AnalyseKind Analyse { get; set; } public bool Relatif { get; set; } public double Ancrage { get; set; } public string Noeud { get; set; } = ""; public Silx.Mesures.Gabarits.Limite Limite { get; set; } public List<double[]> Points { get; set; } = new(); }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    // ── étude → fichier ──
    public static string VersJson(Etude e)
    {
        var defs = e.Schema.Components.Concat(e.Bancs.SelectMany(b => b.Calque.Components))
            .Where(k => k.Kind == ComponentKind.Spice && k.Definition != null).Select(k => k.Definition!).Distinct().ToList();
        // Deux définitions de même nom (ex. après renommage) : on garde des noms uniques dans le fichier.
        var noms = new Dictionary<DefinitionComposant, string>();
        foreach (var d in defs)
        {
            var n = d.Nom; int i = 2;
            while (noms.ContainsValue(n)) n = $"{d.Nom} ({i++})";
            noms[d] = n;
        }
        SchemaF S(Schematic s) => new()
        {
            Composants = s.Components.Select(k => new ComposantF
            {
                Ref = k.Ref, Type = k.Kind, X = k.Position.X, Y = k.Position.Y, Rotation = k.Rotation, Miroir = k.Miroir, Valeur = k.Value, Tolerance = k.Tolerance,
                Composant = k.Definition != null ? noms[k.Definition] : null
            }).ToList(),
            Fils = s.Wires.Select(w => new[] { w.A.X, w.A.Y, w.B.X, w.B.Y }).ToList(),
        };
        var f = new Fichier
        {
            Composants = defs.Select(d => { var c = d.Clone(); c.Nom = noms[d]; return c; }).ToList(),
            Circuit = S(e.Schema),
            Bancs = e.Bancs.Select(b => new BancF
            {
                Nom = b.Nom, TypeParDefaut = b.TypeParDefaut, Couleur = b.Couleur, Campagne = b.Campagne.Mode == ModeCampagne.Aucune ? null : b.Campagne, Calque = S(b.Calque),
                Pas = b.Transitoire.Pas, Duree = b.Transitoire.Duree, FDebut = b.Ac.FDebut, FFin = b.Ac.FFin, PointsParDecade = b.Ac.PointsParDecade,
                Criteres = b.Gabarits.ToList(),
                GabaritsDessines = b.GabaritsTraces.Select(g => new TraceF { Analyse = g.Analyse, Relatif = g.Relatif, Ancrage = g.Ancrage, Noeud = g.Noeud, Limite = g.Limite, Points = g.Points.Select(p => new[] { p.X, p.Y }).ToList() }).ToList(),
                Graphiques = b.Graphiques.Select(g => g.ToList()).ToList(),
            }).ToList(),
        };
        return JsonSerializer.Serialize(f, Json);
    }

    // ── fichier → étude ──
    public static Etude DepuisJson(string json)
    {
        var f = JsonSerializer.Deserialize<Fichier>(json, Json) ?? throw new InvalidDataException("Projet vide.");
        if (f.Version > VersionFormat) throw new InvalidDataException($"Projet enregistré par une version plus récente de Silx (format {f.Version}).");
        var defs = f.Composants.GroupBy(d => d.Nom).ToDictionary(g => g.Key, g => g.First());
        Schematic S(SchemaF s)
        {
            var r = new Schematic();
            foreach (var c in s.Composants)
            {
                DefinitionComposant? d = null;
                if (c.Composant != null && !defs.TryGetValue(c.Composant, out d))
                    throw new InvalidDataException($"{c.Ref} : composant « {c.Composant} » absent du projet.");
                r.Components.Add(new PlacedComponent { Ref = c.Ref, Kind = c.Type, Position = new(c.X, c.Y), Rotation = c.Rotation, Miroir = c.Miroir, Value = c.Valeur, Definition = d, Tolerance = c.Tolerance });
            }
            foreach (var w in s.Fils.Where(w => w.Length == 4)) r.Wires.Add(new(new(w[0], w[1]), new(w[2], w[3])));
            if (f.Version < 2) r.RaccorderDipoles(4);
            return r;
        }
        var e = new Etude { Schema = S(f.Circuit) };
        foreach (var bf in f.Bancs)
        {
            var b = new Banc { Nom = bf.Nom, Calque = S(bf.Calque), TypeParDefaut = bf.TypeParDefaut, Couleur = bf.Couleur, Campagne = bf.Campagne ?? new() };
            b.Transitoire.Pas = bf.Pas; b.Transitoire.Duree = bf.Duree;
            b.Ac.FDebut = bf.FDebut; b.Ac.FFin = bf.FFin; b.Ac.PointsParDecade = bf.PointsParDecade;
            b.Gabarits.AddRange(bf.Criteres);
            b.GabaritsTraces.AddRange(bf.GabaritsDessines.Select(t => new GabaritTrace { Analyse = t.Analyse, Relatif = t.Relatif, Ancrage = t.Ancrage, Noeud = t.Noeud, Limite = t.Limite, Points = t.Points.Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToList() }));
            b.Graphiques.AddRange(bf.Graphiques);
            e.Bancs.Add(b);
        }
        return e;
    }

    public static void Enregistrer(Etude e, string chemin)
    {
        // Écriture atomique : fichier temporaire puis remplacement, pour ne jamais laisser un projet à moitié écrit.
        var tmp = chemin + ".tmp";
        File.WriteAllText(tmp, VersJson(e));
        File.Move(tmp, chemin, overwrite: true);
    }

    public static Etude Ouvrir(string chemin) => DepuisJson(File.ReadAllText(chemin));
}
