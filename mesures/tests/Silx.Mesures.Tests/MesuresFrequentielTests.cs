using Silx.Mesures.Mesures;
using Silx.Mesures.Tests.Reference;

namespace Silx.Mesures.Tests;

public class MesuresFrequentielTests
{
    static readonly double[] Grille = Circuits.GrilleLog(1, 1e8, 200);

    [Fact] public void Rc_gain_dc_et_bande_passante()
    {
        double r = 1e3, c = 1e-7; // fc = 1591,549 Hz
        var h = Circuits.RcAc(r, c, Grille);
        Assert.Equal(1.0, Frequentiel.GainDc(h), 6);
        double fc = Circuits.RcFc(r, c);
        Assert.Equal(fc, Frequentiel.BandePassante3dB(h), fc * 2e-3); // 0,2 % : interpolation linéaire sur grille 200 pts/décade
    }

    [Fact] public void Rc_gain_dc_different_de_un_la_bande_est_relative_au_gain_dc()
    {
        double fc = 1000;
        var f = Grille;
        var h = new Courbes.ReponseFrequentielle(f, f.Select(x => 10.0 / (1 + System.Numerics.Complex.ImaginaryOne * x / fc)));
        Assert.Equal(10.0, Frequentiel.GainDc(h), 4); // |H(1 Hz)| = 10/√(1+1e-6) : le « DC » est le plus bas point de la grille
        Assert.Equal(fc, Frequentiel.BandePassante3dB(h), fc * 2e-3);
    }

    [Theory]
    [InlineData(0.2)] [InlineData(0.5)] [InlineData(0.707)] [InlineData(1.0)]
    public void SecondOrdre_bande_passante_analytique(double zeta)
    {
        double fn = 10e3;
        var h = Circuits.SecondOrdreAc(fn, zeta, Grille);
        double attendu = Circuits.SecondOrdreBandePassante(fn, zeta);
        Assert.Equal(attendu, Frequentiel.BandePassante3dB(h), attendu * 3e-3);
    }

    [Theory]
    [InlineData(0.1)] [InlineData(0.3)] [InlineData(0.5)] [InlineData(0.707)] [InlineData(1.0)]
    public void SecondOrdre_marge_de_phase_analytique(double zeta)
    {
        var l = Circuits.SecondOrdreBoucle(10e3, zeta, Grille);
        Assert.Equal(Circuits.SecondOrdreMargePhaseDeg(zeta), Frequentiel.MargeDePhase(l), 0.05); // 0,05°
    }

    [Fact] public void SecondOrdre_marge_de_phase_zeta_0_707_vaut_65_5_deg()
        => Assert.Equal(65.5, Frequentiel.MargeDePhase(Circuits.SecondOrdreBoucle(10e3, Math.Sqrt(0.5), Grille)), 0.1);

    [Fact] public void TroisiemeOrdre_marge_de_gain_et_de_phase_analytiques()
    {
        double k = 4, f0 = 1e3;
        var l = Circuits.TroisiemeOrdreBoucle(k, f0, Grille);
        // phase = -180° à f = √3·f0 où |L| = K/8  =>  GM = 20·log10(8/K)
        double gm = Frequentiel.MargeDeGain(l, out double f180);
        Assert.Equal(20 * Math.Log10(8 / k), gm, 0.02);
        Assert.Equal(Math.Sqrt(3) * f0, f180, Math.Sqrt(3) * f0 * 3e-3);
        // gain unité : |L|=1 à f0·√(K^(2/3)-1) ; PM = 180 - 3·atan(f/f0)
        double fu = f0 * Math.Sqrt(Math.Pow(k, 2.0 / 3) - 1);
        double pm = Frequentiel.MargeDePhase(l, out double fAtteinte);
        Assert.Equal(180 - 3 * Math.Atan(fu / f0) * 180 / Math.PI, pm, 0.1);
        Assert.Equal(fu, fAtteinte, fu * 3e-3);
    }

    [Fact] public void Phase_deroulee_traverse_plusieurs_tours_sans_saut()
    {
        // 3 pôles : la phase brute (Arg) saute de -180 à +180 ; la déroulée doit rester monotone jusqu'à -270°.
        var l = Circuits.TroisiemeOrdreBoucle(1, 1e3, Grille);
        var ph = Frequentiel.PhaseDeroulee(l.Valeurs);
        for (int i = 1; i < ph.Length; i++) Assert.True(ph[i] <= ph[i - 1] + 1e-9);
        Assert.Equal(-270, ph[^1], 0.1);
    }

    [Fact] public void Marge_de_gain_impossible_si_phase_ne_traverse_pas_moins_180()
        => Assert.Throws<MesureImpossibleException>(() => Frequentiel.MargeDeGain(Circuits.SecondOrdreBoucle(10e3, 0.5, Grille)));

    [Fact] public void Marge_de_phase_impossible_si_gain_toujours_sous_0dB()
    {
        var l = Circuits.RcAc(1e3, 1e-7, Grille); // |H| ≤ 1, démarre à 0 dB : jamais > 0 dB
        Assert.Throws<MesureImpossibleException>(() => Frequentiel.MargeDePhase(l));
    }

    [Fact] public void Bande_passante_impossible_si_pas_de_coupure()
    {
        var f = new[] { 1.0, 10, 100 };
        var h = new Courbes.ReponseFrequentielle(f, f.Select(_ => System.Numerics.Complex.One));
        Assert.Throws<MesureImpossibleException>(() => Frequentiel.BandePassante3dB(h));
    }
}
