namespace Silx.Etude;

/// <summary>
/// Spectre d'un signal temporel simulé : rééchantillonnage uniforme (pas de ngspice variable),
/// fenêtre de Hann, FFT radix-2, amplitude crête en dBV (0 dBV = 1 V crête).
/// </summary>
public static class Spectre
{
    public const int TailleMax = 1 << 16;

    public static (double[] F, double[] DbV) Calculer(IReadOnlyList<double> t, IReadOnlyList<double> y)
    {
        int m = Math.Min(t.Count, y.Count);
        if (m < 4 || !(t[m - 1] > t[0])) return ([], []);
        int n = 1; while (n < m && n < TailleMax) n <<= 1;
        double t0 = t[0], duree = t[m - 1] - t0, dt = duree / (n - 1);
        var re = new double[n]; var im = new double[n];
        double somme = 0; int j = 0;
        for (int i = 0; i < n; i++)
        {
            double ti = t0 + i * dt;
            while (j < m - 2 && t[j + 1] < ti) j++;
            double a = t[j + 1] > t[j] ? Math.Clamp((ti - t[j]) / (t[j + 1] - t[j]), 0, 1) : 0;
            double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
            re[i] = (y[j] + a * (y[j + 1] - y[j])) * w; somme += w;
        }
        Fft(re, im);
        int k = n / 2;
        var f = new double[k - 1]; var db = new double[k - 1];
        for (int i = 1; i < k; i++)
        {
            double amp = 2 * Math.Sqrt(re[i] * re[i] + im[i] * im[i]) / somme;
            f[i - 1] = i / (n * dt); db[i - 1] = 20 * Math.Log10(Math.Max(amp, 1e-12));
        }
        return (f, db);
    }

    /// <summary>FFT complexe en place (Cooley-Tukey itératif, taille puissance de 2).</summary>
    public static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                    double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
    }
}
