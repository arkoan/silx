using System.Text;
namespace Silx.Core;

public enum Severity { Warning, Error }
public sealed record ErcIssue(Severity Severity, string Message);

public sealed record NetlistResult(string Text, IReadOnlyList<ErcIssue> Issues);

public static class NetlistGenerator
{
    public static IReadOnlyList<ErcIssue> Check(Schematic s, Connectivity c)
    {
        var issues = new List<ErcIssue>();
        foreach (var g in s.Components.GroupBy(x => x.Ref).Where(g => g.Count() > 1))
            issues.Add(new(Severity.Error, $"Référence dupliquée : {g.Key}"));
        if (s.Components.Count(k => k.Kind != ComponentKind.Ground) == 0)
            issues.Add(new(Severity.Warning, "Schéma vide"));
        else if (!s.Components.Any(k => k.Kind == ComponentKind.Ground))
            issues.Add(new(Severity.Error, "Aucune masse (GND) : le nœud 0 est requis par SPICE"));
        foreach (var k in s.Components.Where(k => k.Kind != ComponentKind.Ground))
        {
            if (string.IsNullOrWhiteSpace(k.Value)) issues.Add(new(Severity.Error, $"{k.Ref} : valeur manquante"));
            for (int i = 0; i < k.Pins.Count; i++)
            {
                var node = c.NodeOf[k.Pins[i]];
                // Nœud flottant : une seule broche de composant et rien d'autre (ni fil, ni masse).
                if (c.PinCount[node] == 1 && node != "0")
                    issues.Add(new(Severity.Warning, $"{k.Ref} broche {i + 1} non connectée (nœud {node} flottant)"));
            }
        }
        return issues;
    }

    /// <summary>Génère la netlist SPICE. <paramref name="analysis"/> ex. ".tran 10u 20m".</summary>
    public static NetlistResult Generate(Schematic s, string title = "Silx", string? analysis = null)
    {
        var c = Connectivity.Build(s);
        var issues = Check(s, c);
        var sb = new StringBuilder();
        sb.AppendLine($"* {title}");
        foreach (var k in s.Components.Where(k => k.Kind != ComponentKind.Ground).OrderBy(k => k.Ref, StringComparer.Ordinal))
        {
            var p = k.Pins;
            sb.AppendLine($"{k.Ref} {c.NodeOf[p[0]]} {c.NodeOf[p[1]]} {k.Value}");
        }
        if (analysis != null) sb.AppendLine(analysis);
        sb.AppendLine(".end");
        return new(sb.ToString(), issues);
    }
}
