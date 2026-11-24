using System.Text;
using System.Text.RegularExpressions;
namespace Silx.Etude;

/// <summary>Nature d'un modèle importé : fixe la lettre SPICE de l'instance et l'ordre des broches.</summary>
public enum TypeModele { Diode, Npn, Pnp, Nmos, Pmos, Njf, Pjf, SousCircuit }

/// <summary>Un .model ou un .subckt trouvé dans un fichier, avec son texte d'origine (recopié tel quel dans la netlist).</summary>
public sealed record ModeleImporte(string Nom, TypeModele Type, IReadOnlyList<string> Broches, string Texte, string Parametres = "")
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
        TypeModele.Njf or TypeModele.Pjf => "J", _ => "X"
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
        TypeModele.Pjf => "JFET canal P", _ => "Sous-circuit"
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
                else avert.Add($"Modèle « {nom} » de type {type} : pas encore pris en charge (seuls D, NPN, PNP, NMOS, PMOS, NJF, PJF et .subckt le sont).");
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
                modeles.Add(new(nom, TypeModele.SousCircuit, broches, string.Join('\n', corps), param.ToString().Trim()));
                i = j;
            }
        }
        foreach (var g in modeles.GroupBy(m => m.Nom, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            avert.Add($"« {g.Key} » est défini {g.Count()} fois : seule la première définition sera utilisée par ngspice.");
        if (modeles.Count == 0 && avert.Count == 0) avert.Add("Aucun .model ni .subckt trouvé.");
        return new(modeles, avert);
    }

    static string SansCommentaire(string s)
    {
        // « ; » et « $ » ouvrent un commentaire de fin de ligne (ngspice) ; on ne touche pas aux expressions entre accolades.
        var m = Regex.Match(s, @";|(^|\s)\$");
        return m.Success ? s[..m.Index] : s;
    }

    static string Court(string s) => s.Length > 60 ? s[..60] + "…" : s;
}
