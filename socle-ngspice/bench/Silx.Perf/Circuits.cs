using System.Globalization;
using Silx.Core.Circuits;

namespace Silx.Perf;

/// <summary>Une famille de circuits de taille paramétrable, représentative d'un usage en bureau d'étude.</summary>
public sealed record Famille(string Nom, string Description, string Unite, int[] Tailles, int[] TaillesRapides, Func<int, int, Circuit> Fabrique)
{
    /// <param name="variante">fait varier une valeur (comme une campagne paramétrique) sans changer la taille.</param>
    public Circuit Creer(int taille, int variante = 0) => Fabrique(taille, variante);
}

public static class Circuits
{
    static string F(double d) => d.ToString("G6", CultureInfo.InvariantCulture);

    /// <summary>RC du 1er ordre, ~500 points : le temps est dominé par le coût fixe (process + fichiers).</summary>
    public static Circuit RcSimple(int _, int v) => new Circuit("rc simple")
        .V("V1", "in", "0", "PULSE(0 1 0 1n 1n 1 2)").R("R1", "in", "out", F(1000 + v)).C("C1", "out", "0", "1u")
        .Also(c => { c.Analysis = new TranAnalysis(10e-6, 5e-3); c.Saves.Add("v(out)"); });

    /// <summary>Échelle RC de N cellules (ligne, filtre d'ordre N) : croissance du nombre de nœuds seul.</summary>
    public static Circuit EchelleRc(int n, int v)
    {
        var c = new Circuit($"echelle rc {n}").V("V1", "n0", "0", "PULSE(0 1 0 1u 1u 5m 10m)");
        for (int i = 0; i < n; i++)
        {
            c.R($"R{i}", $"n{i}", $"n{i + 1}", F(100 + v * 0.1));
            c.C($"C{i}", $"n{i + 1}", "0", "10n");
        }
        c.R("Rload", $"n{n}", "0", "1meg");
        c.Analysis = new TranAnalysis(5e-6, 10e-3);   // ~2000 points
        c.Saves.Add($"v(n{n})");
        return c;
    }

    /// <summary>N étages émetteur commun 2N2222 (gain ~ -1, couplage capacitif) : modèle non linéaire Gummel-Poon.</summary>
    public static Circuit ChaineBjt(int n, int v)
    {
        var c = new Circuit($"chaine bjt {n}")
            .V("Vcc", "vcc", "0", "DC 12")
            .V("Vin", "in", "0", $"SIN(0 {F(0.1 + v * 1e-4)} 1k)")
            .Directive(".model Q2N2222 NPN(IS=14.34f XTI=3 EG=1.11 VAF=74.03 BF=255.9 NE=1.307 ISE=14.34f IKF=.2847 XTB=1.5 BR=6.092 NC=2 ISC=0 IKR=0 RC=1 CJC=7.306p MJC=.3416 VJC=.75 FC=.5 CJE=22.01p MJE=.377 VJE=.75 TR=46.91n TF=411.1p ITF=.6 VTF=1.7 XTF=3 RB=10)");
        string prev = "in";
        for (int i = 0; i < n; i++)
        {
            c.C($"Cc{i}", prev, $"b{i}", "10u");
            c.R($"Ra{i}", "vcc", $"b{i}", "47k").R($"Rb{i}", $"b{i}", "0", "22k");
            c.R($"Rc{i}", "vcc", $"c{i}", "2.2k").R($"Re{i}", $"e{i}", "0", "2.2k");
            c.Add(new Component($"Q{i}", [$"c{i}", $"b{i}", $"e{i}"], "Q2N2222"));
            prev = $"c{i}";
        }
        c.C("Cout", prev, "out", "10u").R("Rl", "out", "0", "10k");
        c.Analysis = new TranAnalysis(2e-6, 2e-3);   // 2 périodes, ~1000 points
        c.Saves.Add("v(out)");
        return c;
    }

    /// <summary>Chaîne de N inverseurs CMOS BSIM4 (level=54) : modèle MOS moderne, seul cas où ngspice utilise OpenMP.</summary>
    public static Circuit ChaineCmos(int n, int v)
    {
        var c = new Circuit($"chaine cmos {n}")
            .V("Vdd", "vdd", "0", "DC 1.0")
            .V("Vin", "n0", "0", "PULSE(0 1 0 50p 50p 1n 2n)")
            .Directive(".model nch nmos level=54").Directive(".model pch pmos level=54");
        for (int i = 0; i < n; i++)
        {
            c.Add(new Component($"MP{i}", [$"n{i + 1}", $"n{i}", "vdd", "vdd"], "pch", "W=2u L=0.1u"));
            c.Add(new Component($"MN{i}", [$"n{i + 1}", $"n{i}", "0", "0"], "nch", "W=1u L=0.1u"));
            c.C($"C{i}", $"n{i + 1}", "0", F(2e-15 + v * 1e-18));
        }
        c.R("Rload", $"n{n}", "0", "1meg");
        c.Analysis = new TranAnalysis(10e-12, 20e-9);
        c.Saves.Add($"v(n{n})");
        return c;
    }

    /// <summary>
    /// Mixte XSPICE : horloge analogique → adc_bridge → compteur asynchrone de N bascules D,
    /// chaque bit repassant en analogique (dac_bridge + RC). Couplage analogique/numérique sur chaque bit.
    /// </summary>
    public static Circuit CompteurXspice(int n, int v)
    {
        var c = new Circuit($"compteur xspice {n}")
            .V("Vck", "ck", "0", $"PULSE(0 5 0 {F(100e-9 + v * 1e-10)} 100n 4.9u 10u)")
            .R("Rck", "ck", "0", "1meg")
            .Add(new Component("Aadc", ["[", "ck", "]", "[", "d_ck", "]"], "adc1"))
            .Directive(".model adc1 adc_bridge(in_low=1.5 in_high=3.5 rise_delay=1n fall_delay=1n)")
            .Directive(".model dac1 dac_bridge(out_low=0 out_high=5 out_undef=2.5 t_rise=10n t_fall=10n)")
            .Directive(".model dff1 d_dff(clk_delay=10n set_delay=10n reset_delay=10n ic=0)");
        string clk = "d_ck";
        for (int i = 0; i < n; i++)
        {
            // Bascule montée en T : D = /Q ; l'horloge de l'étage suivant est /Q (comptage).
            c.Add(new Component($"Aff{i}", [$"d_nq{i}", clk, "NULL", "NULL", $"d_q{i}", $"d_nq{i}"], "dff1"));
            c.Add(new Component($"Adac{i}", ["[", $"d_q{i}", "]", "[", $"a{i}", "]"], "dac1"));
            c.R($"Ra{i}", $"a{i}", $"f{i}", "10k").C($"Ca{i}", $"f{i}", "0", "100p");
            clk = $"d_nq{i}";
        }
        c.Analysis = new TranAnalysis(100e-9, 2e-3);   // 200 cycles d'horloge
        c.Saves.Add($"v(f{n - 1})");
        return c;
    }

    public static readonly Famille[] Familles =
    [
        new("rc", "RC 1er ordre, 500 points (coût fixe)", "cellule", [1], [1], RcSimple),
        new("echelle_rc", "Échelle RC de N cellules, 2000 points", "cellules", [10, 100, 1000, 5000, 20000], [10, 100, 1000, 5000], EchelleRc),
        new("bjt", "N étages émetteur commun 2N2222", "étage(s)", [1, 10, 50, 200], [1, 10, 50], ChaineBjt),
        new("cmos", "N inverseurs CMOS BSIM4", "inverseurs", [10, 50, 200, 500], [10, 50, 200], ChaineCmos),
        new("xspice", "Compteur XSPICE de N bascules + N DAC/RC", "bascules", [4, 16, 64, 256], [4, 16, 64], CompteurXspice),
    ];

    public static Famille Famille(string nom) => Familles.Single(f => f.Nom == nom);
}

public static class Ext { public static T Also<T>(this T t, Action<T> a) { a(t); return t; } }
