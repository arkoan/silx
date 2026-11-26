using Silx.Etude;
using Silx.Mesures.Gabarits;
namespace Silx.App.Tests;

/// <summary>Campagnes sur l'exemple RC (R1 = 1k, C1 = 1µ, échelon) : balayage, Monte-Carlo, coins.</summary>
public class CampagneTests
{
    static (Silx.Etude.Etude E, Banc B) Exemple() { var e = Silx.Etude.Etude.Demo(); return (e, e.Bancs[0]); }

    [Fact]
    public async Task Balayage_de_r1_fait_croitre_le_temps_de_montee()
    {
        var (e, b) = Exemple();
        b.Campagne = new() { Mode = ModeCampagne.Balayage, Cible = "R1", Debut = 500, Fin = 2000, Points = 4 };
        var r = await Campagnes.RunAsync(e, b, ["OUT"]);
        Assert.Equal(4, r.Tirages.Count);
        Assert.All(r.Tirages, t => Assert.Null(t.Erreur));
        var tm = b.Gabarits.Single(g => g.Mesure == TypeMesure.TempsDeMontee);
        var montees = r.Tirages.Select(t => t.Criteres[tm].Valeur!.Value).ToList();
        Assert.True(montees.Zip(montees.Skip(1)).All(p => p.Second > p.First));   // τ = RC croît avec R
        Assert.InRange(montees[0], 0.9e-3, 1.3e-3);                               // 2,2 RC à 500 Ω ≈ 1,1 ms
        Assert.False(r.Tirages[^1].Conforme);                                     // 2 kΩ : 4,4 ms > 2,5 ms
        Assert.True(r.Tirages[0].Courbes.ContainsKey("OUT"));
        Assert.Equal(r.Tirages[^1], r.PireCas);
    }

    [Fact]
    public async Task Monte_carlo_donne_moyenne_ecart_type_et_cpk()
    {
        var (e, b) = Exemple();
        b.Campagne = new() { Mode = ModeCampagne.MonteCarlo, Tirages = 40, Graine = 7 };
        var plan = Campagnes.Plan(b.Campagne, b.Fusion(e.Schema), out _);
        Assert.Equal(40, plan.Count);
        Assert.All(plan, t => Assert.InRange(t.Valeurs["C1"], 0.9e-6, 1.1e-6));   // ± 10 % par défaut
        Assert.All(plan, t => Assert.InRange(t.Valeurs["R1"], 990, 1010));        // ± 1 %
        var r = await Campagnes.RunAsync(e, b, ["OUT"]);
        var st = r.Stats.Single(s => s.Critere.Mesure == TypeMesure.TempsDeMontee);
        Assert.Equal(40, st.N);
        Assert.InRange(st.Moyenne, 2.0e-3, 2.4e-3);
        Assert.InRange(st.EcartType, 1e-5, 2e-4);
        Assert.NotNull(st.Cpk);
        Assert.InRange(r.Taux, 0.5, 1.0);
        // Reproductible : même graine, mêmes tirages.
        Assert.Equal(plan[3].Valeurs["C1"], Campagnes.Plan(b.Campagne, b.Fusion(e.Schema), out _)[3].Valeurs["C1"]);
    }

    [Fact]
    public async Task Coins_avec_temperature_et_pire_cas()
    {
        var (e, b) = Exemple();
        e.Schema.Components.Single(k => k.Ref == "C1").Tolerance = 20;
        b.Campagne = new() { Mode = ModeCampagne.Coins, CoinsTemperature = true };
        var r = await Campagnes.RunAsync(e, b, ["OUT"]);
        Assert.Equal(8, r.Tirages.Count);                 // R1, C1, T : 2³
        var pire = r.PireCas!;
        Assert.Contains("C1+", pire.Tirage.Libelle);      // C au maxi : montée la plus lente
        Assert.Contains("R1+", pire.Tirage.Libelle);
    }

    /// <summary>Pas automatique : ngspice resserre ses points au front de l'échelon (0,5 ms) et les espace ailleurs.</summary>
    [Fact]
    public async Task Pas_automatique_dense_au_front()
    {
        var (e, b) = Exemple();
        Assert.True(b.Transitoire.Auto);
        var r = await Verification.RunAsync(e, b, []);
        var t = r.Resultats[AnalyseKind.Transitoire].Vector("time");
        int auFront = t.Count(x => x >= 0.5e-3 && x < 0.6e-3), aPlat = t.Count(x => x >= 8e-3 && x < 8.1e-3);
        Assert.True(auFront >= 3 * Math.Max(aPlat, 1), $"front {auFront} points, plateau {aPlat}");
        Assert.True(t.Length < 2000);
        // Mesures inchangées à la précision près : temps de montée 2,2 RC.
        Assert.InRange(r.Criteres.Single(c => c.Critere.Mesure == TypeMesure.TempsDeMontee).Resultat.Valeur!.Value, 2.15e-3, 2.25e-3);
    }
}
