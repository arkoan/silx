using System.Text;
using Silx.Core.Circuits;
namespace Silx.Etude;

/// <summary>Schéma → <see cref="Circuit"/> du socle ngspice.</summary>
public static class CircuitBuilder
{
    public static Analysis AnalysisFor(Banc e, AnalyseKind k) => k switch
    {
        AnalyseKind.Transitoire => new TranAnalysis(e.Transitoire.Pas, e.Transitoire.Duree, 0, e.Transitoire.Pas),
        _ => new AcAnalysis(AcSweep.Dec, e.Ac.PointsParDecade, e.Ac.FDebut, e.Ac.FFin),
    };

    /// <summary>Construit le circuit. Suppose un ERC sans erreur (sinon le socle lève).</summary>
    public static Circuit Build(Schematic s, Connectivity c, Analysis analysis, string title = "Silx")
    {
        var circuit = new Circuit(title) { Analysis = analysis };
        foreach (var k in s.Components.Where(k => k.IsElectrical).OrderBy(k => k.Ref, StringComparer.Ordinal))
            circuit.Add(Ligne(k, c));
        foreach (var d in Directives(s)) circuit.Directive(d);
        return circuit;
    }

    /// <summary>Netlist lisible pour la vue « Netlist », même si le schéma est incomplet.</summary>
    public static string Preview(Schematic s, Connectivity c, Analysis? analysis)
    {
        var sb = new StringBuilder("* Silx — netlist générée depuis le schéma\n");
        foreach (var k in s.Components.Where(k => k.IsElectrical).OrderBy(k => k.Ref, StringComparer.Ordinal))
            sb.AppendLine(Ligne(k, c, apercu: true).ToSpiceLine());
        foreach (var d in Directives(s)) sb.AppendLine(d);
        if (analysis == null) { sb.AppendLine("* (circuit sous test seul : pas de stimulus ni d'analyse ; voir les bancs de test)"); sb.AppendLine(".end"); return sb.ToString(); }
        sb.AppendLine(analysis switch
        {
            TranAnalysis t => $".tran {G(t.Step)} {G(t.Stop)}",
            AcAnalysis a => $".ac {a.Sweep.ToString().ToLowerInvariant()} {a.PointsPerSweep} {G(a.FStart)} {G(a.FStop)}",
            _ => ".op",
        });
        sb.AppendLine(".end");
        return sb.ToString();
    }

    /// <summary>
    /// Ligne d'instance. Composant SPICE importé : nœuds dans l'ordre des broches du modèle (via la correspondance du symbole),
    /// puis le nom du modèle, puis les paramètres d'instance.
    /// </summary>
    public static Component Ligne(PlacedComponent k, Connectivity c, bool apercu = false)
    {
        string N(GridPoint p) => c.NodeOf.TryGetValue(p, out var n) ? n : "?";
        if (k.Kind != ComponentKind.Spice || k.Definition is not { } d)
            return new Component(k.Ref, k.Pins.Select(N).ToList(), k.Value.Trim());
        var pins = k.Pins;
        var noeuds = d.OrdreSpice().Select(i => i >= 0 && i < pins.Count ? N(pins[i]) : apercu ? "?" : throw new InvalidOperationException($"{k.Ref} : broche du modèle non reliée")).ToList();
        return new Component(k.Ref, noeuds, d.Modele, string.IsNullOrWhiteSpace(k.Value) ? null : k.Value.Trim());
    }

    /// <summary>Modèles de diodes intégrés utilisés, puis texte SPICE de chaque définition importée (une fois par modèle).</summary>
    public static IEnumerable<string> Directives(Schematic s)
    {
        foreach (var m in s.Components.Where(k => k.Kind == ComponentKind.Diode).Select(k => k.Value.Trim().ToUpperInvariant()).Distinct())
            if (DiodeModels.All.TryGetValue(m, out var line)) yield return line;
        foreach (var t in s.Components.Where(k => k.Kind == ComponentKind.Spice && k.Definition != null)
                     .Select(k => k.Definition!).GroupBy(d => d.Modele, StringComparer.OrdinalIgnoreCase).Select(g => g.First().TexteSpice))
            yield return t;
    }

    static string G(double d) => d.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
}
