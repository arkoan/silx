using Silx.Etude;
using Silx.Mesures.Gabarits;
using Xunit;
namespace Silx.App.Tests;

public class CoreTests
{
    static string Line(Schematic s, string reference)
    {
        var c = Connectivity.Build(s);
        var k = s.Components.Single(x => x.Ref == reference);
        return $"{k.Ref} {string.Join(' ', k.Pins.Select(p => c.NodeOf[p]))} {k.Value}";
    }

    [Fact]
    public void Netlist_reconnait_jonctions_et_masse()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 0), Value = "1k" });
        s.Components.Add(new() { Ref = "R2", Kind = ComponentKind.Resistor, Position = new(8, 0), Value = "2k" });
        s.Wires.Add(new(new(4, 0), new(8, 0)));
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(12, 0) });
        var c = Connectivity.Build(s);
        Assert.Equal(c.NodeOf[new GridPoint(4, 0)], c.NodeOf[new GridPoint(8, 0)]);
        Assert.Equal("0", c.NodeOf[new GridPoint(12, 0)]);
        Assert.Equal("R1 N001 N002 1k", Line(s, "R1"));
        Assert.Equal("R2 N002 0 2k", Line(s, "R2"));
    }

    [Fact]
    public void Erc_detecte_pas_de_masse_et_broche_flottante_et_doublon()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 0), Value = "1k" });
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 8), Value = "1k" });
        var issues = Erc.Check(s, Connectivity.Build(s));
        Assert.Contains(issues, i => i.Message.Contains("dupliquée"));
        Assert.Contains(issues, i => i.Message.Contains("Aucune masse"));
        Assert.Contains(issues, i => i.Message.Contains("non connectée"));
    }

    [Fact]
    public void Jonction_en_T_connecte_broche_sur_fil()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 0), Value = "1k" });
        s.Components.Add(new() { Ref = "R2", Kind = ComponentKind.Resistor, Position = new(2, 4), Rotation = 3, Value = "1k" }); // (2,4),(2,0)
        s.Wires.Add(new(new(0, 0), new(4, 0)));
        var c = Connectivity.Build(s);
        Assert.Equal(c.NodeOf[new GridPoint(2, 0)], c.NodeOf[new GridPoint(0, 0)]);
    }

    [Fact]
    public void Etiquettes_nomment_et_relient_les_noeuds()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 0), Value = "1k" });
        s.Components.Add(new() { Ref = "R2", Kind = ComponentKind.Resistor, Position = new(0, 10), Value = "1k" });
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(4, 0), Value = "OUT" });
        s.Components.Add(new() { Ref = "NET2", Kind = ComponentKind.Label, Position = new(0, 10), Value = "OUT" });
        var c = Connectivity.Build(s);
        Assert.Equal("OUT", c.NodeOf[new GridPoint(4, 0)]);
        Assert.Equal("OUT", c.NodeOf[new GridPoint(0, 10)]);
        Assert.Equal(["OUT"], c.NamedNodes);
    }

    [Theory]
    [InlineData("4,7k", 4700)] [InlineData("10meg", 1e7)] [InlineData("2.5 ms", 2.5e-3)] [InlineData("150Hz", 150)] [InlineData("1e-3", 1e-3)] [InlineData("100n", 1e-7)]
    public void Nombres_notation_ingenieur(string text, double expected)
    {
        Assert.True(SpiceNumber.TryParse(text, out var v));
        Assert.Equal(expected, v, expected * 1e-12);
    }

    [Fact]
    public void Format_ingenieur_avec_virgule() => Assert.Equal("2,20 ms", SpiceNumber.Format(2.2e-3, "s"));

    static Banc Banc(Silx.Etude.Etude e, string debut) => e.Bancs.First(b => b.Nom.StartsWith(debut));

    /// <summary>RC 1 kΩ / 1 µF : tau = 1 ms → montée 10–90 % = 2,197 ms ; fc = 159,15 Hz (charge 100 kΩ : +1 %).</summary>
    [Fact]
    public async Task Demo_rc_les_deux_bancs_passent_avec_valeurs_analytiques()
    {
        var e = Silx.Etude.Etude.Demo();
        var r = await Verification.RunAsync(e, Banc(e, "Échelon"), [AnalyseKind.Transitoire]);
        Assert.Null(r.Erreur);
        Assert.Equal(Verdict.Pass, r.Global);
        Assert.InRange(r.Criteres.Single(c => c.Critere.Mesure == TypeMesure.TempsDeMontee).Resultat.Valeur!.Value, 2.15e-3, 2.25e-3);
        var rb = await Verification.RunAsync(e, Banc(e, "Réponse"), []);
        Assert.Equal(Verdict.Pass, rb.Global);
        Assert.InRange(rb.Criteres.Single(c => c.Critere.Mesure == TypeMesure.BandePassante3dB).Resultat.Valeur!.Value, 157, 163);
    }

    [Fact]
    public void Circuit_seul_sans_source_ni_masse_n_est_pas_en_erreur_mais_le_banc_le_complete()
    {
        var e = Silx.Etude.Etude.Demo();
        var c = Connectivity.Build(e.Schema);
        Assert.DoesNotContain(Erc.Check(e.Schema, c, circuitSeul: true), i => i.Severity == Severity.Error);
        var fusion = e.Bancs[0].Fusion(e.Schema);
        Assert.Contains(fusion.Components, k => k.Ref == "V1");
        Assert.DoesNotContain(e.Schema.Components, k => k.Ref == "V1");   // le stimulus n'appartient qu'au banc
        Assert.Empty(Erc.Check(fusion, Connectivity.Build(fusion)));
    }

    [Fact]
    public async Task Modifier_le_circuit_se_repercute_sur_tous_les_bancs()
    {
        var e = Silx.Etude.Etude.Demo();
        e.Schema.Components.Single(k => k.Ref == "C1").Value = "10u";     // tau x10
        var r1 = await Verification.RunAsync(e, Banc(e, "Échelon"), []);
        var r2 = await Verification.RunAsync(e, Banc(e, "Réponse"), []);
        Assert.Equal(Verdict.Fail, r1.Global);
        Assert.Equal(Verdict.Fail, r2.Global);
    }

    [Fact]
    public async Task Gabarits_dessines_de_la_demo_passent_avec_marge_en_fin_de_courbe()
    {
        var e = Silx.Etude.Etude.Demo();
        var r = await Verification.RunAsync(e, Banc(e, "Échelon"), []);
        var basse = r.Traces.Single(t => t.Gabarit.Limite == Limite.Basse).Resultat;
        Assert.Equal(Verdict.Pass, basse.Verdict);
        Assert.InRange(basse.Marge!.Value, 0.03, 0.06);          // au plus près vers 6 ms : 1 − e^-5,5 − 0,95 ≈ 0,046 V
        Assert.Equal(Verdict.Pass, r.Traces.Single(t => t.Gabarit.Limite == Limite.Haute).Resultat.Verdict);
    }

    [Fact]
    public async Task Un_banc_frequentiel_ne_lance_que_l_analyse_ac()
    {
        var e = Silx.Etude.Etude.Demo(); var b = Banc(e, "Réponse");
        b.Gabarits.Add(new() { Mesure = TypeMesure.TempsDeMontee, Noeud = "OUT", Max = 1e-9 });   // critère temporel : ignoré
        var r = await Verification.RunAsync(e, b, []);
        Assert.Equal([AnalyseKind.Ac], r.Resultats.Keys);
        Assert.DoesNotContain(r.Criteres, c => c.Critere.Mesure == TypeMesure.TempsDeMontee);
        Assert.Equal(Verdict.Pass, r.Global);
    }

    [Fact]
    public async Task Gabarit_dessine_franchi_echoue()
    {
        var e = Silx.Etude.Etude.Demo(); var b = Banc(e, "Échelon");
        b.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Transitoire, Noeud = "OUT", Limite = Limite.Basse, Points = [(1e-3, 0.9), (5e-3, 0.9)] });
        Assert.Equal(Verdict.Fail, (await Verification.RunAsync(e, b, [])).Global);
    }

    [Fact]
    public async Task Diode_redresse()
    {
        var e = new Silx.Etude.Etude(); var banc = new Banc();
        var s = e.Schema;
        s.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(0, 4), Rotation = 3, Value = "SIN(0 5 1k)" }); // + en (0,0)
        s.Components.Add(new() { Ref = "D1", Kind = ComponentKind.Diode, Position = new(0, 0), Value = "1N4148" });
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(4, 0), Rotation = 1, Value = "1k" }); // (4,0)-(4,4)
        s.Wires.Add(new(new(0, 4), new(4, 4)));
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(0, 4) });
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(4, 0), Value = "OUT" });
        banc.Transitoire.Duree = 3e-3; banc.Transitoire.Pas = 5e-6;
        var r = await Verification.RunAsync(e, banc, [AnalyseKind.Transitoire]);
        Assert.Null(r.Erreur);
        var v = r.Resultats[AnalyseKind.Transitoire].Vector("v(out)");
        Assert.True(v.Min() > -0.05);          // demi-alternance négative bloquée
        Assert.InRange(v.Max(), 4.1, 4.5);     // 5 V − ~0,7 V de seuil
    }
}
