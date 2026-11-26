using Silx.Mesures;
using Silx.Mesures.Courbes;
namespace Silx.Etude;

/// <summary>Mesures sur une caractéristique de transfert DC (abscisse = grandeur balayée, rangée croissante).</summary>
public static class MesuresDc
{
    /// <summary>Valeur de la grandeur balayée où la sortie franchit le milieu de son excursion (premier passage).</summary>
    public static double Seuil(CourbeTemporelle c)
    {
        var x = c.Temps.ToArray(); var y = c.Valeurs.ToArray();
        double lo = y.Min(), hi = y.Max(), mi = (lo + hi) / 2;
        if (hi - lo < 1e-12) throw new MesureImpossibleException("La sortie ne varie pas sur le balayage.");
        for (int i = 1; i < y.Length; i++)
            if ((y[i - 1] - mi) * (y[i] - mi) <= 0 && y[i] != y[i - 1])
                return x[i - 1] + (mi - y[i - 1]) / (y[i] - y[i - 1]) * (x[i] - x[i - 1]);
        throw new MesureImpossibleException("Pas de basculement sur le balayage.");
    }

    /// <summary>Plus grande pente |dy/dx| (gain petit signal maximal sur la plage balayée), signée.</summary>
    public static double PenteMax(CourbeTemporelle c)
    {
        var x = c.Temps.ToArray(); var y = c.Valeurs.ToArray();
        if (x.Length < 2) throw new MesureImpossibleException("Balayage trop court.");
        double best = 0;
        for (int i = 1; i < x.Length; i++)
        {
            double d = (y[i] - y[i - 1]) / Math.Max(x[i] - x[i - 1], 1e-30);
            if (Math.Abs(d) > Math.Abs(best)) best = d;
        }
        return best;
    }
}
