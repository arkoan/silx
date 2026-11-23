namespace Silx.Mesures.Courbes;

internal static class Interpolation
{
    public static double Lineaire(double x0, double y0, double x1, double y1, double x)
        => y0 + (y1 - y0) * (x - x0) / (x1 - x0);

    /// <summary>Abscisse où le segment (x0,y0)-(x1,y1) atteint <paramref name="niveau"/>.</summary>
    public static double Croisement(double x0, double y0, double x1, double y1, double niveau)
    {
        if (y1 == y0) return x0;
        return x0 + (niveau - y0) * (x1 - x0) / (y1 - y0);
    }
}
