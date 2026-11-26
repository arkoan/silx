using Silx.Etude;
using Silx.Mesures.Courbes;
using Xunit;
namespace Silx.App.Tests;

/// <summary>Nouveaux stimuli (rampe, PWL, PRBS, bruit), nouvelles mesures (efficace, moyenne, THD), critères sur courants, exports CSV.</summary>
public class StimuliMesuresTests
{
    [Fact]
    public void Prbs7_127_bits_paliers_et_temps_croissants()
    {
        var s = Stimulus.Nouveau(FormeStimulus.Prbs); s["v1"] = 0; s["v2"] = 5; s["debit"] = 1e3; s["tr"] = 1e-6;
        var p = s.PointsPrbs();
        Assert.True(p.Zip(p.Skip(1)).All(x => x.Second.T > x.First.T));
        Assert.InRange(p[^1].T, 0.126, 0.128);   // 127 bits de 1 ms
        // Niveau de chaque bit (au milieu du bit) : PRBS7 = 64 « 1 » et 63 « 0 ».
        double V(double t) { int i = p.FindLastIndex(q => q.T <= t); return p[i].V; }
        var bits = Enumerable.Range(0, 127).Select(i => V((i + 0.5) * 1e-3)).ToList();
        Assert.Equal(64, bits.Count(b => b == 5)); Assert.Equal(63, bits.Count(b => b == 0));
        Assert.StartsWith("PWL(", s.VersNetlist());
        Assert.Equal(FormeStimulus.Prbs, Stimulus.Lire(s.VersSpice()).Forme);
    }

    [Theory]
    [InlineData("temps;valeur\n0;0\n0,001;3,3\n0,002;0", 3)]
    [InlineData("t\tv\n0\t0\n1m\t5\n", 2)]
    [InlineData("# commentaire\n0 0\n1e-3 -2.5\n2e-3 1\n", 3)]
    [InlineData("0,0\n1,5\n", 2)]
    [InlineData("0;0\n1m;1\n0,5m;2\n", 2)]   // temps non croissant écarté
    public void Lecture_pwl_csv(string texte, int n) => Assert.Equal(n, Stimulus.LirePwlCsv(texte).Count);

    [Fact]
    public void Forme_d_onde_exportee_se_relit_en_pwl()
    {
        var s = Stimulus.Lire("PULSE(0 1 0.5m 1u 1u 2m 4m)");
        var csv = ExportDonnees.Stimulus(s, 10e-3, false)!;
        Assert.StartsWith("temps (s);valeur (V)", csv);
        var pts = Stimulus.LirePwlCsv(csv);
        Assert.InRange(pts.Count, 1500, 2100);
        Assert.Equal(1, pts.First(p => p.T > 1e-3 && p.T < 2e-3).V, 6);
        Assert.Null(ExportDonnees.Stimulus(Stimulus.Nouveau(FormeStimulus.Bruit), 10e-3, false));
    }

    static CourbeTemporelle Signal(Func<double, double> f, double duree = 20e-3, int n = 40000) =>
        new(Enumerable.Range(0, n).Select(i => duree * i / (n - 1)).ToArray(), Enumerable.Range(0, n).Select(i => f(duree * i / (n - 1))).ToArray());

    [Fact]
    public void Efficace_moyenne_et_thd()
    {
        var sinus = Signal(t => 1 + 2 * Math.Sin(2 * Math.PI * 1e3 * t));
        Assert.Equal(1, MesuresEtablies.Moyenne(sinus), 2);
        Assert.Equal(Math.Sqrt(1 + 2), MesuresEtablies.Efficace(sinus), 2);   // √(DC² + A²/2)
        Assert.InRange(MesuresEtablies.Thd(sinus), 0, 0.05);
        var distordu = Signal(t => Math.Sin(2 * Math.PI * 1e3 * t) + 0.1 * Math.Sin(2 * Math.PI * 3e3 * t) + 0.05 * Math.Sin(2 * Math.PI * 5e3 * t));
        Assert.Equal(100 * Math.Sqrt(0.01 + 0.0025), MesuresEtablies.Thd(distordu), 1);
        Assert.Throws<Silx.Mesures.MesureImpossibleException>(() => MesuresEtablies.Thd(Signal(t => t)));
    }

    /// <summary>Rampe lente sur le RC de démo : en régime établi, le courant dans R1 vaut C·dV/dt = 1 µF × 1 V / 20 ms = 50 µA.</summary>
    [Fact]
    public async Task Rampe_simulee_et_critere_sur_courant()
    {
        var e = Silx.Etude.Etude.Demo(); var b = e.Bancs[0];
        b.Calque.Components.Single(k => k.Ref == "V1").Value = "PWL(0 0 0 0 20m 1)";
        b.Gabarits.Clear(); b.GabaritsTraces.Clear();
        b.Gabarits.Add(new() { Mesure = TypeMesure.ValeurMoyenne, Noeud = "I(R1:1)", Min = 45e-6, Max = 55e-6 });
        var r = await Verification.RunAsync(e, b, []);
        Assert.Null(r.Erreur);
        var c = Assert.Single(r.Criteres).Resultat;
        Assert.Equal("A", c.Unite);
        Assert.Equal(Silx.Mesures.Gabarits.Verdict.Pass, c.Verdict);

        var csv = ExportDonnees.Courbes(AnalyseKind.Transitoire, r.Resultats[AnalyseKind.Transitoire], ["OUT", "I(R1:1)", "ABSENT"], b.Fusion(e.Schema));
        var lignes = csv.TrimEnd().Split('\n');
        Assert.Equal("temps (s);V(OUT) (V);I(R1) (A)", lignes[0]);
        Assert.True(lignes.Length > 50);
        Assert.Equal(3, lignes[^1].Split(';').Length);
        var m = ExportDonnees.Mesures(b, r, null, b.Fusion(e.Schema));
        Assert.Contains("Valeur moyenne (régime établi);I(R1);4,5E-05;5,5E-05;", m);
        Assert.Contains(";A;conforme;", m);
    }

    [Theory]
    [InlineData("PRBS(0 1 2k 0 1u)")]
    [InlineData("DC 0.5 TRNOISE(50m 10u 0 0)")]
    public async Task Prbs_et_bruit_simules_par_ngspice(string valeur)
    {
        var e = Silx.Etude.Etude.Demo(); var b = e.Bancs[0];
        b.Calque.Components.Single(k => k.Ref == "V1").Value = valeur;
        var r = await Verification.RunAsync(e, b, []);
        Assert.Null(r.Erreur);
        var vin = r.Resultats[AnalyseKind.Transitoire].Vector("v(in)");
        Assert.True(vin.Max() - vin.Min() > 0.1, "le stimulus varie");
    }

    [Fact]
    public async Task Export_mesures_d_une_campagne()
    {
        var e = Silx.Etude.Etude.Demo(); var b = e.Bancs[0];
        b.Campagne = new Campagne { Mode = ModeCampagne.Balayage, Cible = "R1", Debut = 1e3, Fin = 2e3, Points = 2 };
        var rc = await Campagnes.RunAsync(e, b, ["OUT"]);
        var m = ExportDonnees.Mesures(b, null, rc, b.Fusion(e.Schema));
        Assert.Contains("mesure;signal;N;moyenne", m);
        Assert.Contains("tirage;libellé;verdict;R1", m);
        Assert.Equal(2, m.Split('\n').Count(l => l.StartsWith("0;") || l.StartsWith("1;")));
    }
}
