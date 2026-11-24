namespace Silx.Etude;

public enum Severity { Warning, Error }
public sealed record ErcIssue(Severity Severity, string Message);

/// <summary>Vérification de cohérence électrique avant simulation (spec §3.1).</summary>
public static class Erc
{
    /// <param name="circuitSeul">Circuit sous test sans banc : pas de masse ni de source exigées, broches ouvertes tolérées sur les nœuds étiquetés (points de connexion du banc).</param>
    public static IReadOnlyList<ErcIssue> Check(Schematic s, Connectivity c, bool circuitSeul = false)
    {
        var issues = new List<ErcIssue>();
        foreach (var g in s.Components.Where(k => k.IsElectrical).GroupBy(x => x.Ref, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add(new(Severity.Error, $"Référence dupliquée : {g.Key}"));
        var elec = s.Components.Where(k => k.IsElectrical).ToList();
        if (elec.Count == 0) { issues.Add(new(Severity.Warning, "Schéma vide")); return issues; }
        if (!circuitSeul && !s.Components.Any(k => k.Kind == ComponentKind.Ground))
            issues.Add(new(Severity.Error, "Aucune masse (GND) : le nœud 0 est requis par SPICE"));
        foreach (var k in elec)
        {
            if (k.Kind == ComponentKind.Spice)
            {
                if (k.Definition == null) issues.Add(new(Severity.Error, $"{k.Ref} : définition du composant introuvable"));
                else foreach (var i in k.Definition.Verifier().Where(i => i.Severity == Severity.Error))
                    issues.Add(new(Severity.Error, $"{k.Ref} ({k.Definition.Nom}) : {i.Message}"));
            }
            else if (string.IsNullOrWhiteSpace(k.Value)) issues.Add(new(Severity.Error, $"{k.Ref} : valeur manquante"));
            if (!k.Ref.StartsWith(k.Prefix, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(Severity.Error, $"{k.Ref} : la référence doit commencer par « {k.Prefix} » (convention SPICE)"));
            if (k.Kind == ComponentKind.Diode && !DiodeModels.All.ContainsKey(k.Value.Trim()))
                issues.Add(new(Severity.Error, $"{k.Ref} : modèle de diode inconnu « {k.Value} »"));
            for (int i = 0; i < k.Pins.Count; i++)
            {
                var node = c.NodeOf[k.Pins[i]];
                if (k.Kind == ComponentKind.Spice && k.Definition?.Symbole.Broches[i].BrocheModele == null) continue;
                if (c.PinCount[node] == 1 && node != "0" && !(circuitSeul && c.NamedNodes.Contains(node)))
                    issues.Add(new(Severity.Warning, $"{k.Ref} broche {NomBroche(k, i)} non connectée (nœud {node} flottant)"));
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
        foreach (var g in s.Components.Where(k => k.Kind == ComponentKind.Spice && k.Definition != null).Select(k => k.Definition!)
                     .GroupBy(d => d.Modele, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(d => d.TexteSpice).Distinct().Count() > 1))
            issues.Add(new(Severity.Warning, $"Deux définitions différentes du modèle « {g.Key} » : seule la première est écrite dans la netlist"));
        return issues;
    }

    static string NomBroche(PlacedComponent k, int i)
        => k.Kind == ComponentKind.Spice && k.Definition?.Symbole.Broches[i].BrocheModele is { } n ? n : (i + 1).ToString();
}
