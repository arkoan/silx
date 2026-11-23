using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Silx.Mesures.Courbes;

namespace Silx.Mesures.Mesures;

public static class Spectral
{
    /// <summary>
    /// THD (rapport, pas %) = √(Σ_{k=2..n} Vk²) / V1.
    /// Hypothèses (voir docs/HYPOTHESES.md) : fondamentale connue (<paramref name="f0Hz"/>) ;
    /// analyse sur un nombre entier de périodes finissant à la fin de la courbe (régime établi),
    /// au plus <paramref name="periodes"/> périodes ; ré-échantillonnage linéaire uniforme ;
    /// fenêtre rectangulaire (exacte pour un nombre entier de périodes).
    /// </summary>
    public static double Thd(CourbeTemporelle c, double f0Hz, int periodes = 10, int harmoniques = 10,
        int pointsParPeriode = 256)
    {
        if (!(f0Hz > 0) || !double.IsFinite(f0Hz)) throw new ArgumentException("Fondamentale invalide.");
        if (periodes < 1 || harmoniques < 2 || pointsParPeriode < 2 * (harmoniques + 1))
            throw new ArgumentException("Paramètres d'analyse invalides (Nyquist ou périodes).");

        double T = 1.0 / f0Hz;
        int dispo = (int)Math.Floor(c.Duree / T + 1e-9);
        int n = Math.Min(periodes, dispo);
        if (n < 1) throw new MesureImpossibleException("Moins d'une période de la fondamentale dans la courbe.");

        int N = n * pointsParPeriode;
        double tFin = c.Temps[^1], tDeb = tFin - n * T, dt = T / pointsParPeriode;
        var x = new Complex[N];
        for (int i = 0; i < N; i++) x[i] = c.Interpoler(Math.Min(tDeb + i * dt, tFin));
        Fourier.Forward(x, FourierOptions.AsymmetricScaling); // sans normalisation : |X[k]| = N·A/2
        double Amp(int k) => 2.0 * x[k * n].Magnitude / N; // la raie de l'harmonique k est au bin k·n

        double v1 = Amp(1);
        if (!(v1 > 1e-15)) throw new MesureImpossibleException("Fondamentale d'amplitude nulle.");
        double somme = 0;
        for (int k = 2; k <= harmoniques; k++) somme += Amp(k) * Amp(k);
        return Math.Sqrt(somme) / v1;
    }
}
