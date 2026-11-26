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

    /// <summary>Périodes entières du régime établi (entre le premier et le dernier passage montant par la moyenne), si au moins une.</summary>
    static (double[] T, double[] V)? Periodes(CourbeTemporelle c, out int nPer)
    {
        var (t, v) = Fin(c); nPer = 0;
        double moy = v.Average();
        var p = new List<double>();
        for (int i = 1; i < v.Length; i++)
            if (v[i - 1] < moy && v[i] >= moy) p.Add(t[i - 1] + (moy - v[i - 1]) / (v[i] - v[i - 1]) * (t[i] - t[i - 1]));
        if (p.Count < 2) return null;
        nPer = p.Count - 1;
        return Reechantillonner(t, v, p[0], p[^1], 4096);
    }

    static (double[] T, double[] V) Reechantillonner(double[] t, double[] v, double a, double b, int n)
    {
        var tt = new double[n]; var vv = new double[n]; int j = 0;
        for (int i = 0; i < n; i++)
        {
            double x = a + (b - a) * i / n;   // intervalle semi-ouvert : n points sur des périodes entières
            while (j < t.Length - 2 && t[j + 1] < x) j++;
            double u = t[j + 1] > t[j] ? Math.Clamp((x - t[j]) / (t[j + 1] - t[j]), 0, 1) : 0;
            tt[i] = x; vv[i] = v[j] + u * (v[j + 1] - v[j]);
        }
        return (tt, vv);
    }

    /// <summary>Valeur efficace (RMS) sur le régime établi : sur des périodes entières si le signal est périodique.</summary>
    public static double Efficace(CourbeTemporelle c)
    {
        var (_, v) = Periodes(c, out _) ?? Uniforme(c);
        return Math.Sqrt(v.Average(x => x * x));
    }

    /// <summary>Valeur moyenne sur le régime établi (sur des périodes entières si le signal est périodique).</summary>
    public static double Moyenne(CourbeTemporelle c) => (Periodes(c, out _) ?? Uniforme(c)).V.Average();

    static (double[] T, double[] V) Uniforme(CourbeTemporelle c) { var (t, v) = Fin(c); return Reechantillonner(t, v, t[0], t[^1], 4096); }

    /// <summary>Nombre d'harmoniques prises en compte par la THD (2 à 10).</summary>
    public const int HarmoniquesThd = 10;

    /// <summary>
    /// Distorsion harmonique totale en % : √(Σ A²ₕ, h = 2…10) / A₁, amplitudes calculées par transformée de Fourier exacte sur
    /// des périodes entières du régime établi (pas de fenêtre ni de fuite spectrale).
    /// </summary>
    public static double Thd(CourbeTemporelle c)
    {
        if (Periodes(c, out int np) is not { } s || np < 3)
            throw new MesureImpossibleException("Pas de signal périodique sur le régime établi (moins de 3 périodes).");
        var v = s.V; int n = v.Length;
        double A(int h)
        {
            double re = 0, im = 0, w = 2 * Math.PI * h * np / n;
            for (int i = 0; i < n; i++) { re += v[i] * Math.Cos(w * i); im += v[i] * Math.Sin(w * i); }
            return 2 * Math.Sqrt(re * re + im * im) / n;
        }
        double a1 = A(1);
        if (a1 <= 0) throw new MesureImpossibleException("Fondamental nul.");
        double somme = 0;
        for (int h = 2; h <= HarmoniquesThd && h * np < n / 2; h++) somme += A(h) * A(h);
        return 100 * Math.Sqrt(somme) / a1;
    }
}
