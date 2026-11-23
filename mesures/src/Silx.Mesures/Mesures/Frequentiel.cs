using System.Numerics;
using Silx.Mesures.Courbes;

namespace Silx.Mesures.Mesures;

/// <summary>
/// Mesures AC. Interpolation linéaire en (log f, dB) et (log f, phase) entre points.
/// Hypothèses (voir docs/HYPOTHESES.md) : « gain DC » = |H| au point de plus basse fréquence ;
/// marges mesurées sur le gain de boucle L(jf) (retour unitaire, signe inclus dans L) ;
/// première intersection rencontrée en balayant vers les hautes fréquences.
/// </summary>
public static class Frequentiel
{
    public static double Db(Complex h) => 20.0 * Math.Log10(h.Magnitude);

    /// <summary>Gain en régime quasi-statique (|H| au plus bas point de fréquence), échelle linéaire.</summary>
    public static double GainDc(ReponseFrequentielle r) => r.Valeurs[0].Magnitude;

    /// <summary>Bande passante à -3 dB (Hz) relativement au gain du premier point.</summary>
    public static double BandePassante3dB(ReponseFrequentielle r)
    {
        var f = r.Frequences; var h = r.Valeurs;
        double ref0 = Db(h[0]);
        if (!double.IsFinite(ref0)) throw new MesureImpossibleException("Gain DC nul.");
        double seuil = ref0 - 10.0 * Math.Log10(2.0); // -3,0103 dB exact (1/√2)
        for (int i = 1; i < f.Length; i++)
        {
            double a = Db(h[i - 1]), b = Db(h[i]);
            if (a > seuil && b <= seuil)
                return Math.Pow(10, Interpolation.Croisement(Math.Log10(f[i - 1]), a, Math.Log10(f[i]), b, seuil));
        }
        throw new MesureImpossibleException("Le gain ne descend jamais sous -3 dB dans la plage.");
    }

    /// <summary>Marge de phase en degrés : 180° + phase(L) à la fréquence de gain unité. Retourne aussi cette fréquence.</summary>
    public static double MargeDePhase(ReponseFrequentielle l, out double frequenceUniteHz)
    {
        var f = l.Frequences; var h = l.Valeurs;
        var ph = PhaseDeroulee(h);
        for (int i = 1; i < f.Length; i++)
        {
            double a = Db(h[i - 1]), b = Db(h[i]);
            if (a > 0 && b <= 0)
            {
                double lf = Interpolation.Croisement(Math.Log10(f[i - 1]), a, Math.Log10(f[i]), b, 0.0);
                frequenceUniteHz = Math.Pow(10, lf);
                double phi = Interpolation.Lineaire(Math.Log10(f[i - 1]), ph[i - 1], Math.Log10(f[i]), ph[i], lf);
                return 180.0 + phi;
            }
        }
        throw new MesureImpossibleException("Pas de passage à 0 dB (gain unité) du gain de boucle.");
    }

    public static double MargeDePhase(ReponseFrequentielle l) => MargeDePhase(l, out _);

    /// <summary>
    /// Marge de gain en dB : -|L| (dB) à la première fréquence où la phase déroulée atteint -180°.
    /// Si la phase n'atteint jamais -180° dans la plage, lève <see cref="MesureImpossibleException"/>
    /// (on ne renvoie pas « infini » : la plage balayée peut simplement être trop courte).
    /// </summary>
    public static double MargeDeGain(ReponseFrequentielle l, out double frequence180Hz)
    {
        var f = l.Frequences; var h = l.Valeurs;
        var ph = PhaseDeroulee(h);
        for (int i = 1; i < f.Length; i++)
        {
            if (ph[i - 1] > -180.0 && ph[i] <= -180.0)
            {
                double lf = Interpolation.Croisement(Math.Log10(f[i - 1]), ph[i - 1], Math.Log10(f[i]), ph[i], -180.0);
                frequence180Hz = Math.Pow(10, lf);
                double db = Interpolation.Lineaire(Math.Log10(f[i - 1]), Db(h[i - 1]), Math.Log10(f[i]), Db(h[i]), lf);
                return -db;
            }
        }
        throw new MesureImpossibleException("La phase n'atteint jamais -180° dans la plage.");
    }

    public static double MargeDeGain(ReponseFrequentielle l) => MargeDeGain(l, out _);

    /// <summary>Phase en degrés, déroulée (continue), premier point ramené dans (-180°, 180°].</summary>
    internal static double[] PhaseDeroulee(ReadOnlySpan<Complex> h)
    {
        var res = new double[h.Length];
        double prec = h[0].Phase;
        double cumul = prec;
        res[0] = cumul * 180.0 / Math.PI;
        for (int i = 1; i < h.Length; i++)
        {
            double p = h[i].Phase;
            double d = p - prec;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d <= -Math.PI) d += 2 * Math.PI;
            cumul += d;
            prec = p;
            res[i] = cumul * 180.0 / Math.PI;
        }
        return res;
    }
}
