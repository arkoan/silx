using System.Text;
using System.Text.RegularExpressions;
namespace Silx.Etude;

/// <summary>Nature d'un modèle importé : fixe la lettre SPICE de l'instance et l'ordre des broches.</summary>
public enum TypeModele { Diode, Npn, Pnp, Nmos, Pmos, Njf, Pjf, SousCircuit, Logique }

/// <summary>
/// Ce qu'on peut lire d'un modèle sans l'ouvrir : description (commentaires qui le précèdent), rôle de chaque broche
/// (commentaires « 20=DRAIN 10=GATE… »), composition interne et nature probable (ex. sous-circuit qui contient un NMOS
/// et des broches DRAIN/GATE/SOURCE → MOSFET canal N).
/// </summary>
public sealed record InfosModele(
    string Description,
    IReadOnlyDictionary<string, string> Libelles,
    string Composition,
    TypeModele? NatureProbable,
    IReadOnlyDictionary<string, string> Roles)
{
    public static InfosModele Vide { get; } = new("", new Dictionary<string, string>(), "", null, new Dictionary<string, string>());
}

/// <summary>Un .model ou un .subckt trouvé dans un fichier, avec son texte d'origine (recopié tel quel dans la netlist).</summary>
public sealed record ModeleImporte(string Nom, TypeModele Type, IReadOnlyList<string> Broches, string Texte, string Parametres = "", InfosModele? Infos = null, string? Code = null)
{
    public string Lettre => ModelesSpice.Lettre(Type);
}

/// <summary>Résultat de l'analyse d'un fichier de modèles.</summary>
public sealed record AnalyseSpice(IReadOnlyList<ModeleImporte> Modeles, IReadOnlyList<string> Avertissements);

public static class ModelesSpice
{
    public static string Lettre(TypeModele t) => t switch
    {
        TypeModele.Diode => "D", TypeModele.Npn or TypeModele.Pnp => "Q", TypeModele.Nmos or TypeModele.Pmos => "M",
        TypeModele.Njf or TypeModele.Pjf => "J", TypeModele.Logique => "A", _ => "X"
    };

    /// <summary>
    /// Modèles numériques XSPICE pris en charge : libellé et broches (entrées puis sorties). Les portes ont 2 entrées par
    /// défaut (A, B), ou le nombre lu à la fin du nom du modèle (« and3 » → 3). Mise à un / à zéro des bascules : inactives (NULL).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CodesLogiques = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["d_and"] = "Porte ET", ["d_nand"] = "Porte NON-ET", ["d_or"] = "Porte OU", ["d_nor"] = "Porte NON-OU",
        ["d_xor"] = "Porte OU exclusif", ["d_xnor"] = "Porte NON-OU exclusif", ["d_inverter"] = "Inverseur", ["d_buffer"] = "Tampon",
        ["d_dff"] = "Bascule D", ["d_jkff"] = "Bascule JK", ["d_tff"] = "Bascule T", ["d_srff"] = "Bascule SR",
        ["d_dlatch"] = "Verrou D", ["d_srlatch"] = "Verrou SR",
    };

    public static bool EstPorte(string code) => code.ToLowerInvariant() is "d_and" or "d_nand" or "d_or" or "d_nor" or "d_xor" or "d_xnor";
    public static bool EstSortieLogique(string broche) => broche.ToUpperInvariant() is "Y" or "Q" or "QB";

    /// <summary>Broches d'un modèle logique, entrées puis sorties.</summary>
    public static IReadOnlyList<string> BrochesLogiques(string code, int entrees = 2) => code.ToLowerInvariant() switch
    {
        "d_inverter" or "d_buffer" => ["A", "Y"],
        "d_dff" => ["D", "CLK", "Q", "QB"], "d_jkff" => ["J", "K", "CLK", "Q", "QB"], "d_tff" => ["T", "CLK", "Q", "QB"],
        "d_srff" => ["S", "R", "CLK", "Q", "QB"], "d_dlatch" => ["D", "EN", "Q", "QB"], "d_srlatch" => ["S", "R", "EN", "Q", "QB"],
        _ => [.. Enumerable.Range(0, Math.Clamp(entrees, 2, 8)).Select(i => ((char)('A' + i)).ToString()), "Y"],
    };

    /// <summary>
    /// Ordre des ports sur la ligne d'instance XSPICE : « [A B] » groupe les entrées d'une porte ; « NULL » laisse inactives
    /// la mise à un et la mise à zéro des bascules.
    /// </summary>
    public static IReadOnlyList<string> PortsXspice(string code, IReadOnlyList<string> broches) => code.ToLowerInvariant() switch
    {
        "d_inverter" or "d_buffer" => ["A", "Y"],
        "d_dff" => ["D", "CLK", "NULL", "NULL", "Q", "QB"], "d_jkff" => ["J", "K", "CLK", "NULL", "NULL", "Q", "QB"],
        "d_tff" => ["T", "CLK", "NULL", "NULL", "Q", "QB"], "d_srff" => ["S", "R", "CLK", "NULL", "NULL", "Q", "QB"],
        "d_dlatch" => ["D", "EN", "NULL", "NULL", "Q", "QB"], "d_srlatch" => ["S", "R", "EN", "NULL", "NULL", "Q", "QB"],
        _ => ["[" + string.Join(' ', broches.Where(b => !EstSortieLogique(b))) + "]", "Y"],
    };

    /// <summary>Broches dans l'ordre de la ligne d'instance SPICE.</summary>
    public static IReadOnlyList<string> BrochesStandard(TypeModele t) => t switch
    {
        TypeModele.Diode => ["A", "K"],
        TypeModele.Npn or TypeModele.Pnp => ["C", "B", "E"],
        TypeModele.Nmos or TypeModele.Pmos => ["D", "G", "S", "B"],
        TypeModele.Njf or TypeModele.Pjf => ["D", "G", "S"],
        _ => [],
    };

    public static string Libelle(TypeModele t) => t switch
    {
        TypeModele.Diode => "Diode", TypeModele.Npn => "Transistor NPN", TypeModele.Pnp => "Transistor PNP",
        TypeModele.Nmos => "MOSFET canal N", TypeModele.Pmos => "MOSFET canal P", TypeModele.Njf => "JFET canal N",
        TypeModele.Pjf => "JFET canal P", TypeModele.Logique => "Logique (XSPICE)", _ => "Sous-circuit"
    };

    static readonly Dictionary<string, TypeModele> TypesModel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["D"] = TypeModele.Diode, ["NPN"] = TypeModele.Npn, ["PNP"] = TypeModele.Pnp,
        ["NMOS"] = TypeModele.Nmos, ["PMOS"] = TypeModele.Pmos, ["NJF"] = TypeModele.Njf, ["PJF"] = TypeModele.Pjf,
    };

    /// <summary>
    /// Analyse un texte de bibliothèque SPICE (.lib, .mod, .cir…) : lignes de continuation « + », commentaires « * » et « ; »,
    /// .model (diodes, bipolaires, MOS, JFET) et .subckt … .ends (sous-circuits imbriqués tolérés).
    /// </summary>
    public static AnalyseSpice Analyser(string texte)
    {
        var modeles = new List<ModeleImporte>();
        var avert = new List<string>();
        // Lignes logiques : (texte joint pour l'analyse, lignes physiques d'origine pour la recopie).
        var logiques = new List<(string Joint, List<string> Brut)>();
        foreach (var raw in texte.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.TrimEnd();
            var s = t.TrimStart();
            if (s.StartsWith('+') && logiques.Count > 0)
            {
                logiques[^1] = (logiques[^1].Joint + " " + SansCommentaire(s[1..]), logiques[^1].Brut);
                logiques[^1].Brut.Add(t);
            }
            else logiques.Add((s.StartsWith('*') ? "" : SansCommentaire(s), [t]));
        }

        for (int i = 0; i < logiques.Count; i++)
        {
            var l = logiques[i].Joint.Trim();
            if (l.StartsWith(".model", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(l, @"^\.model\s+(\S+)\s+([A-Za-z_]+)", RegexOptions.IgnoreCase);
                if (!m.Success) { avert.Add($"Ligne .model illisible : {Court(l)}"); continue; }
                var nom = m.Groups[1].Value; var type = m.Groups[2].Value;
                if (TypesModel.TryGetValue(type, out var tm))
                    modeles.Add(new(nom, tm, BrochesStandard(tm), string.Join('\n', logiques[i].Brut)));
                else if (CodesLogiques.ContainsKey(type))
                {
                    // Porte ou bascule XSPICE : nombre d'entrées d'une porte lu à la fin du nom (« and3 »), 2 sinon.
                    var n = Regex.Match(nom, @"(\d)$") is { Success: true } mn ? int.Parse(mn.Value) : 2;
                    modeles.Add(new(nom, TypeModele.Logique, BrochesLogiques(type, n is >= 2 and <= 8 ? n : 2), string.Join('\n', logiques[i].Brut), Code: type.ToLowerInvariant()));
                }
                else if (type.Equals("adc_bridge", StringComparison.OrdinalIgnoreCase) || type.Equals("dac_bridge", StringComparison.OrdinalIgnoreCase))
                    avert.Add($"Pont « {nom} » ({type}) ignoré : Silx place lui-même les ponts analogique ↔ numérique autour des portes.");
                else avert.Add($"Modèle « {nom} » de type {type} : pas encore pris en charge (seuls D, NPN, PNP, NMOS, PMOS, NJF, PJF, les portes et bascules XSPICE d_… et .subckt le sont).");
            }
            else if (l.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase))
            {
                var mots = l.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (mots.Length < 2) { avert.Add($"Ligne .subckt illisible : {Court(l)}"); continue; }
                var nom = mots[1];
                var broches = new List<string>(); var param = new StringBuilder();
                bool dansParams = false;
                foreach (var w in mots.Skip(2))
                {
                    if (w.Equals("params:", StringComparison.OrdinalIgnoreCase) || w.StartsWith("params:", StringComparison.OrdinalIgnoreCase)) { dansParams = true; var reste = w[7..]; if (reste.Length > 0) param.Append(reste).Append(' '); continue; }
                    if (dansParams || w.Contains('=')) { dansParams = true; param.Append(w).Append(' '); continue; }
                    broches.Add(w);
                }
                var corps = new List<string>();
                int prof = 0; int j = i;
                for (; j < logiques.Count; j++)
                {
                    corps.AddRange(logiques[j].Brut);
                    var lj = logiques[j].Joint.Trim();
                    if (lj.StartsWith(".subckt", StringComparison.OrdinalIgnoreCase)) prof++;
                    else if (lj.StartsWith(".ends", StringComparison.OrdinalIgnoreCase) && --prof == 0) break;
                }
                if (prof != 0) { avert.Add($"Sous-circuit « {nom} » sans .ends : ignoré."); continue; }
                modeles.Add(new(nom, TypeModele.SousCircuit, broches, string.Join('\n', corps), param.ToString().Trim(),
                    InfosSousCircuit(logiques.Select(x => x.Brut).ToList(), i, j, broches)));
                i = j;
            }
        }
        foreach (var g in modeles.GroupBy(m => m.Nom, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            avert.Add($"« {g.Key} » est défini {g.Count()} fois : seule la première définition sera utilisée par ngspice.");
        if (modeles.Count == 0 && avert.Count == 0) avert.Add("Aucun .model ni .subckt trouvé.");
        return new(modeles, avert);
    }

    /// <summary>Rôle standard d'une broche d'après son libellé ou son nom : D, G, S, B (MOS), C, B, E (bipolaire), A, K (diode).</summary>
    public static string? Role(string libelle) => libelle.Trim().ToUpperInvariant() switch
    {
        "DRAIN" or "D" => "D", "GATE" or "G" or "GRILLE" => "G", "SOURCE" or "S" => "S",
        "BULK" or "BODY" or "SUB" or "SUBSTRATE" or "SUBSTRAT" => "B",
        "COLLECTOR" or "COLLECTEUR" or "C" => "C", "BASE" => "B", "EMITTER" or "EMETTEUR" or "ÉMETTEUR" or "E" => "E",
        "ANODE" or "A" => "A", "CATHODE" or "K" => "K",
        _ => null
    };

    static InfosModele InfosSousCircuit(List<List<string>> lignes, int debut, int fin, List<string> broches)
    {
        static string Nettoie(string l) => l.TrimStart('*', ';', ' ', '\t').Trim().Trim('-', '=', '*').Trim();
        // Description : commentaires juste avant .subckt (en remontant jusqu'à une ligne non commentaire, 6 au plus).
        var desc = new List<string>();
        for (int k = debut - 1; k >= 0 && desc.Count < 6; k--)
        {
            var l = string.Join(" ", lignes[k]).Trim();
            if (l.Length == 0) { if (desc.Count > 0) break; continue; }
            if (!l.StartsWith('*') && !l.StartsWith(';')) break;
            var n = Nettoie(l); if (n.Length > 0) desc.Insert(0, n);
        }
        // Libellés des broches : « 20=DRAIN », « 20 : DRAIN » dans les commentaires autour de la ligne .subckt.
        var lib = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var autour = Enumerable.Range(Math.Max(0, debut - 6), Math.Min(lignes.Count, debut + 6) - Math.Max(0, debut - 6))
            .SelectMany(k => lignes[k]).Where(l => l.TrimStart().StartsWith('*') || l.TrimStart().StartsWith(';'));
        foreach (var l in autour)
            foreach (Match m in Regex.Matches(l, @"(?<![\w.])([A-Za-z0-9_+\-]+)\s*[=:]\s*([A-Za-z][A-Za-z0-9_+\-/]*)"))
            {
                var p = m.Groups[1].Value;
                if (broches.Contains(p, StringComparer.OrdinalIgnoreCase) && !lib.ContainsKey(p)) lib[p] = m.Groups[2].Value;
            }
        // Composition interne.
        var corps = lignes.Skip(debut + 1).Take(fin - debut - 1).Select(b => string.Join(" ", b).Trim()).Where(l => l.Length > 0 && !l.StartsWith('*')).ToList();
        var types = corps.Where(l => l.StartsWith(".model", StringComparison.OrdinalIgnoreCase))
            .Select(l => Regex.Match(l, @"^\.model\s+\S+\s+([A-Za-z]+)", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant()).Where(t => t.Length > 0).ToList();
        var lettres = corps.Where(l => !l.StartsWith('.') && !l.StartsWith('+')).Select(l => char.ToUpperInvariant(l[0])).GroupBy(c => c).OrderByDescending(g => g.Count()).ToList();
        string NomElt(char c) => c switch { 'M' => "MOS", 'D' => "diode", 'Q' => "bipolaire", 'R' => "R", 'C' => "C", 'L' => "L", 'E' or 'F' or 'G' or 'H' or 'B' => "source commandée", 'V' => "source V", 'I' => "source I", 'X' => "sous-circuit", 'J' => "JFET", _ => c.ToString() };
        var compo = string.Join(", ", lettres.GroupBy(g => NomElt(g.Key)).Select(g => $"{g.Sum(x => x.Count())} {g.Key}"));
        if (types.Count > 0) compo += " · modèles internes : " + string.Join(", ", types.GroupBy(t => t).Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key));
        // Rôles et nature probable.
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in broches) if (Role(lib.GetValueOrDefault(b, b)) is { } r && !roles.ContainsValue(r)) roles[b] = r;
        var rs = roles.Values.ToHashSet();
        TypeModele? nature = null;
        if (rs.IsSupersetOf(["D", "G", "S"]))
            nature = types.Contains("PMOS") && !types.Contains("NMOS") ? TypeModele.Pmos : types.Contains("NMOS") || desc.Any(d => Regex.IsMatch(d, @"N[- ]?CH", RegexOptions.IgnoreCase)) ? TypeModele.Nmos
                   : desc.Any(d => Regex.IsMatch(d, @"P[- ]?CH", RegexOptions.IgnoreCase)) ? TypeModele.Pmos : null;
        else if (rs.IsSupersetOf(["C", "B", "E"])) nature = types.Contains("PNP") ? TypeModele.Pnp : TypeModele.Npn;
        else if (rs.IsSupersetOf(["A", "K"]) && broches.Count == 2) nature = TypeModele.Diode;
        if (nature == null) roles.Clear();
        // Sans libellés : MOSFET discret décrit comme tel (N-Ch, P-Ch, MOSFET…) et contenant un NMOS/PMOS, 3 ou 4 broches
        // → ordre usuel des fabricants D, G, S (, B). HYPOTHÈSE affichée à l'utilisateur, à vérifier sur la documentation.
        string txt = string.Join(" ", desc);
        if (nature == null && lib.Count == 0 && broches.Count is 3 or 4 && (types.Contains("NMOS") || types.Contains("PMOS"))
            && Regex.IsMatch(txt, @"MOSFET|DMOS|[NP][- ]?CH", RegexOptions.IgnoreCase))
        {
            nature = Regex.IsMatch(txt, @"P[- ]?CH", RegexOptions.IgnoreCase) || (types.Contains("PMOS") && !types.Contains("NMOS")) ? TypeModele.Pmos : TypeModele.Nmos;
            string[] ordre = ["D", "G", "S", "B"];
            for (int k = 0; k < broches.Count; k++) roles[broches[k]] = ordre[k];
            compo += " · broches supposées dans l'ordre usuel D, G, S" + (broches.Count == 4 ? ", B" : "") + " (aucun libellé dans le fichier) : à vérifier";
        }
        return new InfosModele(txt, lib, compo, nature, roles);
    }

    static string SansCommentaire(string s)
    {
        // « ; » et « $ » ouvrent un commentaire de fin de ligne (ngspice) ; on ne touche pas aux expressions entre accolades.
        var m = Regex.Match(s, @";|(^|\s)\$");
        return m.Success ? s[..m.Index] : s;
    }

    static string Court(string s) => s.Length > 60 ? s[..60] + "…" : s;
}
