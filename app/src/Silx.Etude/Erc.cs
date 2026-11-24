namespace Silx.Etude;

public enum Severity { Warning, Error }
public sealed record ErcIssue(Severity Severity, string Message);

/// <summary>Vérification de cohérence électrique avant simulation (spec §3.1).</summary>
public static class Erc
{
    public static IReadOnlyList<ErcIssue> Check(Schematic s, Connectivity c)
    {
        var issues = new List<ErcIssue>();
        foreach (var g in s.Components.Where(k => k.IsElectrical).GroupBy(x => x.Ref, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(new(Severity.Error, $"Référence dupliquée : {g.Key}"));
        var elec = s.Components.Where(k => k.IsElectrical).ToList();
        if (elec.Count == 0) { issues.Add(new(Severity.Warning, "Schéma vide")); return issues; }
        if (!s.Components.Any(k => k.Kind == ComponentKind.Ground))
            issues.Add(new(Severity.Error, "Aucune masse (GND) : le nœud 0 est requis par SPICE"));
        foreach (var k in elec)
        {
            if (string.IsNullOrWhiteSpace(k.Value)) issues.Add(new(Severity.Error, $"{k.Ref} : valeur manquante"));
            if (!k.Ref.StartsWith(Schematic.Prefix(k.Kind), StringComparison.OrdinalIgnoreCase))
                issues.Add(new(Severity.Error, $"{k.Ref} : la référence doit commencer par « {Schematic.Prefix(k.Kind)} » (convention SPICE)"));
            if (k.Kind == ComponentKind.Diode && !DiodeModels.All.ContainsKey(k.Value.Trim()))
                issues.Add(new(Severity.Error, $"{k.Ref} : modèle de diode inconnu « {k.Value} »"));
            for (int i = 0; i < k.Pins.Count; i++)
            {
                var node = c.NodeOf[k.Pins[i]];
                if (c.PinCount[node] == 1 && node != "0")
                    issues.Add(new(Severity.Warning, $"{k.Ref} broche {i + 1} non connectée (nœud {node} flottant)"));
            }
        }
        foreach (var l in s.Components.Where(k => k.Kind == ComponentKind.Label))
        {
            var v = l.Value.Trim();
            if (v.Length == 0 || !v.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') || v == "0")
                issues.Add(new(Severity.Error, $"Étiquette « {l.Value} » : lettres, chiffres et _ uniquement"));
            else if (c.NodeOf.TryGetValue(l.Pins[0], out var n) && !string.Equals(n, v, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(Severity.Warning, $"Étiquette « {v} » posée sur le nœud déjà nommé « {n} »"));
        }
        return issues;
    }
}
