using Silx.Mesures.Courbes;

namespace Silx.Mesures.Gabarits;

/// <summary>Côté de la limite : la zone interdite est au-dessus d'une limite haute, en dessous d'une limite basse.</summary>
public enum Limite { Haute, Basse }

public sealed record ResultatGabaritGraphique(string Nom, Verdict Verdict, double? Marge, double? XPire, double? YPire, string? Detail);

/// <summary>
/// Gabarit graphique : ligne brisée (x croissants) qu'une courbe ne doit pas franchir.
/// Marge = plus petite distance verticale (unité de la courbe) entre la courbe et la limite, côté autorisé ;
/// négative en cas de violation. Évaluée aux échantillons de la courbe et aux sommets du gabarit, sur la seule
/// plage d'abscisses commune ; l'interpolation entre sommets se fait en log x si <see cref="EchelleLogX"/>.
/// </summary>
public sealed class GabaritGraphique
{
    readonly double[] _x, _y;

    public GabaritGraphique(string nom, Limite limite, IEnumerable<(double X, double Y)> points, bool echelleLogX = false)
    {
        var p = points.ToArray();
        if (p.Length < 2) throw new ArgumentException("Au moins 2 points requis.");
        for (int i = 0; i < p.Length; i++)
        {
            if (!double.IsFinite(p[i].X) || !double.IsFinite(p[i].Y)) throw new ArgumentException($"Point {i} non fini.");
            if (echelleLogX && p[i].X <= 0) throw new ArgumentException($"Point {i} : abscisse ≤ 0 en échelle log.");
            if (i > 0 && p[i].X <= p[i - 1].X) throw new ArgumentException($"Abscisses non strictement croissantes au point {i}.");
        }
        Nom = nom; Limite = limite; EchelleLogX = echelleLogX;
        _x = p.Select(q => q.X).ToArray(); _y = p.Select(q => q.Y).ToArray();
    }

    public string Nom { get; }
    public Limite Limite { get; }
    public bool EchelleLogX { get; }
    public IReadOnlyList<(double X, double Y)> Points => _x.Select((x, i) => (x, _y[i])).ToList();

    double T(double x) => EchelleLogX ? Math.Log10(x) : x;

    /// <summary>Valeur de la limite en x (x dans la plage du gabarit).</summary>
    public double LimiteEn(double x)
    {
        int i = Array.BinarySearch(_x, x);
        if (i >= 0) return _y[i];
        i = ~i;
        return Interpolation.Lineaire(T(_x[i - 1]), _y[i - 1], T(_x[i]), _y[i], T(x));
    }

    public ResultatGabaritGraphique Evaluer(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Count < 2) throw new ArgumentException("Courbe invalide.");
        double lo = Math.Max(x[0], _x[0]), hi = Math.Min(x[^1], _x[^1]);
        if (lo > hi) return new(Nom, Verdict.NonEvaluable, null, null, null, "Le gabarit et la courbe ne se recouvrent pas.");

        // Abscisses testées : échantillons de la courbe + sommets du gabarit, dans [lo, hi].
        var xs = x.Where(v => v >= lo && v <= hi).Concat(_x.Where(v => v >= lo && v <= hi)).Append(lo).Append(hi).Distinct().Order();
        double marge = double.PositiveInfinity, xPire = double.NaN, yPire = double.NaN;
        var xa = x as double[] ?? x.ToArray(); var ya = y as double[] ?? y.ToArray();
        foreach (var xi in xs)
        {
            double yc = CourbeEn(xa, ya, xi);
            double m = Limite == Limite.Haute ? LimiteEn(xi) - yc : yc - LimiteEn(xi);
            if (m < marge) { marge = m; xPire = xi; yPire = yc; }
        }
        return new(Nom, marge >= 0 ? Verdict.Pass : Verdict.Fail, marge, xPire, yPire, null);
    }

    double CourbeEn(double[] x, double[] y, double xi)
    {
        int i = Array.BinarySearch(x, xi);
        if (i >= 0) return y[i];
        i = ~i;
        return Interpolation.Lineaire(T(x[i - 1]), y[i - 1], T(x[i]), y[i], T(xi));
    }
}
