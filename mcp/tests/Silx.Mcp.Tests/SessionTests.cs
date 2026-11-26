using Silx.Etude;
using Silx.Mcp;
namespace Silx.Mcp.Tests;

/// <summary>Logique des outils, sans protocole : projets, bancs, simulation, campagnes, modification, enregistrement.</summary>
public class SessionTests
{
    static SessionSilx Session(out ProjetCharge rc)
    {
        var s = new SessionSilx();
        rc = s.OuvrirDemo("Filtre RC");
        return s;
    }

    [Fact]
    public void Projet_par_defaut_puis_ambigu()
    {
        var s = Session(out var rc);
        Assert.Same(rc, s.Projet_(null));
        Assert.Same(rc, s.Projet_("filtre rc"));
        s.OuvrirDemo("Filtre à quartz 4 MHz");
        var e = Assert.Throws<ErreurSilx>(() => s.Projet_(null));
        Assert.Contains("Plusieurs projets", e.Message);
        Assert.Throws<ErreurSilx>(() => s.Projet_("p99"));
        Assert.Throws<ErreurSilx>(() => s.OuvrirDemo("Inconnue"));
    }

    [Fact]
    public void Banc_par_nom_ou_numero()
    {
        Session(out var rc);
        Assert.Equal("Échelon 1 V", SessionSilx.Banc_(rc, "1").Nom);
        Assert.Equal("Réponse en fréquence", SessionSilx.Banc_(rc, "réponse en fréquence").Nom);
        Assert.Contains("Précisez le banc", Assert.Throws<ErreurSilx>(() => SessionSilx.Banc_(rc, null)).Message);
        Assert.Contains("introuvable", Assert.Throws<ErreurSilx>(() => SessionSilx.Banc_(rc, "3")).Message);
    }

    [Fact]
    public void Description_du_circuit_et_des_bancs()
    {
        Session(out var rc);
        var v = Descriptions.Projet(rc);
        Assert.Equal(["IN", "OUT"], v.NoeudsNommes);
        var r1 = v.Circuit.Single(c => c.Ref == "R1");
        Assert.Equal(["IN", "OUT"], r1.Noeuds);
        Assert.Equal("résistance", r1.Type);
        Assert.Equal(2, v.Bancs.Count);
        var b = Descriptions.Banc(rc, rc.Etude.Bancs[0]);
        Assert.Equal("transitoire", b.Analyse);
        Assert.Contains(b.Criteres, c => c.Mesure == "TempsDeMontee" && c.Max == 2.5e-3 && c.Unite == "s");
        Assert.Equal(2, b.GabaritsDessines.Count);
    }

    [Fact]
    public async Task Simulation_nominale_du_filtre_RC()
    {
        Session(out var rc);
        var r = await SessionSilx.SimulerAsync(rc, null, CancellationToken.None);
        Assert.Equal(2, r.Count);
        var echelon = Descriptions.Simulation(rc, r[0].Banc, r[0].Resultat, false);
        Assert.Null(echelon.Erreur);
        Assert.Equal("PASS", echelon.Verdict);
        var tr = echelon.Criteres.Single(c => c.Nom.StartsWith("Temps de montée")).Valeur!.Value;
        Assert.InRange(tr, 2.1e-3, 2.3e-3);   // 2,2 RC
        var bode = Descriptions.Simulation(rc, r[1].Banc, r[1].Resultat, false);
        Assert.InRange(bode.Criteres.Single().Valeur!.Value, 150, 170);   // 1/(2π RC) ≈ 159 Hz

        var resume = Descriptions.ResumeTests(rc);
        Assert.Equal("PASS", resume.Verdict);
        Assert.All(resume.Bancs, b => Assert.Equal("simulé", b.Simulation));

        var (x, y, ux, uy, n) = SessionSilx.Courbe(rc, "1", "V(OUT)", 100, null, null);
        Assert.True(n > 100);
        Assert.InRange(x.Length, 2, 100);
        Assert.Equal(("s", "V"), (ux, uy));
        Assert.InRange(y.Max(), 0.95, 1.01);
        Assert.Equal(x.OrderBy(v => v), x);
        var (xf, _, uxf, uyf, _) = SessionSilx.Courbe(rc, "2", "OUT", 500, 100, 1000);
        Assert.Equal(("Hz", "dB"), (uxf, uyf));
        Assert.All(xf, f => Assert.InRange(f, 100, 1000));
        Assert.Contains("absent", Assert.Throws<ErreurSilx>(() => SessionSilx.Courbe(rc, "1", "XYZ", 100, null, null)).Message);
        Assert.Contains(SessionSilx.SignauxDisponibles(rc, rc.Etude.Bancs[0]), s => s == "I(R1:1)");
    }

    [Fact]
    public void Bilan_avant_simulation()
    {
        Session(out var rc);
        var r = Descriptions.ResumeTests(rc);
        Assert.Equal("INCOMPLET", r.Verdict);
        Assert.Contains("pas encore été simulé", Assert.Throws<ErreurSilx>(() => SessionSilx.Courbe(rc, "1", "OUT", 100, null, null)).Message);
    }

    [Fact]
    public async Task Modification_puis_echec_puis_coins()
    {
        Session(out var rc);
        await SessionSilx.SimulerAsync(rc, "1", CancellationToken.None);
        var (k, ancienne, _) = await SessionSilx.ChangerValeurAsync(rc, "r1", "2.2k", null, null, CancellationToken.None);
        Assert.Equal(("1k", "2.2k"), (ancienne, k.Value));
        Assert.True(rc.Modifie);
        Assert.Empty(rc.Simulations);   // résultats périmés effacés
        var r = await SessionSilx.SimulerAsync(rc, "1", CancellationToken.None);
        var v = Descriptions.Simulation(rc, r[0].Banc, r[0].Resultat, false);
        Assert.Equal("FAIL", v.Verdict);   // tr ≈ 4,8 ms > 2,5 ms
        var echec = Descriptions.ResumeTests(rc).Bancs[0].Echecs;
        Assert.Contains(echec, e => e.Contains("Temps de montée"));

        await Assert.ThrowsAsync<ErreurSilx>(() => SessionSilx.ChangerValeurAsync(rc, "R1", "abc", null, null, CancellationToken.None));
        await Assert.ThrowsAsync<ErreurSilx>(() => SessionSilx.ChangerValeurAsync(rc, "R1", "-1k", null, null, CancellationToken.None));
        var e2 = await Assert.ThrowsAsync<ErreurSilx>(() => SessionSilx.ChangerValeurAsync(rc, "V1", "DC 5", null, null, CancellationToken.None));
        Assert.Contains("banc", e2.Message);
        var (v1, _, lieu) = await SessionSilx.ChangerValeurAsync(rc, "V1", "PULSE(0 2 0.5m 1u 1u 20m 40m)", "1", null, CancellationToken.None);
        Assert.Contains("Échelon", lieu);

        // Coins : R1 ± 1 %, C1 ± 10 % → 4 combinaisons ; le banc garde sa campagne (aucune).
        await SessionSilx.ChangerValeurAsync(rc, "R1", "1k", null, null, CancellationToken.None);
        await SessionSilx.ChangerValeurAsync(rc, "V1", "PULSE(0 1 0.5m 1u 1u 20m 40m)", "1", null, CancellationToken.None);
        var c = OutilsSilx.Reglages(rc.Etude.Bancs[0].Campagne, "coins", null, null, null, null, null, null, null, null, null, null, null, null)!;
        var camp = await SessionSilx.CampagneAsync(rc, "1", c, null, CancellationToken.None);
        Assert.Equal(4, camp.Tirages.Count);
        Assert.Equal(ModeCampagne.Aucune, rc.Etude.Bancs[0].Campagne.Mode);
        Assert.NotNull(camp.PireCas);
        Assert.Equal("C1+ R1+", camp.PireCas!.Tirage.Libelle);   // RC maximal : temps de montée le plus long
        var vue = Descriptions.CampagneResultat(rc, rc.Etude.Bancs[0], camp);
        Assert.Equal("coins", vue.Mode);
        Assert.Equal(2, vue.Statistiques.Count);
    }

    [Fact]
    public async Task Campagne_absente_refusee()
    {
        Session(out var rc);
        await Assert.ThrowsAsync<ErreurSilx>(() => SessionSilx.CampagneAsync(rc, "1", null, null, CancellationToken.None));
    }

    [Fact]
    public void Reglages_de_campagne()
    {
        var banc = new Campagne { Mode = ModeCampagne.MonteCarlo, Tirages = 100, Graine = 7 };
        Assert.Null(OutilsSilx.Reglages(banc, null, null, null, null, null, null, null, null, null, null, null, null, null));
        var c = OutilsSilx.Reglages(banc, null, 20, "uniforme", null, null, null, null, null, null, null, null, null, null)!;
        Assert.Equal((ModeCampagne.MonteCarlo, 20, LoiTirage.Uniforme, 7), (c.Mode, c.Tirages, c.Loi, c.Graine));
        Assert.Equal(100, banc.Tirages);   // la campagne du banc n'est pas touchée
        var b = OutilsSilx.Reglages(banc, "balayage", null, null, null, "R1", "1k", "10k", 3, "log", null, null, null, null)!;
        Assert.Equal((ModeCampagne.Balayage, "R1", 1e3, 10e3, 3, EchelleBalayage.Logarithmique), (b.Mode, b.Cible, b.Debut, b.Fin, b.Points, b.Echelle));
        var l = OutilsSilx.Reglages(banc, "balayage", null, null, null, "R1", null, null, null, null, "1k 2.2k", null, null, null)!;
        Assert.Equal(EchelleBalayage.Liste, l.Echelle);
        Assert.Throws<ErreurSilx>(() => OutilsSilx.Reglages(banc, "autre", null, null, null, null, null, null, null, null, null, null, null, null));
        Assert.Throws<ErreurSilx>(() => OutilsSilx.Reglages(banc, null, null, null, null, null, "beaucoup", null, null, null, null, null, null, null));
    }

    [Fact]
    public void Enregistrer_et_rouvrir_sans_ecraser_par_megarde()
    {
        var dossier = Directory.CreateTempSubdirectory("silx-mcp-").FullName;
        try
        {
            var s = Session(out var rc);
            Assert.Contains("chemin", Assert.Throws<ErreurSilx>(() => SessionSilx.Enregistrer(rc, null, false)).Message);
            var chemin = Path.Combine(dossier, "rc");
            SessionSilx.Enregistrer(rc, chemin, false);
            Assert.Equal(chemin + ".silx", rc.Chemin);
            Assert.Contains("ecraser", Assert.Throws<ErreurSilx>(() => SessionSilx.Enregistrer(rc, null, false)).Message);
            Assert.Contains("existe déjà", Assert.Throws<ErreurSilx>(() => SessionSilx.Enregistrer(rc, chemin + ".silx", false)).Message);
            SessionSilx.Enregistrer(rc, null, true);

            var p = s.Ouvrir(chemin + ".silx");
            Assert.Equal(rc.Id, p.Id);   // même fichier : même identifiant
            Assert.Equal(2, p.Etude.Bancs.Count);
            Assert.Throws<ErreurSilx>(() => s.Ouvrir(Path.Combine(dossier, "absent.silx")));
            Assert.Contains("extension", Assert.Throws<ErreurSilx>(() => s.Ouvrir(Path.Combine(dossier, "x.txt"))).Message);
            File.WriteAllText(Path.Combine(dossier, "abime.silx"), "{ pas du json");
            Assert.Contains("illisible", Assert.Throws<ErreurSilx>(() => s.Ouvrir(Path.Combine(dossier, "abime.silx"))).Message);
        }
        finally { Directory.Delete(dossier, true); }
    }

    [Fact]
    public void Signal_normalise()
    {
        Assert.Equal("OUT", SessionSilx.NormaliserSignal("V(OUT)"));
        Assert.Equal("out", SessionSilx.NormaliserSignal(" v(out) "));
        Assert.Equal("I(R1:2)", SessionSilx.NormaliserSignal("i(R1:2)"));
        Assert.Equal("VCC", SessionSilx.NormaliserSignal("VCC"));
    }
}
