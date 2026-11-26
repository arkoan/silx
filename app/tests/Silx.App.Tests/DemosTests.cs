using Silx.Etude;
using Silx.Mesures.Gabarits;
namespace Silx.App.Tests;

/// <summary>Projets de démonstration : chaque banc simule sous ngspice et respecte ses contraintes.</summary>
public class DemosTests
{
    public static TheoryData<string> Noms => new(Demos.Toutes.Select(d => d.Nom));

    [Theory]
    [MemberData(nameof(Noms))]
    public async Task La_demo_est_conforme(string nom)
    {
        var e = Demos.Toutes.Single(d => d.Nom == nom).Creer();
        foreach (var b in e.Bancs)
        {
            var r = await Verification.RunAsync(e, b, [], TestContext.Current.CancellationToken);
            Assert.Null(r.Erreur);
            Assert.True(r.Nombre > 0);
            var detail = string.Join(" | ", r.Criteres.Select(c => $"{c.Critere.Info.Nom}={c.Resultat.Valeur} {c.Resultat.Verdict}")
                .Concat(r.Traces.Select(t => $"{t.Gabarit.Nom} {t.Resultat.Verdict} {t.Resultat.Marge} {t.Resultat.Detail}")));
            Assert.True(r.Global == Verdict.Pass, $"{nom} / {b.Nom} : {detail}");
        }
    }

    [Fact]
    public async Task Colpitts_oscille_vers_2_2_MHz()
    {
        var e = Demos.Colpitts(); var b = e.Bancs[0];
        var r = await Verification.RunAsync(e, b, [], TestContext.Current.CancellationToken);
        var f = r.Criteres.Single(c => c.Critere.Mesure == TypeMesure.Frequence).Resultat.Valeur!.Value;
        Assert.InRange(f, 2.1e6, 2.3e6);
    }
}
