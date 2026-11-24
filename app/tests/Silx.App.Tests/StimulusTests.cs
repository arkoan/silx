using Silx.Etude;
using Xunit;
namespace Silx.App.Tests;

public class StimulusTests
{
    [Theory]
    [InlineData("PULSE(0 1 0.5m 1u 1u 20m 40m)", FormeStimulus.Impulsion, "PULSE(0 1 0.0005 1E-06 1E-06 0.02 0.04)")]
    [InlineData("SIN(0 1 1k)", FormeStimulus.Sinus, "SIN(0 1 1000 0)")]
    [InlineData("AC 1", FormeStimulus.PetitSignal, "DC 0 AC 1")]
    [InlineData("DC 2.5 AC 1 90", FormeStimulus.PetitSignal, "DC 2.5 AC 1 90")]
    [InlineData("DC 5", FormeStimulus.Continu, "DC 5")]
    [InlineData("12", FormeStimulus.Continu, "DC 12")]
    [InlineData("DC 0 AC 1 PULSE(0 1 0 1n 1n 5m 10m)", FormeStimulus.Expression, "DC 0 AC 1 PULSE(0 1 0 1n 1n 5m 10m)")]
    [InlineData("PWL(0 0 1m 5)", FormeStimulus.Expression, "PWL(0 0 1m 5)")]
    public void Lecture_et_ecriture_des_stimuli(string spice, FormeStimulus forme, string attendu)
    {
        var s = Stimulus.Lire(spice);
        Assert.Equal(forme, s.Forme);
        Assert.Equal(attendu, s.VersSpice());
        Assert.Equal(s.VersSpice(), Stimulus.Lire(s.VersSpice()).VersSpice());   // stable
    }

    [Fact]
    public void Champs_en_clair_vers_spice()
    {
        var s = Stimulus.Nouveau(FormeStimulus.Sinus);
        s["va"] = 2.5; s["f"] = 50e3; s["vo"] = 1;
        Assert.Equal("SIN(1 2.5 50000 0)", s.VersSpice());
        Assert.Equal(AnalyseKind.Transitoire, s.Analyse);
        Assert.Equal(AnalyseKind.Ac, Stimulus.Nouveau(FormeStimulus.PetitSignal).Analyse);
    }

    [Fact]
    public void Le_premier_stimulus_fixe_le_type_du_banc()
    {
        var e = Silx.Etude.Etude.Demo();
        Assert.Equal(AnalyseKind.Transitoire, e.Bancs[0].Type);
        Assert.Equal(AnalyseKind.Ac, e.Bancs[1].Type);
        e.Bancs[0].Calque.Components.Single(k => k.Ref == "V1").Value = "AC 1";
        Assert.Equal(AnalyseKind.Ac, e.Bancs[0].Type);
        // Un second stimulus ne change rien : c'est le premier qui décide.
        e.Bancs[1].Calque.Components.Add(new() { Ref = "V2", Kind = ComponentKind.VoltageSource, Position = new(20, 20), Value = "SIN(0 1 1k)" });
        Assert.Equal(AnalyseKind.Ac, e.Bancs[1].Type);
    }

    [Theory]
    [InlineData("PULSE(0 1 0.5m 1u 1u 20m 40m)", "0 → 1 V, 20 ms")]
    [InlineData("SIN(0 2.5 1k)", "2,5 V crête, 1 kHz")]
    [InlineData("AC 1", "AC 1 V")]
    [InlineData("DC 5", "5 V")]
    public void Resume_lisible_sur_le_schema(string spice, string resume) => Assert.Equal(resume, Stimulus.Lire(spice).Resume());
}
