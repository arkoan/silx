using Silx.Core;
using Xunit;
namespace Silx.Tests;

public class CoreTests
{
    [Fact]
    public void Netlist_reconnait_jonctions_et_masse()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 0), Value = "1k" });
        s.Components.Add(new() { Ref = "R2", Kind = ComponentKind.Resistor, Position = new(8, 0), Value = "2k" });
        s.Wires.Add(new(new(4, 0), new(8, 0)));
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(12, 0) });
        s.Wires.Add(new(new(12, 0), new(12, 0))); // fil dégénéré toléré
        s.Components.First(k => k.Ref == "R2").Rotation = 0; // pins (8,0),(12,0)
        var r = NetlistGenerator.Generate(s);
        var c = Connectivity.Build(s);
        Assert.Equal(c.NodeOf[new GridPoint(4, 0)], c.NodeOf[new GridPoint(8, 0)]);   // R1.2 = R2.1 via le fil
        Assert.Equal("0", c.NodeOf[new GridPoint(12, 0)]);
        Assert.Contains("R1 N001 N002 1k", r.Text);
        Assert.Contains("R2 N002 0 2k", r.Text);
    }

    [Fact]
    public void Erc_detecte_pas_de_masse_et_broche_flottante_et_doublon()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 0), Value = "1k" });
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 8), Value = "1k" });
        var r = NetlistGenerator.Generate(s);
        Assert.Contains(r.Issues, i => i.Message.Contains("dupliquée"));
        Assert.Contains(r.Issues, i => i.Message.Contains("Aucune masse"));
        Assert.Contains(r.Issues, i => i.Message.Contains("non connectée"));
    }

    [Fact]
    public void Jonction_en_T_connecte_broche_sur_fil()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 0), Value = "1k" }); // (0,0)-(4,0)
        s.Components.Add(new() { Ref = "R2", Kind = ComponentKind.Resistor, Position = new(2, 4), Rotation = 3, Value = "1k" }); // pins (2,4),(2,0)
        s.Wires.Add(new(new(0, 0), new(4, 0)));
        var c = Connectivity.Build(s);
        Assert.Equal(c.NodeOf[new GridPoint(2, 0)], c.NodeOf[new GridPoint(0, 0)]);
    }

    [Fact]
    public async Task Rc_ngspice_charge_vers_valeur_analytique()
    {
        // V1 + en (0,0)? On construit explicitement : source pins (0,0)=+ et (0,4)=-, R de + vers out, C de out vers GND.
        var s = new Schematic();
        s.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(0, 4), Rotation = 3, Value = "PULSE(0 1 0 1n 1n 1 2)" }); // pins (0,4),(0,0): + en (0,4)
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(0, 4), Value = "1k" });  // (0,4)->(4,4)
        s.Components.Add(new() { Ref = "C1", Kind = ComponentKind.Capacitor, Position = new(4, 4), Rotation = 1, Value = "1u" }); // (4,4)->(4,8)
        s.Wires.Add(new(new(0, 0), new(4, 0)));
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(0, 0) });
        s.Wires.Add(new(new(4, 8), new(0, 0)));
        var res = NetlistGenerator.Generate(s);
        Assert.DoesNotContain(res.Issues, i => i.Severity == Severity.Error);
        var outNode = Connectivity.Build(s).NodeOf[new GridPoint(4, 4)];
        var tr = (await NgspiceRunner.RunTranAsync(res.Text, new[] { outNode }, "10u 5m"))[0];
        // Tau = RC = 1 ms ; v(1 ms) = 1 - e^-1 = 0.6321
        Assert.True(tr.Y.Length > 100);
        double v1ms = tr.Y[Array.FindIndex(tr.X, t => t >= 1e-3)];
        Assert.InRange(v1ms, 0.62, 0.645);
    }
}
