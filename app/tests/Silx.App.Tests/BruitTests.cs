using Silx.Etude;
using Xunit;
namespace Silx.App.Tests;

/// <summary>Analyse de bruit (.noise) sur le banc Bode de la démo RC (R1 = 1 k, C1 = 1 µF, RL = 100 k).</summary>
public class BruitTests
{
    [Fact]
    public async Task Bruit_thermique_du_filtre_rc()
    {
        var e = Silx.Etude.Etude.Demo(); var b = e.Bancs[1];
        var (c, err) = await Bruit.CalculerAsync(e, b, ["OUT", "I(R1:1)"]);
        Assert.Null(err);
        var o = Assert.Single(c).Value;   // les courants sont ignorés
        // Basse fréquence : √(4kTR) avec R = 1 k // 100 k ≈ 4,05 nV/√Hz (27 °C).
        Assert.InRange(o.Sortie[0], 3.9e-9, 4.2e-9);
        Assert.True(o.Sortie[^1] < o.Sortie[0] / 10, "filtré au-delà de fc");
        // Bruit total d'un RC : √(kT/C) ≈ 64 nV efficaces.
        Assert.InRange(o.TotalSortie, 55e-9, 70e-9);
        Assert.InRange(o.SortieA(1e3)!.Value, 0.5e-9, 1e-9);   // 4 nV/√Hz / √(1 + (1k/159)²) ≈ 0,64 nV/√Hz
    }

    [Fact]
    public async Task Critere_de_bruit_evalue_avec_la_simulation()
    {
        var e = Silx.Etude.Etude.Demo(); var b = e.Bancs[1];
        b.Gabarits.Clear(); b.GabaritsTraces.Clear();
        b.Gabarits.Add(new() { Mesure = TypeMesure.BruitSortieTotal, Noeud = "OUT", Max = 100e-9 });
        b.Gabarits.Add(new() { Mesure = TypeMesure.DensiteBruit1k, Noeud = "OUT" });
        var r = await Verification.RunAsync(e, b, []);
        Assert.Null(r.Erreur);
        Assert.Equal(Silx.Mesures.Gabarits.Verdict.Pass, r.Criteres[0].Resultat.Verdict);
        Assert.NotNull(r.Criteres[1].Resultat.Valeur);
        Assert.True(r.Bruit.ContainsKey("OUT"));
        // Banc temporel : pas de bruit (message clair).
        var (_, err) = await Bruit.CalculerAsync(e, e.Bancs[0], ["OUT"]);
        Assert.NotNull(err);
    }
}
