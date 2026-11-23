using Silx.Mesures.Courbes;
using Silx.Mesures.Gabarits;
using Silx.Mesures.Mesures;
using Silx.Mesures.Tests.Reference;

namespace Silx.Mesures.Tests;

public class SpectralEtGabaritsTests
{
    static CourbeTemporelle Signal(double f0, double a2, double a3, double dc = 0, int periodesTotales = 14, int ptsPeriode = 400)
    {
        double T = 1 / f0;
        int n = periodesTotales * ptsPeriode;
        var t = Enumerable.Range(0, n + 1).Select(i => i * T / ptsPeriode).ToArray();
        return new(t, t.Select(x => dc + Math.Sin(2 * Math.PI * f0 * x) + a2 * Math.Sin(2 * Math.PI * 2 * f0 * x + 0.7)
                                      + a3 * Math.Sin(2 * Math.PI * 3 * f0 * x + 1.9)));
    }

    [Fact] public void Thd_harmoniques_connues()
        => Assert.Equal(Math.Sqrt(0.1 * 0.1 + 0.05 * 0.05), Spectral.Thd(Signal(1e3, 0.1, 0.05), 1e3), 1e-4);

    [Fact] public void Thd_sinus_pur_est_quasi_nulle()
        => Assert.True(Spectral.Thd(Signal(1e3, 0, 0), 1e3) < 1e-4);

    [Fact] public void Thd_insensible_a_l_offset_dc()
        => Assert.Equal(0.1, Spectral.Thd(Signal(1e3, 0.1, 0, dc: 3.0), 1e3), 1e-4);

    [Fact] public void Thd_sur_pas_de_temps_non_uniforme()
    {
        double f0 = 1e3;
        var t = Enumerable.Range(0, 20001).Select(i => 12 / f0 * (i / 20000.0 + 0.02 * Math.Sin(2 * Math.PI * i / 20000.0 * 37) / 37)).ToArray();
        var c = new CourbeTemporelle(t, t.Select(x => Math.Sin(2 * Math.PI * f0 * x) + 0.1 * Math.Sin(2 * Math.PI * 3 * f0 * x)));
        Assert.Equal(0.1, Spectral.Thd(c, f0), 2e-3);
    }

    [Fact] public void Thd_moins_d_une_periode_est_impossible()
    {
        var c = Signal(1e3, 0.1, 0, periodesTotales: 1);
        Assert.Throws<MesureImpossibleException>(() => Spectral.Thd(c, 100)); // 100 Hz : < 1 période en 1 ms
    }

    [Fact] public void Gabarit_pass_fail_sur_rc()
    {
        var f = Circuits.GrilleLog(1, 1e8, 200);
        var h = Circuits.RcAc(1e3, 1e-7, f); // fc ≈ 1591 Hz
        var passe = new Gabarit<ReponseFrequentielle>("BW >= 1 kHz", Catalogue.BandePassante3dB, Bornes.AuMoins(1e3));
        var echoue = new Gabarit<ReponseFrequentielle>("BW >= 2 kHz", Catalogue.BandePassante3dB, Bornes.AuMoins(2e3));
        Assert.Equal(Verdict.Pass, passe.Evaluer(h).Verdict);
        var r = echoue.Evaluer(h);
        Assert.Equal(Verdict.Fail, r.Verdict);
        Assert.InRange(r.Valeur!.Value, 1580, 1600);
    }

    [Fact] public void Gabarit_marge_de_phase_45_deg_sur_second_ordre()
    {
        var f = Circuits.GrilleLog(1, 1e8, 200);
        var g = new Gabarit<ReponseFrequentielle>("PM >= 45°", Catalogue.MargeDePhase, Bornes.AuMoins(45));
        Assert.Equal(Verdict.Pass, g.Evaluer(Circuits.SecondOrdreBoucle(10e3, 0.6, f)).Verdict); // PM ≈ 59,2°
        Assert.Equal(Verdict.Fail, g.Evaluer(Circuits.SecondOrdreBoucle(10e3, 0.3, f)).Verdict); // PM ≈ 33,3°
    }

    [Fact] public void Gabarit_non_evaluable_n_est_jamais_pass()
    {
        var f = Circuits.GrilleLog(1, 1e8, 50);
        var g = new Gabarit<ReponseFrequentielle>("GM >= 6 dB", Catalogue.MargeDeGain, Bornes.AuMoins(6));
        var r = g.Evaluer(Circuits.SecondOrdreBoucle(10e3, 0.5, f)); // phase jamais à -180°
        Assert.Equal(Verdict.NonEvaluable, r.Verdict);
        Assert.Null(r.Valeur);
        Assert.NotNull(r.Detail);
    }

    [Fact] public void Gabarit_bornes_inclusives_et_plage()
    {
        var m = new Mesure<double>("id", "u", x => x);
        var g = new Gabarit<double>("[1;2]", m, Bornes.Entre(1, 2));
        Assert.Equal(Verdict.Pass, g.Evaluer(1).Verdict);
        Assert.Equal(Verdict.Pass, g.Evaluer(2).Verdict);
        Assert.Equal(Verdict.Fail, g.Evaluer(2.0000001).Verdict);
        Assert.Equal(Verdict.NonEvaluable, g.Evaluer(double.NaN).Verdict);
    }

    [Fact] public void Gabarit_invalide_refuse()
    {
        var m = new Mesure<double>("id", "u", x => x);
        Assert.Throws<ArgumentException>(() => new Gabarit<double>("x", m, new Bornes(null, null)));
        Assert.Throws<ArgumentException>(() => new Gabarit<double>("x", m, Bornes.Entre(2, 1)));
    }

    [Fact] public void Verdict_global()
    {
        var m = new Mesure<double>("id", "u", x => x);
        var gs = new[] { new Gabarit<double>("a", m, Bornes.AuMoins(0)), new Gabarit<double>("b", m, Bornes.AuPlus(5)) };
        Assert.Equal(Verdict.Pass, Gabarits.Gabarits.Global(Gabarits.Gabarits.Evaluer(gs, 3)));
        Assert.Equal(Verdict.Fail, Gabarits.Gabarits.Global(Gabarits.Gabarits.Evaluer(gs, 7)));
        Assert.Equal(Verdict.NonEvaluable, Gabarits.Gabarits.Global(Gabarits.Gabarits.Evaluer(gs, double.NaN)));
    }

    [Fact] public void Courbes_invalides_refusees()
    {
        Assert.Throws<ArgumentException>(() => new CourbeTemporelle(new[] { 0.0, 0.0 }, new[] { 1.0, 2.0 }));
        Assert.Throws<ArgumentException>(() => new CourbeTemporelle(new[] { 0.0, 1.0 }, new[] { 1.0, double.NaN }));
        Assert.Throws<ArgumentException>(() => new ReponseFrequentielle(new[] { 0.0, 1.0 }, new[] { System.Numerics.Complex.One, System.Numerics.Complex.One }));
    }
}
