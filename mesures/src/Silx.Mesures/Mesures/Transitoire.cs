using Silx.Mesures.Courbes;

namespace Silx.Mesures.Mesures;

/// <summary>
/// Mesures sur réponse indicielle / transitoire.
/// Hypothèses (voir docs/HYPOTHESES.md) : valeur initiale = premier échantillon ;
/// valeur finale = moyenne temporelle sur les <c>fractionFinale</c> (5 % par défaut) derniers de la durée ;
/// l'échelon est supposé monotone dans le sens (final - initial) ;
/// la fenêtre finale doit être plate (≤ 1 % de l'excursion) sinon la mesure est impossible.
/// </summary>
public static class Transitoire
{
    public const double FractionFinaleDefaut = 0.05;

    public static double ValeurInitiale(CourbeTemporelle c) => c.Valeurs[0];

    public static double ValeurFinale(CourbeTemporelle c, double fractionFinale = FractionFinaleDefaut)
    {
        if (fractionFinale is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(fractionFinale));
        var t = c.Temps; var v = c.Valeurs;
        double debut = t[^1] - fractionFinale * c.Duree;
        double aire = 0;
        // intégrale trapézoïdale de debut à t[^1]
        double tPrec = debut, vPrec = c.Interpoler(debut);
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] <= debut) continue;
            aire += 0.5 * (vPrec + v[i]) * (t[i] - tPrec);
            tPrec = t[i]; vPrec = v[i];
        }
        return aire / (t[^1] - debut);
    }

    /// <summary>Gain DC en régime établi : valeur finale / amplitude d'entrée.</summary>
    public static double GainDc(CourbeTemporelle sortie, double amplitudeEntree, double fractionFinale = FractionFinaleDefaut)
    {
        if (amplitudeEntree == 0 || !double.IsFinite(amplitudeEntree)) throw new ArgumentException("Amplitude d'entrée invalide.");
        return ValeurFinale(sortie, fractionFinale) / amplitudeEntree;
    }

    /// <summary>Offset : valeur moyenne sur la fenêtre [tDebut, tFin] (signal au repos).</summary>
    public static double Offset(CourbeTemporelle c, double tDebut, double tFin)
    {
        if (!(tFin > tDebut)) throw new ArgumentException("Fenêtre invalide.");
        if (tDebut < c.Temps[0] || tFin > c.Temps[^1]) throw new ArgumentException("Fenêtre hors de la courbe.");
        var t = c.Temps; var v = c.Valeurs;
        double aire = 0, tPrec = tDebut, vPrec = c.Interpoler(tDebut);
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] <= tDebut) continue;
            if (t[i] >= tFin) break;
            aire += 0.5 * (vPrec + v[i]) * (t[i] - tPrec);
            tPrec = t[i]; vPrec = v[i];
        }
        aire += 0.5 * (vPrec + c.Interpoler(tFin)) * (tFin - tPrec);
        return aire / (tFin - tDebut);
    }

    /// <summary>Dépassement en % de l'amplitude de l'échelon (≥ 0).</summary>
    public static double Depassement(CourbeTemporelle c, double fractionFinale = FractionFinaleDefaut)
    {
        var (v0, vf, signe) = Echelon(c, fractionFinale);
        double pic = double.NegativeInfinity;
        foreach (var x in c.Valeurs) pic = Math.Max(pic, signe * x);
        double exces = pic - signe * vf;
        return Math.Max(0, exces) / Math.Abs(vf - v0) * 100.0;
    }

    /// <summary>Temps de montée entre deux fractions de l'excursion (10 %–90 % par défaut), en secondes.</summary>
    public static double TempsDeMontee(CourbeTemporelle c, double bas = 0.1, double haut = 0.9,
        double fractionFinale = FractionFinaleDefaut)
    {
        if (!(0 <= bas && bas < haut && haut <= 1)) throw new ArgumentException("Fractions invalides.");
        var (v0, vf, _) = Echelon(c, fractionFinale);
        double tBas = PremierPassage(c, v0 + bas * (vf - v0), vf > v0);
        double tHaut = PremierPassage(c, v0 + haut * (vf - v0), vf > v0);
        return tHaut - tBas;
    }

    /// <summary>
    /// Temps d'établissement à ±<paramref name="tolerance"/> (2 % par défaut) de l'excursion, mesuré
    /// depuis le début de la courbe (t0 = premier échantillon) jusqu'à la dernière sortie de bande.
    /// </summary>
    public static double TempsEtablissement(CourbeTemporelle c, double tolerance = 0.02,
        double fractionFinale = FractionFinaleDefaut)
    {
        if (!(tolerance > 0 && tolerance < 1)) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var (v0, vf, _) = Echelon(c, fractionFinale);
        double demi = tolerance * Math.Abs(vf - v0);
        var t = c.Temps; var v = c.Valeurs;
        if (Math.Abs(v[^1] - vf) > demi)
            throw new MesureImpossibleException("Signal non établi à la fin de la courbe.");
        for (int i = t.Length - 1; i > 0; i--)
        {
            double e0 = v[i - 1] - vf, e1 = v[i] - vf;
            if (Math.Abs(e0) > demi) // le segment [i-1,i] part hors bande et finit dedans : sortie de bande
            {
                double bord = demi * Math.Sign(e0);
                return Interpolation.Croisement(t[i - 1], e0, t[i], e1, bord) - t[0];
            }
        }
        return 0; // toujours dans la bande
    }

    private static (double v0, double vf, int signe) Echelon(CourbeTemporelle c, double fractionFinale)
    {
        double v0 = ValeurInitiale(c), vf = ValeurFinale(c, fractionFinale);
        double amp = vf - v0;
        double ref_ = Math.Max(Math.Abs(v0), Math.Abs(vf));
        if (Math.Abs(amp) <= 1e-12 * Math.Max(1.0, ref_))
            throw new MesureImpossibleException("Pas d'échelon : valeur finale égale à la valeur initiale.");
        // Garde-fou : la fenêtre finale doit être plate (≤ 1 % de l'excursion crête-à-crête), sinon la
        // « valeur finale » n'en est pas une (courbe tronquée avant l'établissement).
        if (ExcursionFenetreFinale(c, fractionFinale) > 0.01 * Math.Abs(amp))
            throw new MesureImpossibleException("Signal non établi : la fenêtre finale n'est pas stable.");
        return (v0, vf, Math.Sign(amp));
    }

    private static double ExcursionFenetreFinale(CourbeTemporelle c, double fractionFinale)
    {
        double debut = c.Temps[^1] - fractionFinale * c.Duree;
        double min = c.Interpoler(debut), max = min;
        for (int i = 0; i < c.Count; i++)
        {
            if (c.Temps[i] < debut) continue;
            min = Math.Min(min, c.Valeurs[i]); max = Math.Max(max, c.Valeurs[i]);
        }
        return max - min;
    }

    private static double PremierPassage(CourbeTemporelle c, double niveau, bool montant)
    {
        var t = c.Temps; var v = c.Valeurs;
        for (int i = 1; i < t.Length; i++)
        {
            bool avant = montant ? v[i - 1] < niveau : v[i - 1] > niveau;
            bool apres = montant ? v[i] >= niveau : v[i] <= niveau;
            if (avant && apres) return Interpolation.Croisement(t[i - 1], v[i - 1], t[i], v[i], niveau);
        }
        throw new MesureImpossibleException("Le niveau n'est jamais atteint.");
    }
}
