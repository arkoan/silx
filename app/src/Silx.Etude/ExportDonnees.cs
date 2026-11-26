using System.Globalization;
using System.Text;
using Silx.Core.Raw;
using Silx.Mesures.Gabarits;
namespace Silx.Etude;

/// <summary>
/// Exports de données en CSV « à la française » (séparateur « ; », virgule décimale) : s'ouvre tel quel dans un tableur
/// réglé en français et se relit dans Silx (import PWL).
/// </summary>
public static class ExportDonnees
{
    public const char Separateur = ';';
    static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Nombre(double? v) => v is { } x && double.IsFinite(x) ? x.ToString("G9", Fr) : "";

    static string Cellule(string s) => s.IndexOfAny([Separateur, '"', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    static void Ligne(StringBuilder sb, IEnumerable<string> cellules) => sb.Append(string.Join(Separateur, cellules.Select(Cellule))).Append('\n');

    /// <summary>
    /// Courbes simulées : une colonne pour l'abscisse (temps ou fréquence) puis une par signal ; en fréquentiel, module en
    /// dB et phase en degrés. Les signaux indisponibles sont omis.
    /// </summary>
    public static string Courbes(AnalyseKind k, RawFile raw, IEnumerable<string> signaux, Schematic? fusion)
    {
        var sb = new StringBuilder();
        var dispo = signaux.Distinct(StringComparer.OrdinalIgnoreCase).Where(s => Signaux.Disponible(raw, s, fusion)).ToList();
        if (k is AnalyseKind.Transitoire or AnalyseKind.Dc)
        {
            var cols = dispo.Select(s => Verification.Courbe(k, raw, s, fusion)).ToList();
            var x = cols.Count > 0 ? cols[0].X : [];
            Ligne(sb, dispo.Select(s => $"{Signaux.Nom(s, fusion)} ({Signaux.Unite(s)})").Prepend(k == AnalyseKind.Dc ? raw.Variables[0].Name : "temps (s)"));
            for (int i = 0; i < x.Length; i++) Ligne(sb, cols.Select(c => Nombre(c.Y[i])).Prepend(Nombre(x[i])));
        }
        else
        {
            var f = raw.Vector("frequency");
            var cols = dispo.Select(s =>
            {
                var (vec, signe) = Signaux.Vecteur(s, fusion)!.Value;
                var re = raw.Vector(vec); var im = raw.Imaginary(vec);
                return (Db: re.Select((r, i) => 20 * Math.Log10(Math.Max(Math.Sqrt(r * r + im[i] * im[i]), 1e-300))).ToArray(),
                        Ph: re.Select((r, i) => Math.Atan2(signe * im[i], signe * r) * 180 / Math.PI).ToArray());
            }).ToList();
            Ligne(sb, dispo.SelectMany(s => new[] { $"|{Signaux.Nom(s, fusion)}| (dB)", $"phase {Signaux.Nom(s, fusion)} (°)" }).Prepend("fréquence (Hz)"));
            for (int i = 0; i < f.Length; i++) Ligne(sb, cols.SelectMany(c => new[] { Nombre(c.Db[i]), Nombre(c.Ph[i]) }).Prepend(Nombre(f[i])));
        }
        return sb.ToString();
    }

    static string Texte(Verdict v) => v switch { Silx.Mesures.Gabarits.Verdict.Pass => "conforme", Silx.Mesures.Gabarits.Verdict.Fail => "non conforme", _ => "non évaluable" };

    /// <summary>
    /// Mesures d'un banc : critères chiffrés et gabarits dessinés (valeur, bornes, verdict) ; avec une campagne, les
    /// statistiques par critère puis une ligne par tirage (valeurs des composants et mesures).
    /// </summary>
    public static string Mesures(Banc banc, ResultatEtude? res, ResultatCampagne? camp, Schematic? fusion)
    {
        var sb = new StringBuilder();
        Ligne(sb, ["banc", banc.Nom]);
        Ligne(sb, ["analyse", TypesAnalyse.Libelle(banc.Type)]);
        sb.Append('\n');
        Ligne(sb, ["contrainte", "signal", "mini", "maxi", "valeur", "unité", "verdict", "détail"]);
        foreach (var c in banc.CriteresActifs)
        {
            var r = res?.Criteres.FirstOrDefault(x => x.Critere == c)?.Resultat;
            Ligne(sb, [c.Info.Nom, Signaux.Nom(c.Noeud, fusion), Nombre(c.Min), Nombre(c.Max), Nombre(r?.Valeur), r?.Unite ?? c.Info.Unite,
                r == null ? "non simulé" : Texte(r.Verdict), r?.Detail ?? ""]);
        }
        foreach (var t in banc.TracesActives)
        {
            var r = res?.Traces.FirstOrDefault(x => x.Gabarit == t)?.Resultat;
            Ligne(sb, [$"Gabarit {(t.Limite == Silx.Mesures.Gabarits.Limite.Haute ? "haut" : "bas")} ({t.Points.Count} points)", Signaux.Nom(t.Noeud, fusion), "", "",
                Nombre(r?.Marge), r?.Marge != null ? "marge " + t.Unite : "", r == null ? "non simulé" : Texte(r.Verdict), r?.Detail ?? ""]);
        }
        if (camp is { Tirages.Count: > 0 })
        {
            sb.Append('\n');
            Ligne(sb, ["campagne", camp.Mode.ToString(), $"{camp.Conformes}/{camp.Tirages.Count} conformes", Nombre(100 * camp.Taux) + " %"]);
            Ligne(sb, ["mesure", "signal", "N", "moyenne", "écart-type", "mini", "maxi", "Cpk", "échecs"]);
            foreach (var s in camp.Stats)
                Ligne(sb, [s.Critere.Info.Nom, Signaux.Nom(s.Critere.Noeud, fusion), s.N.ToString(), Nombre(s.Moyenne), Nombre(s.EcartType), Nombre(s.Min), Nombre(s.Max), Nombre(s.Cpk), s.Echecs.ToString()]);
            sb.Append('\n');
            var refs = camp.Tirages.SelectMany(t => t.Tirage.Valeurs.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            bool temp = camp.Tirages.Any(t => t.Tirage.Temperature != null);
            var crits = banc.CriteresActifs.ToList(); var traces = banc.TracesActives.ToList();
            var tete = new List<string> { "tirage", "libellé", "verdict" };
            tete.AddRange(refs); if (temp) tete.Add("température (°C)");
            tete.AddRange(crits.Select(c => $"{c.Info.Nom} {Signaux.Nom(c.Noeud, fusion)}"));
            tete.AddRange(traces.Select(t => $"marge gabarit {(t.Limite == Silx.Mesures.Gabarits.Limite.Haute ? "haut" : "bas")} {Signaux.Nom(t.Noeud, fusion)}"));
            Ligne(sb, tete);
            foreach (var t in camp.Tirages)
            {
                var l = new List<string> { t.Tirage.Index.ToString(), t.Tirage.Libelle, t.Erreur != null ? "erreur : " + t.Erreur : t.Conforme ? "conforme" : "non conforme" };
                l.AddRange(refs.Select(r => Nombre(t.Tirage.Valeurs.TryGetValue(r, out var v) ? v : null)));
                if (temp) l.Add(Nombre(t.Tirage.Temperature));
                l.AddRange(crits.Select(c => Nombre(t.Criteres.TryGetValue(c, out var r) ? r.Valeur : null)));
                l.AddRange(traces.Select(g => Nombre(t.Traces.TryGetValue(g, out var r) ? r.Marge : null)));
                Ligne(sb, l);
            }
        }
        return sb.ToString();
    }

    /// <summary>Forme d'onde d'un stimulus sur la durée du banc (« temps;valeur »), relisible comme source PWL ; null si aléatoire.</summary>
    public static string? Stimulus(Stimulus st, double duree, bool courant)
    {
        if (st.Echantillons(duree) is not { } pts) return null;
        var sb = new StringBuilder();
        Ligne(sb, ["temps (s)", $"valeur ({(courant ? "A" : "V")})"]);
        foreach (var (t, v) in pts) Ligne(sb, [Nombre(t), Nombre(v)]);
        return sb.ToString();
    }
}
