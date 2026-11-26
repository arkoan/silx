using Silx.Mesures;
using Silx.Mesures.Courbes;
using Silx.Mesures.Mesures;
namespace Silx.Etude;

/// <summary>
/// Mesures de régime établi (oscillateurs, signaux périodiques) et de passe-bande : fréquence et crête à crête sur la
/// dernière partie du signal, gain maximal et largeur de bande à −3 dB autour du maximum.
/// </summary>
public static class MesuresEtablies
{
    /// <summary>Fraction finale du signal prise comme régime établi.</summary>
    public const double FractionEtablie = 0.25;

    static (double[] T, double[] V) Fin(CourbeTemporelle c)
    {
        var t = c.Temps.ToArray(); var v = c.Valeurs.ToArray();
        double t0 = t[^1] - (t[^1] - t[0]) * FractionEtablie;
        int i = Array.FindIndex(t, x => x >= t0);
        return (t[i..], v[i..]);
    }

    /// <summary>Fréquence : passages montants par la valeur moyenne sur le régime établi (au moins 3).</summary>
    public static double Frequence(CourbeTemporelle c)
    {
        var (t, v) = Fin(c);
        double moy = v.Average();
        var passages = new List<double>();
        for (int i = 1; i < v.Length; i++)
            if (v[i - 1] < moy && v[i] >= moy) passages.Add(t[i - 1] + (moy - v[i - 1]) / (v[i] - v[i - 1]) * (t[i] - t[i - 1]));
        if (passages.Count < 3) throw new MesureImpossibleException("Pas de signal périodique sur le régime établi (moins de 3 périodes).");
        return (passages.Count - 1) / (passages[^1] - passages[0]);
    }

    public static double CreteACrete(CourbeTemporelle c) { var (_, v) = Fin(c); return v.Max() - v.Min(); }

    /// <summary>Largeur entre les deux fréquences où le gain passe à 3 dB sous son maximum, de part et d'autre du maximum.</summary>
    public static double LargeurBande3dB(ReponseFrequentielle r)
    {
        var f = r.Frequences.ToArray(); var db = r.Valeurs.ToArray().Select(Frequentiel.Db).ToArray();
        int k = Array.IndexOf(db, db.Max()); double seuil = db[k] - 3;
        int a = k; while (a > 0 && db[a] > seuil) a--;
        int b = k; while (b < db.Length - 1 && db[b] > seuil) b++;
        if (db[a] > seuil || db[b] > seuil) throw new MesureImpossibleException("Le gain ne retombe pas de 3 dB des deux côtés du maximum dans la plage simulée.");
        double Interp(int i, int j) => f[i] + (seuil - db[i]) / (db[j] - db[i]) * (f[j] - f[i]);
        return Interp(b - 1, b) - Interp(a, a + 1);
    }
}
