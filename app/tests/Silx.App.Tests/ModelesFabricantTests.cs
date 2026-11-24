using Silx.Etude;
using Xunit;
namespace Silx.App.Tests;

/// <summary>
/// Sous-circuit « à la fabricant » (forme des modèles de MOSFET discrets) : broches numérotées décrites en commentaire,
/// broche thermique en plus, plusieurs sous-circuits dans le même fichier. Modèle d'essai écrit pour les tests.
/// </summary>
public class ModelesFabricantTests
{
    public const string Fichier = """
        *DEMO138 at Temp. Electrical Model
        *-------------------------------------
        .SUBCKT DEMO138 20 10 30 50
        *20=DRAIN 10=GATE 30=SOURCE 50=VTEMP
        Rg 10 1 1
        M1 2 1 30 30 DMOS L=1u W=1u
        .MODEL DMOS NMOS(VTO=1.35 KP=1.1 LEVEL=1)
        Rd 20 2 0.5
        Dbody 30 20 DBODY
        .MODEL DBODY D(IS=1E-10)
        ET 101 0 VALUE {V(50)}
        RT 101 0 1k
        .ENDS DEMO138

        *DEMO138 ELECTRICAL MODEL (SOT-23 Single N-Ch DMOS)
        .SUBCKT DEMO138/B 20 10 30
        M1 20 10 30 30 DM2 L=1u W=1u
        .MODEL DM2 NMOS(VTO={1.3*{-0.002*TEMP+1.05}} KP=0.7 LEVEL=1)
        .ENDS
        """;

    static ModeleImporte Modele(string nom) => ModelesSpice.Analyser(Fichier).Modeles.Single(m => m.Nom == nom);

    [Fact]
    public void Les_commentaires_donnent_libelles_description_et_nature()
    {
        var i = Modele("DEMO138").Infos!;
        Assert.Equal("DRAIN", i.Libelles["20"]); Assert.Equal("GATE", i.Libelles["10"]);
        Assert.Equal("SOURCE", i.Libelles["30"]); Assert.Equal("VTEMP", i.Libelles["50"]);
        Assert.Equal(TypeModele.Nmos, i.NatureProbable);
        Assert.Contains("Electrical Model", i.Description);
        Assert.Contains("MOS", i.Composition); Assert.Contains("NMOS", i.Composition);
        // Second sous-circuit : pas de libellés, mais « N-Ch DMOS » et un NMOS interne → ordre usuel D, G, S supposé, signalé.
        var j = Modele("DEMO138/B").Infos!;
        Assert.Contains("N-Ch", j.Description);
        Assert.Equal(TypeModele.Nmos, j.NatureProbable);
        Assert.Equal("G", j.Roles["10"]);
        Assert.Contains("à vérifier", j.Composition);
    }

    [Fact]
    public void Sous_circuit_reconnu_prend_le_symbole_mos_et_raccorde_la_broche_thermique()
    {
        var d = DefinitionComposant.Depuis(Modele("DEMO138"));
        Assert.Equal(GabaritSymbole.Nmos, d.Symbole.Gabarit);
        Assert.Equal(3, d.Symbole.Broches.Count);                       // G, D, S : pas de broche de substrat
        string Sur(int x, int y) => d.Symbole.Broches.Single(b => b.X == x && b.Y == y).BrocheModele!;
        Assert.Equal("10", Sur(0, 0)); Assert.Equal("20", Sur(3, -3)); Assert.Equal("30", Sur(3, 3));
        Assert.Equal("DC 25", d.BrochesCachees["50"]);
        Assert.Equal("20 (DRAIN)", d.NomAffiche("20"));
        Assert.DoesNotContain(d.Verifier(), p => p.Severity == Severity.Error);
        // Aller-retour JSON
        var r = DefinitionComposant.DepuisJson(d.VersJson());
        Assert.Equal("DC 25", r.BrochesCachees["50"]); Assert.Equal("VTEMP", r.Libelles["50"]);
    }

    [Fact]
    public void Netlist_relie_la_broche_cachee_a_sa_source()
    {
        var d = DefinitionComposant.Depuis(Modele("DEMO138"));
        var s = new Schematic();
        s.Components.Add(new() { Ref = "X1", Kind = ComponentKind.Spice, Definition = d, Position = new(0, 0) });
        var k = s.Components[0];
        var ligne = CircuitBuilder.Ligne(k, Connectivity.Build(s), apercu: true).ToSpiceLine();
        Assert.EndsWith("X1_50 DEMO138", ligne);
        Assert.Equal("VX1_50 X1_50 0 DC 25", Assert.Single(CircuitBuilder.Auxiliaires(k)).ToSpiceLine());
    }

    /// <summary>Interrupteur : grille à 5 V, charge 1 kΩ sur 5 V → V(drain) proche de 0. La variante /B écrit ses paramètres
    /// à la manière PSpice (TEMP, accolades imbriquées) : elle exige la compatibilité PSpice de ngspice.</summary>
    [Theory]
    [InlineData("DEMO138")]
    [InlineData("DEMO138/B")]
    public async Task Mosfet_fabricant_en_interrupteur_avec_ngspice(string nom)
    {
        var e = new Silx.Etude.Etude(); var banc = new Banc(); var s = e.Schema;
        s.Components.Add(new() { Ref = "X1", Kind = ComponentKind.Spice, Definition = DefinitionComposant.Depuis(Modele(nom)), Position = new(0, 0) }); // G(0,0) D(3,-3) S(3,3)
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(3, 3) });
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(3, -3), Value = "OUT" });
        banc.Calque.Components.Add(new() { Ref = "VG", Kind = ComponentKind.VoltageSource, Position = new(0, 0), Rotation = 1, Value = "DC 5" });   // (0,0)-(0,4)
        banc.Calque.Components.Add(new() { Ref = "GND2", Kind = ComponentKind.Ground, Position = new(0, 4) });
        banc.Calque.Components.Add(new() { Ref = "RL", Kind = ComponentKind.Resistor, Position = new(3, -3), Rotation = 3, Value = "1k" });      // (3,-3)-(3,-7)
        banc.Calque.Components.Add(new() { Ref = "VDD", Kind = ComponentKind.VoltageSource, Position = new(3, -7), Rotation = 3, Value = "DC 5" }); // + (3,-7), − (3,-11)
        banc.Calque.Components.Add(new() { Ref = "GND3", Kind = ComponentKind.Ground, Position = new(3, -11) });
        banc.Transitoire.Duree = 1e-4; banc.Transitoire.Pas = 1e-6;
        var r = await Verification.RunAsync(e, banc, [], TestContext.Current.CancellationToken);
        Assert.True(r.Erreur == null, r.Erreur + "\n" + r.Journal);
        Assert.InRange(r.Resultats[AnalyseKind.Transitoire].Vector("v(out)")[^1], 0, 0.1);
    }
}
