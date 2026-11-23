using Silx.Mesures.Mesures;
using Silx.Mesures.Tests.Reference;

namespace Silx.Mesures.Tests;

public class MesuresTransitoireTests
{
    const double R = 1e3, C = 1e-6, Tau = R * C; // τ = 1 ms

    static Courbes.CourbeTemporelle Rc(double amp = 1.0, int n = 4000)
        => Circuits.RcEchelon(R, C, amp, Circuits.GrilleTempsNonUniforme(12 * Tau, n));

    [Fact] public void Rc_valeur_finale_et_gain_dc()
    {
        var c = Rc(2.0);
        Assert.Equal(2.0, Transitoire.ValeurFinale(c), 3);          // e^-11.4 ≈ 1e-5 de l'échelon
        Assert.Equal(1.0, Transitoire.GainDc(c, 2.0), 3);
    }

    [Fact] public void Rc_temps_de_montee_10_90_vaut_tau_ln9()
        => Assert.Equal(Tau * Math.Log(9), Transitoire.TempsDeMontee(Rc()), precision: 6); // 2,197 ms ± 5e-7 s

    [Fact] public void Rc_temps_etablissement_2pct_vaut_tau_ln50()
        => Assert.Equal(Tau * Math.Log(50), Transitoire.TempsEtablissement(Rc()), precision: 5);

    [Fact] public void Rc_pas_de_depassement()
        => Assert.Equal(0.0, Transitoire.Depassement(Rc()), 3); // biais ≈ 2e-4 % : valeur finale = moyenne des 5 % finaux, pas la dernière valeur

    [Fact] public void Rc_echelon_descendant_symetrique()
    {
        var t = Circuits.GrilleTempsNonUniforme(12 * Tau, 4000);
        var c = new Courbes.CourbeTemporelle(t, t.Select(x => 5 * Math.Exp(-x / Tau)));
        Assert.Equal(Tau * Math.Log(9), Transitoire.TempsDeMontee(c), 6);
        Assert.Equal(Tau * Math.Log(50), Transitoire.TempsEtablissement(c), 5);
    }

    [Theory]
    [InlineData(0.2)] [InlineData(0.5)] [InlineData(0.7)]
    public void SecondOrdre_depassement_analytique(double zeta)
    {
        double fn = 1e3;
        var c = Circuits.SecondOrdreEchelon(fn, zeta, Circuits.GrilleTempsNonUniforme(30 / (zeta * 2 * Math.PI * fn), 20000));
        Assert.Equal(Circuits.SecondOrdreDepassementPct(zeta), Transitoire.Depassement(c), 0.02); // tolérance 0,02 point de %
    }

    [Theory]
    [InlineData(0.3)] [InlineData(0.5)] [InlineData(0.8)]
    public void SecondOrdre_temps_etablissement_coherent_avec_la_bande(double zeta)
    {
        double fn = 1e3, wn = 2 * Math.PI * fn;
        var c = Circuits.SecondOrdreEchelon(fn, zeta, Circuits.GrilleTempsNonUniforme(40 / (zeta * wn), 40000));
        double ts = Transitoire.TempsEtablissement(c);
        // Référence indépendante : balayage fin de la formule analytique.
        double dernierHors = 0;
        for (double t = 0; t < 20 / (zeta * wn); t += 1e-7)
            if (Math.Abs(Circuits.SecondOrdreEchelonAt(fn, zeta, t) - 1) > 0.02) dernierHors = t;
        Assert.Equal(dernierHors, ts, 5e-5 * 1 + 1e-5); // ± 60 µs sur ~ms : pas d'échantillonnage / interpolation linéaire
    }

    [Fact] public void Signal_non_etabli_est_impossible()
    {
        var t = Enumerable.Range(0, 200).Select(i => i * 1e-5).ToArray();
        var c = new Courbes.CourbeTemporelle(t, t.Select(x => 1 - Math.Exp(-x / 1e-3))); // 2 ms = 2τ : pas établi
        Assert.Throws<MesureImpossibleException>(() => Transitoire.TempsEtablissement(c));
    }

    [Fact] public void Signal_plat_n_a_pas_d_echelon()
    {
        var c = new Courbes.CourbeTemporelle(new[] { 0.0, 1, 2 }, new[] { 1.0, 1, 1 });
        Assert.Throws<MesureImpossibleException>(() => Transitoire.Depassement(c));
        Assert.Throws<MesureImpossibleException>(() => Transitoire.TempsDeMontee(c));
    }

    [Fact] public void Offset_moyenne_sur_fenetre()
    {
        var t = Enumerable.Range(0, 1001).Select(i => i * 1e-3).ToArray();
        var c = new Courbes.CourbeTemporelle(t, t.Select(x => 0.25 + 0.1 * x)); // moyenne sur [0,1] = 0,30
        Assert.Equal(0.30, Transitoire.Offset(c, 0, 1), 9);
        Assert.Equal(0.25 + 0.1 * (0.0005 + 0.5005) / 2, Transitoire.Offset(c, 0.0005, 0.5005), 9); // fenêtre non alignée sur la grille
    }
}
