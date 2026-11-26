using Silx.Etude;
using Silx.Mesures.Gabarits;
using Xunit;
namespace Silx.App.Tests;

public class ProjetTests
{
    static string Netlist(Silx.Etude.Etude e, Banc b)
    {
        var s = b.Fusion(e.Schema); var c = Connectivity.Build(s);
        return CircuitBuilder.Preview(s, c, CircuitBuilder.AnalysisFor(b, b.Type));
    }

    [Fact]
    public async Task Aller_retour_de_la_demo_meme_netlist_et_memes_verdicts()
    {
        var e = Silx.Etude.Etude.Demo();
        e.Bancs[0].Graphiques.Add(["IN"]);
        var json = Projet.VersJson(e);
        var r = Projet.DepuisJson(json);
        Assert.Equal(e.Bancs.Count, r.Bancs.Count);
        for (int i = 0; i < e.Bancs.Count; i++)
        {
            Assert.Equal(Netlist(e, e.Bancs[i]), Netlist(r, r.Bancs[i]));
            Assert.Equal(e.Bancs[i].Type, r.Bancs[i].Type);
            Assert.Equal(e.Bancs[i].GabaritsTraces.Count, r.Bancs[i].GabaritsTraces.Count);
            Assert.Equal(e.Bancs[i].Gabarits.Select(g => (g.Mesure, g.Noeud, g.Min, g.Max)), r.Bancs[i].Gabarits.Select(g => (g.Mesure, g.Noeud, g.Min, g.Max)));
        }
        Assert.Equal(["IN"], r.Bancs[0].Graphiques[0]);
        Assert.Equal(json, Projet.VersJson(r));                       // stable
        var v = await Verification.RunAsync(r, r.Bancs[0], [], TestContext.Current.CancellationToken);
        Assert.Equal(Verdict.Pass, v.Global);
    }

    [Fact]
    public async Task Le_projet_embarque_le_composant_spice_et_resimule_sans_la_bibliotheque()
    {
        var e = new Silx.Etude.Etude(); var banc = new Banc { Nom = "Interrupteur" }; e.Bancs.Add(banc);
        var def = DefinitionComposant.Depuis(ModelesSpice.Analyser(ModelesFabricantTests.Fichier).Modeles.First());
        e.Schema.Components.Add(new() { Ref = "X1", Kind = ComponentKind.Spice, Definition = def, Position = new(0, 0) });
        e.Schema.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(1, 2) });
        e.Schema.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(1, -2), Value = "OUT" });
        banc.Calque.Components.Add(new() { Ref = "VG", Kind = ComponentKind.VoltageSource, Position = new(0, 0), Rotation = 1, Value = "DC 5" });
        banc.Calque.Components.Add(new() { Ref = "GND2", Kind = ComponentKind.Ground, Position = new(0, 4) });
        banc.Calque.Components.Add(new() { Ref = "RL", Kind = ComponentKind.Resistor, Position = new(1, -2), Rotation = 3, Value = "1k" });
        banc.Calque.Components.Add(new() { Ref = "VDD", Kind = ComponentKind.VoltageSource, Position = new(1, -6), Rotation = 3, Value = "DC 5" });
        banc.Calque.Components.Add(new() { Ref = "GND3", Kind = ComponentKind.Ground, Position = new(1, -10) });
        banc.Transitoire.Duree = 1e-4; banc.Transitoire.Pas = 1e-6;

        Ancien.Raccorder(e.Schema, banc);   // tracé au pas de 4
        var dir = Directory.CreateTempSubdirectory("silx-projet").FullName;
        try
        {
            var chemin = Path.Combine(dir, "interrupteur" + Projet.Extension);
            Projet.Enregistrer(e, chemin);
            Assert.False(File.Exists(chemin + ".tmp"));
            var r = Projet.Ouvrir(chemin);
            var x1 = r.Schema.Components.Single(k => k.Ref == "X1");
            Assert.NotSame(def, x1.Definition);
            Assert.Equal("DC 25", x1.Definition!.BrochesCachees["50"]);
            Assert.Equal(GabaritSymbole.Nmos3, x1.Definition.Symbole.Gabarit);
            var v = await Verification.RunAsync(r, r.Bancs[0], [], TestContext.Current.CancellationToken);
            Assert.Null(v.Erreur);
            Assert.InRange(v.Resultats[AnalyseKind.Transitoire].Vector("v(out)")[^1], 0, 0.1);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Projet_trop_recent_ou_composant_manquant_refuse_clairement()
    {
        Assert.Throws<InvalidDataException>(() => Projet.DepuisJson("""{ "Version": 99 }"""));
        Assert.Throws<InvalidDataException>(() => Projet.DepuisJson("""{ "Version": 1, "Circuit": { "Composants": [ { "Ref": "X1", "Type": "Spice", "Composant": "Inconnu" } ] } }"""));
    }

    /// <summary>Fichier abîmé (JSON invalide, champ nul) : une seule erreur claire, InvalidDataException.</summary>
    [Theory]
    [InlineData("{ pas du json")]
    [InlineData("{\"Version\": 2, \"Circuit\": null, \"Bancs\": null}")]
    [InlineData("{\"Version\": 99}")]
    public void Projet_abime_erreur_claire(string json) => Assert.Throws<InvalidDataException>(() => Projet.DepuisJson(json));
}
