using Silx.Etude;
using Silx.Mesures.Gabarits;
using Xunit;
namespace Silx.App.Tests;

/// <summary>Import de modèles SPICE, définitions de composants (symbole + correspondance des broches) et simulations ngspice réelles.</summary>
public class SpiceTests
{
    public const string Bibliotheque = """
        * Petite bibliothèque d'essai
        .model Q2N2222 NPN(IS=14.34f XTI=3 EG=1.11 VAF=74.03 BF=255.9 NE=1.307
        + ISE=14.34f IKF=.2847 XTB=1.5 BR=6.092 NC=2 ISC=0 IKR=0 RC=1
        + CJC=7.306p MJC=.3416 VJC=.75 FC=.5 CJE=22.01p MJE=.377 VJE=.75   ; capacités
        + TR=46.91n TF=411.1p ITF=.6 VTF=1.7 XTF=3 RB=10)
        .MODEL BS170 nmos (Level=1 Vto=1.8 Kp=0.1 Lambda=0.01)
        .model DAND d_and(rise_delay=1n)
        * Filtre RC en sous-circuit
        .subckt RCF IN OUT GND params: R=1k C=1u
        R1 IN OUT {R}
        C1 OUT GND {C}
        .ends RCF
        """;

    static DefinitionComposant Def(string nom) => DefinitionComposant.Depuis(ModelesSpice.Analyser(Bibliotheque).Modeles.Single(m => m.Nom == nom));

    [Fact]
    public void Analyse_model_subckt_continuations_et_types_non_pris_en_charge()
    {
        var a = ModelesSpice.Analyser(Bibliotheque);
        Assert.Equal(["Q2N2222", "BS170", "RCF"], a.Modeles.Select(m => m.Nom));
        var q = a.Modeles[0];
        Assert.Equal(TypeModele.Npn, q.Type); Assert.Equal("Q", q.Lettre); Assert.Equal(["C", "B", "E"], q.Broches);
        Assert.Contains("+ TR=46.91n", q.Texte);                       // texte d'origine recopié, continuations comprises
        Assert.Equal(TypeModele.Nmos, a.Modeles[1].Type);             // casse ignorée
        var x = a.Modeles[2];
        Assert.Equal(["IN", "OUT", "GND"], x.Broches); Assert.Equal("R=1k C=1u", x.Parametres);
        Assert.EndsWith(".ends RCF", x.Texte.Trim());
        Assert.Contains(a.Avertissements, w => w.Contains("DAND") && w.Contains("d_and"));
    }

    [Fact]
    public void Symbole_par_defaut_et_correspondance_automatique()
    {
        var q = Def("Q2N2222");
        Assert.Equal(GabaritSymbole.Npn, q.Symbole.Gabarit);
        Assert.Empty(q.Verifier());
        var m = Def("BS170");
        Assert.Equal(["D", "G", "S", "B"], m.BrochesModele);
        Assert.Empty(m.Verifier());
        var x = Def("RCF");
        Assert.Equal(GabaritSymbole.Boite, x.Symbole.Gabarit);
        Assert.Equal(3, x.Symbole.Broches.Count);
        Assert.Empty(x.Verifier());
    }

    [Fact]
    public void Verifier_signale_broche_du_modele_non_reliee_ou_en_double()
    {
        var q = Def("Q2N2222");
        q.Symbole.Broches.Single(b => b.BrocheModele == "E").BrocheModele = null;
        Assert.Contains(q.Verifier(), i => i.Severity == Severity.Error && i.Message.Contains("« E »"));
        q.Symbole.Broches.First(b => b.BrocheModele == null).BrocheModele = "C";
        Assert.Contains(q.Verifier(), i => i.Severity == Severity.Error && i.Message.Contains("« C »") && i.Message.Contains("2 broches"));
        q.RelierAutomatiquement();
        Assert.Contains(q.Verifier(), i => i.Message.Contains("« C »"));     // les doublons restent à corriger à la main
    }

    [Fact]
    public void Netlist_dans_l_ordre_du_modele_quel_que_soit_le_symbole()
    {
        var d = Def("RCF");
        // On réarrange le symbole : broche du haut à droite = IN, en bas à gauche = GND.
        d.Symbole.Broches[0].BrocheModele = "GND"; d.Symbole.Broches[2].BrocheModele = "IN";
        var s = new Schematic();
        s.Components.Add(new() { Ref = "X1", Kind = ComponentKind.Spice, Definition = d, Position = new(0, 0), Value = "C=2u" });
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(8, 0), Value = "IN" });
        s.Components.Add(new() { Ref = "NET2", Kind = ComponentKind.Label, Position = new(0, 2), Value = "OUT" });
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(0, 0) });
        var c = Connectivity.Build(s);
        Assert.Equal("X1 IN OUT 0 RCF C=2u", CircuitBuilder.Ligne(s.Components[0], c).ToSpiceLine());
        Assert.Contains(".subckt RCF", CircuitBuilder.Preview(s, c, null));
    }

    [Fact]
    public void Pivoter_un_composant_spice_tourne_ses_broches()
    {
        var q = new PlacedComponent { Ref = "Q1", Kind = ComponentKind.Spice, Definition = Def("Q2N2222"), Position = new(10, 10) };
        Assert.Equal([new GridPoint(10, 10), new GridPoint(13, 7), new GridPoint(13, 13)], q.Pins);
        q.Rotation = 1;
        Assert.Equal([new GridPoint(10, 10), new GridPoint(13, 13), new GridPoint(7, 13)], q.Pins);
    }

    [Fact]
    public void Erc_reference_et_correspondance()
    {
        var d = Def("Q2N2222");
        d.Symbole.Broches[0].BrocheModele = null;
        var s = new Schematic();
        s.Components.Add(new() { Ref = "X1", Kind = ComponentKind.Spice, Definition = d, Position = new(0, 0) });
        var issues = Erc.Check(s, Connectivity.Build(s), circuitSeul: true);
        Assert.Contains(issues, i => i.Message.Contains("commencer par « Q »"));
        Assert.Contains(issues, i => i.Message.Contains("« B »") && i.Severity == Severity.Error);
    }

    [Fact]
    public void Json_aller_retour_et_bibliotheque_sur_disque()
    {
        var d = Def("BS170");
        d.Symbole.Broches[3].X = 5;
        var r = DefinitionComposant.DepuisJson(d.VersJson());
        Assert.Equal(d.VersJson(), r.VersJson());
        Assert.Contains("\"Type\": \"Nmos\"", d.VersJson());
        var dir = Path.Combine(Path.GetTempPath(), "silx-bib-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bib = BibliothequeComposants.Charger(dir);
            bib.Enregistrer(d); bib.Enregistrer(Def("RCF"));
            var relu = BibliothequeComposants.Charger(dir);
            Assert.Equal(["BS170", "RCF"], relu.Definitions.Select(x => x.Nom));
            Assert.Equal(5, relu.Definitions[0].Symbole.Broches[3].X);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    /// <summary>Suiveur à NPN importé : V(E) ≈ V(B) − Vbe ≈ 2 − 0,65 V.</summary>
    [Fact]
    public async Task Suiveur_npn_importe_simule_avec_ngspice()
    {
        var e = new Silx.Etude.Etude(); var banc = new Banc(); var s = e.Schema;
        s.Components.Add(new() { Ref = "Q1", Kind = ComponentKind.Spice, Definition = Def("Q2N2222"), Position = new(0, 0) }); // B(0,0) C(3,-3) E(3,3)
        s.Components.Add(new() { Ref = "RE", Kind = ComponentKind.Resistor, Position = new(3, 3), Rotation = 1, Value = "1k" });  // (3,3)-(3,7)
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(3, 7) });
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(3, 3), Value = "OUT" });
        banc.Calque.Components.Add(new() { Ref = "VB", Kind = ComponentKind.VoltageSource, Position = new(0, 0), Rotation = 1, Value = "DC 2" }); // (0,0)-(0,4)
        banc.Calque.Components.Add(new() { Ref = "GND2", Kind = ComponentKind.Ground, Position = new(0, 4) });
        banc.Calque.Components.Add(new() { Ref = "VCC", Kind = ComponentKind.VoltageSource, Position = new(3, -3), Rotation = 3, Value = "DC 10" }); // (3,-3)-(3,-7)
        banc.Calque.Components.Add(new() { Ref = "GND3", Kind = ComponentKind.Ground, Position = new(3, -7) });
        banc.Transitoire.Duree = 1e-3; banc.Transitoire.Pas = 1e-5;
        var r = await Verification.RunAsync(e, banc, []);
        Assert.Null(r.Erreur);
        var v = r.Resultats[AnalyseKind.Transitoire].Vector("v(out)");
        Assert.InRange(v[^1], 1.25, 1.45);
    }

    /// <summary>Filtre RC en sous-circuit importé : fc = 1/(2πRC) = 159 Hz, et 79,6 Hz avec le paramètre d'instance C=2u.</summary>
    [Theory]
    [InlineData("", 159.15)]
    [InlineData("C=2u", 79.58)]
    public async Task Sous_circuit_rc_importe_donne_la_bonne_frequence_de_coupure(string param, double fc)
    {
        var e = new Silx.Etude.Etude(); var s = e.Schema;
        s.Components.Add(new() { Ref = "X1", Kind = ComponentKind.Spice, Definition = Def("RCF"), Position = new(0, 0), Value = param }); // IN(0,0) OUT(0,2) GND(8,0)
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(0, 0), Value = "IN" });
        s.Components.Add(new() { Ref = "NET2", Kind = ComponentKind.Label, Position = new(0, 2), Value = "OUT" });
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(8, 0) });
        // Charge de 10 MΩ : le socle refuse un nœud relié à une seule broche, même de sous-circuit (effet < 0,01 %).
        s.Components.Add(new() { Ref = "RL", Kind = ComponentKind.Resistor, Position = new(0, 2), Rotation = 1, Value = "10meg" });
        s.Components.Add(new() { Ref = "GND3", Kind = ComponentKind.Ground, Position = new(0, 6) });
        var banc = new Banc { Type = AnalyseKind.Ac };
        banc.Calque.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(0, 0), Rotation = 3, Value = "AC 1" }); // (0,0)-(0,-4)
        banc.Calque.Components.Add(new() { Ref = "GND2", Kind = ComponentKind.Ground, Position = new(0, -4) });
        banc.Gabarits.Add(new() { Mesure = TypeMesure.BandePassante3dB, Noeud = "OUT", Min = fc * 0.97, Max = fc * 1.03 });
        var r = await Verification.RunAsync(e, banc, []);
        Assert.Null(r.Erreur);
        Assert.Equal(Verdict.Pass, r.Global);
    }
}
