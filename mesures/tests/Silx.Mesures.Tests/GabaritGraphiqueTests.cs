using Silx.Mesures.Gabarits;

namespace Silx.Mesures.Tests;

public class GabaritGraphiqueTests
{
    static readonly double[] T = Enumerable.Range(0, 1001).Select(i => i * 1e-5).ToArray();          // 0..10 ms
    static readonly double[] Rc = T.Select(t => 1 - Math.Exp(-t / 1e-3)).ToArray();                 // tau = 1 ms

    [Fact] public void Limite_haute_respectee_donne_la_marge_minimale()
    {
        var g = new GabaritGraphique("max", Limite.Haute, [(0, 1.05), (10e-3, 1.05)]);
        var r = g.Evaluer(T, Rc);
        Assert.Equal(Verdict.Pass, r.Verdict);
        Assert.Equal(1.05 - Rc[^1], r.Marge!.Value, 1e-9);   // au plus près à la fin
    }

    [Fact] public void Limite_basse_franchie_echoue_avec_point_de_violation()
    {
        // Exige 0,9 dès 1 ms : RC vaut 0,632 à 1 ms.
        var g = new GabaritGraphique("min", Limite.Basse, [(1e-3, 0.9), (10e-3, 0.9)]);
        var r = g.Evaluer(T, Rc);
        Assert.Equal(Verdict.Fail, r.Verdict);
        Assert.Equal(1e-3, r.XPire!.Value, 1e-12);
        Assert.Equal(1 - Math.Exp(-1) - 0.9, r.Marge!.Value, 1e-9);
    }

    [Fact] public void Sommet_du_gabarit_entre_deux_echantillons_est_controle()
    {
        // Pointe étroite de la limite basse entre deux échantillons (aucun échantillon ne la voit).
        var x = new double[] { 0, 1, 2 }; var y = new double[] { 0, 0, 0 };
        var g = new GabaritGraphique("pic", Limite.Basse, [(0.4, -1), (0.5, 0.5), (0.6, -1)]);
        Assert.Equal(Verdict.Fail, g.Evaluer(x, y).Verdict);
    }

    [Fact] public void Interpolation_logarithmique_en_frequence()
    {
        var g = new GabaritGraphique("bode", Limite.Haute, [(10, 0), (1000, -40)], echelleLogX: true);
        Assert.Equal(-20, g.LimiteEn(100), 1e-9);   // milieu de décade, pas milieu linéaire
    }

    [Fact] public void Sans_recouvrement_non_evaluable()
        => Assert.Equal(Verdict.NonEvaluable, new GabaritGraphique("x", Limite.Haute, [(1, 0), (2, 0)]).Evaluer([5, 6], [0, 0]).Verdict);

    [Fact] public void Abscisses_non_croissantes_refusees()
        => Assert.Throws<ArgumentException>(() => new GabaritGraphique("x", Limite.Haute, [(2, 0), (1, 0)]));
}
