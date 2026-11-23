using System.Numerics;
using Silx.Mesures.Courbes;

namespace Silx.Mesures.Tests.Reference;

/// <summary>Circuits de référence à solution analytique connue.</summary>
public static class Circuits
{
    public static double[] GrilleLog(double fMin, double fMax, int parDecade)
    {
        int n = (int)Math.Round(Math.Log10(fMax / fMin) * parDecade);
        return Enumerable.Range(0, n + 1).Select(i => fMin * Math.Pow(10, (double)i / parDecade)).ToArray();
    }

    /// <summary>Grille temporelle volontairement NON uniforme (pas croissant, comme un pas variable SPICE).</summary>
    public static double[] GrilleTempsNonUniforme(double tFin, int n)
        => Enumerable.Range(0, n + 1).Select(i => tFin * Math.Pow((double)i / n, 1.5)).ToArray();

    // ---------- RC passe-bas : H = 1/(1+jωRC) ----------
    public static ReponseFrequentielle RcAc(double r, double c, double[] f)
        => new(f, f.Select(x => 1.0 / (Complex.One + Complex.ImaginaryOne * 2 * Math.PI * x * r * c)));

    public static double RcFc(double r, double c) => 1.0 / (2 * Math.PI * r * c);

    public static CourbeTemporelle RcEchelon(double r, double c, double amp, double[] t)
        => new(t, t.Select(x => amp * (1 - Math.Exp(-x / (r * c)))));

    // ---------- Second ordre : H = ωn²/(s²+2ζωn s+ωn²) ----------
    public static ReponseFrequentielle SecondOrdreAc(double fn, double zeta, double[] f)
    {
        double wn = 2 * Math.PI * fn;
        return new(f, f.Select(x =>
        {
            var s = Complex.ImaginaryOne * 2 * Math.PI * x;
            return wn * wn / (s * s + 2 * zeta * wn * s + wn * wn);
        }));
    }

    public static double SecondOrdreEchelonAt(double fn, double zeta, double t)
    {
        double wn = 2 * Math.PI * fn, wd = wn * Math.Sqrt(1 - zeta * zeta);
        return 1 - Math.Exp(-zeta * wn * t) / Math.Sqrt(1 - zeta * zeta) * Math.Sin(wd * t + Math.Acos(zeta));
    }

    public static CourbeTemporelle SecondOrdreEchelon(double fn, double zeta, double[] t)
        => new(t, t.Select(x => SecondOrdreEchelonAt(fn, zeta, x)));

    public static double SecondOrdreDepassementPct(double zeta)
        => 100 * Math.Exp(-Math.PI * zeta / Math.Sqrt(1 - zeta * zeta));

    public static double SecondOrdreBandePassante(double fn, double zeta)
        => fn * Math.Sqrt(1 - 2 * zeta * zeta + Math.Sqrt(4 * Math.Pow(zeta, 4) - 4 * zeta * zeta + 2));

    /// <summary>Gain de boucle L = ωn²/(s(s+2ζωn)) : en boucle fermée unitaire on retrouve le 2e ordre standard.</summary>
    public static ReponseFrequentielle SecondOrdreBoucle(double fn, double zeta, double[] f)
    {
        double wn = 2 * Math.PI * fn;
        return new(f, f.Select(x =>
        {
            var s = Complex.ImaginaryOne * 2 * Math.PI * x;
            return wn * wn / (s * (s + 2 * zeta * wn));
        }));
    }

    public static double SecondOrdreMargePhaseDeg(double zeta)
        => Math.Atan(2 * zeta / Math.Sqrt(Math.Sqrt(1 + 4 * Math.Pow(zeta, 4)) - 2 * zeta * zeta)) * 180 / Math.PI;

    // ---------- Boucle 3e ordre : L = K/(1+s/ω0)³ ----------
    public static ReponseFrequentielle TroisiemeOrdreBoucle(double k, double f0, double[] f)
        => new(f, f.Select(x => k / Complex.Pow(Complex.One + Complex.ImaginaryOne * x / f0, 3)));
}
