using Silx.Etude;
using Silx.Mcp;
namespace Silx.Mcp.Tests;

/// <summary>Analyses DC (balayage, point de fonctionnement) et bruit, vues par le serveur MCP.</summary>
public class AnalysesTests
{
    /// <summary>Filtre RC avec un banc « balayage DC » de 0 à 5 V sur IN : en continu, OUT suit IN.</summary>
    static ProjetCharge RcAvecBalayage(SessionSilx s)
    {
        var p = s.OuvrirDemo("Filtre RC");
        var b = new Banc { Nom = "Transfert DC" };
        b.Calque.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(4, 8), Rotation = 1, Value = "DCSWEEP(0 5 0.1)" });
        b.Gabarits.Add(new() { Mesure = TypeMesure.MaximumDc, Noeud = "OUT", Min = 4.9 });
        p.Etude.Bancs.Add(b);
        return p;
    }

    [Fact]
    public async Task Balayage_DC_decrit_simule_et_trace()
    {
        var p = RcAvecBalayage(new SessionSilx());
        var b = SessionSilx.Banc_(p, "Transfert DC");
        var vue = Descriptions.Banc(p, b);
        Assert.Equal("dc", vue.Analyse);
        Assert.Equal("V1", vue.Reglages["source_balayee"]);
        Assert.Equal(5.0, vue.Reglages["a"]);

        var r = await SessionSilx.SimulerAsync(p, "Transfert DC", CancellationToken.None);
        var sim = Descriptions.Simulation(p, b, r[0].Resultat, false);
        Assert.Null(sim.Erreur);
        Assert.Equal("PASS", sim.Verdict);
        var (x, y, ux, uy, _) = SessionSilx.Courbe(p, "Transfert DC", "OUT", 100, null, null);
        Assert.Equal(("V", "V"), (ux, uy));
        Assert.InRange(x.Max(), 4.99, 5.01);
        Assert.InRange(y.Max(), 4.99, 5.01);
    }

    [Fact]
    public async Task Point_de_fonctionnement_de_l_ampli()
    {
        var s = new SessionSilx();
        var p = s.OuvrirDemo("Ampli émetteur commun");
        var op = await SessionSilx.PointAsync(p, "1", CancellationToken.None);
        Assert.Null(op.Erreur);
        Assert.InRange(op.Tensions["VCC"], 11.99, 12.01);
        Assert.InRange(op.Tensions["B"], 1.6, 2.2);          // pont 47k / 10k, chargé par la base
        Assert.InRange(op.Courant("RC")!.Value, 0.8e-3, 1.6e-3);   // ≈ (VB − 0,65) / RE
    }

    [Fact]
    public async Task Bruit_d_un_noeud()
    {
        var s = new SessionSilx();
        var p = s.OuvrirDemo("Ampli émetteur commun");
        var (_, c) = await SessionSilx.BruitAsync(p, null, "V(OUT)", CancellationToken.None);
        Assert.True(c.F.Length > 10);
        Assert.True(c.TotalSortie > 0 && double.IsFinite(c.TotalSortie));
        Assert.NotNull(c.SortieA(1e3));
        await Assert.ThrowsAsync<ErreurSilx>(() => SessionSilx.BruitAsync(p, null, "NULLEPART", CancellationToken.None));
        var rc = s.OuvrirDemo("Filtre RC");
        var e = await Assert.ThrowsAsync<ErreurSilx>(() => SessionSilx.BruitAsync(rc, "1", "OUT", CancellationToken.None));
        Assert.Contains("fréquentiel", e.Message);
    }
}
