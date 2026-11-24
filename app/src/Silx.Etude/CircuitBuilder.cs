using System.Text;
using Silx.Core.Circuits;
namespace Silx.Etude;

/// <summary>Schéma → <see cref="Circuit"/> du socle ngspice.</summary>
public static class CircuitBuilder
{
    public static Analysis AnalysisFor(Etude e, AnalyseKind k) => k switch
    {
        AnalyseKind.Transitoire => new TranAnalysis(e.Transitoire.Pas, e.Transitoire.Duree, 0, e.Transitoire.Pas),
        _ => new AcAnalysis(AcSweep.Dec, e.Ac.PointsParDecade, e.Ac.FDebut, e.Ac.FFin),
    };

    /// <summary>Construit le circuit. Suppose un ERC sans erreur (sinon le socle lève).</summary>
    public static Circuit Build(Schematic s, Connectivity c, Analysis analysis, string title = "Silx")
    {
        var circuit = new Circuit(title) { Analysis = analysis };
        foreach (var k in s.Components.Where(k => k.IsElectrical).OrderBy(k => k.Ref, StringComparer.Ordinal))
            circuit.Add(new Component(k.Ref, k.Pins.Select(p => c.NodeOf[p]).ToList(), k.Value.Trim()));
        foreach (var m in s.Components.Where(k => k.Kind == ComponentKind.Diode).Select(k => k.Value.Trim().ToUpperInvariant()).Distinct())
            if (DiodeModels.All.TryGetValue(m, out var line)) circuit.Directive(line);
        return circuit;
    }

    /// <summary>Netlist lisible pour la vue « Netlist », même si le schéma est incomplet.</summary>
    public static string Preview(Schematic s, Connectivity c, Analysis analysis)
    {
        var sb = new StringBuilder("* Silx — netlist générée depuis le schéma\n");
        foreach (var k in s.Components.Where(k => k.IsElectrical).OrderBy(k => k.Ref, StringComparer.Ordinal))
            sb.AppendLine(new Component(k.Ref, k.Pins.Select(p => c.NodeOf[p]).ToList(), k.Value.Trim()).ToSpiceLine());
        foreach (var m in s.Components.Where(k => k.Kind == ComponentKind.Diode).Select(k => k.Value.Trim().ToUpperInvariant()).Distinct())
            if (DiodeModels.All.TryGetValue(m, out var line)) sb.AppendLine(line);
        sb.AppendLine(analysis switch
        {
            TranAnalysis t => $".tran {G(t.Step)} {G(t.Stop)}",
            AcAnalysis a => $".ac {a.Sweep.ToString().ToLowerInvariant()} {a.PointsPerSweep} {G(a.FStart)} {G(a.FStop)}",
            _ => ".op",
        });
        sb.AppendLine(".end");
        return sb.ToString();
    }

    static string G(double d) => d.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
}
