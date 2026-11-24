using System.Text;
using Silx.Core.Circuits;
using Silx.Core.Netlist;
using Silx.Core.Raw;
using Silx.Core.Simulation;
using Xunit;

namespace Silx.Core.Tests;

public class CoreTests
{
    static Circuit Divider(string r2 = "1k") => new Circuit("diviseur")
        .V("V1", "in", "0", "DC 10").R("R1", "in", "out", "1k").R("R2", "out", "0", r2)
        .Also(c => c.Analysis = new OpAnalysis());

    static Circuit RcAc() => new Circuit("rc ac")
        .V("V1", "in", "0", "AC 1").R("R1", "in", "out", "1k").C("C1", "out", "0", "159.155n")
        .Also(c => c.Analysis = new AcAnalysis(AcSweep.Dec, 20, 10, 1e6));

    static Circuit RcTran() => new Circuit("rc tran")
        .V("V1", "in", "0", "PULSE(0 1 0 1n 1n 1 2)").R("R1", "in", "out", "1k").C("C1", "out", "0", "1u")
        .Also(c => c.Analysis = new TranAnalysis(10e-6, 5e-3));

    [Fact] public void Netlist_contient_analyse_et_end()
    {
        var n = NetlistWriter.Write(Divider());
        Assert.Contains("R1 in out 1k", n); Assert.Contains(".op", n); Assert.EndsWith(".end" + Environment.NewLine, n);
    }

    [Fact] public void Netlist_rejette_noeud_flottant_et_absence_de_masse_et_doublon()
    {
        var c = new Circuit("x").R("R1", "a", "b", "1k"); c.Analysis = new OpAnalysis();
        Assert.Throws<InvalidOperationException>(() => NetlistWriter.Write(c));
        Assert.Throws<ArgumentException>(() => Divider().R("r1", "out", "0", "1k"));
    }

    [Fact] public async Task Op_diviseur_donne_5V()
    {
        var r = await new NgspiceRunner().RunAsync(Divider());
        Assert.Equal(5.0, r.Raw.Vector("v(out)")[0], 9);
    }

    [Fact] public async Task Ac_rc_gain_a_fc_vaut_moins_3dB()
    {
        var r = (await new NgspiceRunner().RunAsync(RcAc())).Raw;
        Assert.True(r.IsComplex);
        var f = r.Vector("frequency"); var mag = r.Magnitude("v(out)");
        int i = Array.FindIndex(f, x => Math.Abs(x - 1000) < 1e-6 * 1000 || x >= 999.9);
        // interpolation log entre les 2 points entourant 1 kHz
        int k = Array.FindIndex(f, x => x >= 1000); double t = Math.Log(1000 / f[k - 1]) / Math.Log(f[k] / f[k - 1]);
        double g = mag[k - 1] + t * (mag[k] - mag[k - 1]);
        Assert.Equal(1 / Math.Sqrt(2), g, 3);
    }

    [Fact] public async Task Tran_rc_atteint_63pct_a_tau()
    {
        var r = (await new NgspiceRunner().RunAsync(RcTran())).Raw;
        var t = r.Vector("time"); var v = r.Vector("v(out)");
        int k = Array.FindIndex(t, x => x >= 1e-3);
        double y = v[k - 1] + (1e-3 - t[k - 1]) / (t[k] - t[k - 1]) * (v[k] - v[k - 1]);
        Assert.Equal(1 - Math.Exp(-1), y, 2);
    }

    [Fact] public async Task Parallele_renvoie_les_resultats_dans_l_ordre()
    {
        var circuits = Enumerable.Range(1, 24).Select(i => Divider($"{i}k")).ToList();
        var progress = System.Threading.Channels.Channel.CreateUnbounded<RunProgress>();
        var res = await new ParallelSimulator(new NgspiceRunner(), 4).RunAllAsync(circuits, progress.Writer);
        for (int i = 0; i < circuits.Count; i++)
        {
            double r2 = (i + 1) * 1000.0;
            Assert.Equal(10 * r2 / (1000 + r2), res[i]!.Raw.Vector("v(out)")[0], 8);
        }
        int n = 0; await foreach (var _ in progress.Reader.ReadAllAsync()) n++;
        Assert.Equal(24, n);
    }

    [Fact] public async Task Annulation_leve_OperationCanceled_et_nettoie()
    {
        var circuits = Enumerable.Range(0, 200).Select(_ => RcTran()).ToList();
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ParallelSimulator(new NgspiceRunner(), 2).RunAllAsync(circuits, null, cts.Token));
    }

    [Fact] public async Task Run_en_erreur_est_signale_sans_planter_le_lot()
    {
        var bad = new Circuit("bad").V("V1", "a", "0", "DC 1").Add(new Component("X1", ["a", "0"], "inconnu"));
        bad.Analysis = new OpAnalysis();
        var ch = System.Threading.Channels.Channel.CreateUnbounded<RunProgress>();
        var res = await new ParallelSimulator(new NgspiceRunner(), 2).RunAllAsync([Divider(), bad], ch.Writer);
        Assert.NotNull(res[0]); Assert.Null(res[1]);
        var all = new List<RunProgress>(); await foreach (var p in ch.Reader.ReadAllAsync()) all.Add(p);
        Assert.Contains(all, p => !p.Success && p.Index == 1);
    }

    [Fact] public void Threads_OpenMP_repartis_entre_les_runs_simultanes()
    {
        int n = Environment.ProcessorCount;
        Assert.Equal(1, new ParallelSimulator(new NgspiceRunner(), n).ThreadsPerRun);
        Assert.Equal(1, new ParallelSimulator(new NgspiceRunner(), 2 * n).ThreadsPerRun);
        Assert.Equal(n, new ParallelSimulator(new NgspiceRunner(), 1).ThreadsPerRun);
        Assert.Equal(3, new ParallelSimulator(new NgspiceRunner { NumThreads = 3 }, n).ThreadsPerRun);
    }

    [Fact] public async Task Run_avec_threads_fixes_reste_correct()
    {
        var r = await new NgspiceRunner { NumThreads = 2 }.RunAsync(Divider());
        Assert.Equal(5.0, r.Raw.Vector("v(out)")[0], 9);
    }

    [Fact] public void Raw_ascii_reel_et_tronque()
    {
        string s = "Title: t\nPlotname: DC\nFlags: real\nNo. Variables: 2\nNo. Points: 2\nVariables:\n\t0\tv-sweep\tvoltage\n\t1\tv(a)\tvoltage\nValues:\n 0\t0\n\t0\n 1\t1\n\t2\n";
        var r = RawFileReader.Read(Encoding.ASCII.GetBytes(s));
        Assert.Equal(new[] { 0.0, 1.0 }, r.Vector("v-sweep")); Assert.Equal(new[] { 0.0, 2.0 }, r.Vector("V(A)"));
        var bin = Encoding.ASCII.GetBytes("Title: t\nPlotname: x\nFlags: real\nNo. Variables: 1\nNo. Points: 5\nVariables:\n\t0\tt\ttime\nBinary:\n");
        Assert.Throws<InvalidDataException>(() => RawFileReader.Read(bin));
    }
}

static class Ext { public static T Also<T>(this T t, Action<T> a) { a(t); return t; } }
