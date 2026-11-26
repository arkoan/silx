using Silx.Etude;
using Xunit;
namespace Silx.App.Tests;

/// <summary>Analyse DC : caractéristique de transfert (balayage d'une source) et point de fonctionnement.</summary>
public class AnalyseDcTests
{
    /// <summary>Banc « Réponse en fréquence » de la démo RC (R1 = 1 k, RL = 100 k) : en continu, OUT = IN × 100/101.</summary>
    static (Silx.Etude.Etude E, Banc B) Diviseur(string source)
    {
        var e = Silx.Etude.Etude.Demo(); var b = e.Bancs[1];
        b.Calque.Components.Single(k => k.Ref == "V1").Value = source;
        b.Gabarits.Clear(); b.GabaritsTraces.Clear();
        return (e, b);
    }

    [Fact]
    public async Task Balayage_dc_mesures_et_export()
    {
        var (e, b) = Diviseur("DCSWEEP(0 5 0.05)");
        Assert.Equal(AnalyseKind.Dc, b.Type);
        Assert.Equal(("V1", "V"), b.SourceBalayee);
        b.Gabarits.Add(new() { Mesure = TypeMesure.PenteMaxDc, Noeud = "OUT", Min = 0.98, Max = 1 });
        b.Gabarits.Add(new() { Mesure = TypeMesure.SeuilDc, Noeud = "OUT" });
        b.Gabarits.Add(new() { Mesure = TypeMesure.MaximumDc, Noeud = "OUT" });
        b.Gabarits.Add(new() { Mesure = TypeMesure.MaximumDc, Noeud = "I(RL:1)" });
        var r = await Verification.RunAsync(e, b, []);
        Assert.Null(r.Erreur);
        var nl = CircuitBuilder.Preview(b.Fusion(e.Schema), Connectivity.Build(b.Fusion(e.Schema)), CircuitBuilder.AnalysisFor(b, AnalyseKind.Dc)); Assert.True(nl.Contains(".dc V1 0 5 0.05"), nl);
        var (x, y) = Verification.Courbe(AnalyseKind.Dc, r.Resultats[AnalyseKind.Dc], "OUT", null);
        Assert.Equal(101, x.Length); Assert.Equal(0, x[0], 9); Assert.Equal(5, x[^1], 9);
        Assert.Equal(5 * 100.0 / 101, y[^1], 4);
        Assert.Equal(Silx.Mesures.Gabarits.Verdict.Pass, r.Criteres[0].Resultat.Verdict);
        Assert.Equal(2.5, r.Criteres[1].Resultat.Valeur!.Value, 2);
        Assert.Equal(4.95, r.Criteres[2].Resultat.Valeur!.Value, 2);
        Assert.Equal("A", r.Criteres[3].Resultat.Unite);
        Assert.Equal(5 / 101e3, r.Criteres[3].Resultat.Valeur!.Value, 7);
        var csv = ExportDonnees.Courbes(AnalyseKind.Dc, r.Resultats[AnalyseKind.Dc], ["OUT"], b.Fusion(e.Schema));
        Assert.StartsWith("v(v-sweep);V(OUT) (V)", csv);
    }

    [Theory]
    [InlineData(0, 5, 0, 1)]          // pas nul : 10 000 points
    [InlineData(5, 0, 0.1, -0.1)]     // balayage descendant
    [InlineData(0, 1, 1e-9, 1e-4)]    // trop de points : ramené à 10 000
    public void Pas_du_balayage(double de, double a, double pas, double attendu) =>
        Assert.Equal(attendu == 1 ? (a - de) / 10_000 : attendu, CircuitBuilder.PasDc(de, a, pas), 12);

    [Fact]
    public async Task Point_de_fonctionnement_avec_banc_et_circuit_seul()
    {
        var (e, b) = Diviseur("DC 5");
        var r = await PointFonctionnement.CalculerAsync(e, b);
        Assert.Null(r.Erreur);
        Assert.Equal(5 * 100.0 / 101, r.Tensions["OUT"], 4);
        Assert.Equal(5, r.Tensions["IN"], 6);
        Assert.Equal(5 / 101e3, r.Courant("R1")!.Value, 8);   // entrant par la broche 1 (IN)
        Assert.Equal(0, r.Courant("C1")!.Value, 12);          // condensateur ouvert en continu
        // Circuit seul (sans masse ni source) : erreur explicite, pas d'exception.
        var seul = await PointFonctionnement.CalculerAsync(e, null);
        Assert.NotNull(seul.Erreur);
    }
}
