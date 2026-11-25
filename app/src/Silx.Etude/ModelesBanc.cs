namespace Silx.Etude;

/// <summary>Modèle de banc de test : un stimulus prêt à brancher et un premier critère sur la sortie.</summary>
public enum ModeleBanc { Echelon, Sinus, Bode, Vide }

public static class ModelesBanc
{
    public static string Libelle(ModeleBanc m) => m switch
    {
        ModeleBanc.Echelon => "Échelon (réponse indicielle)", ModeleBanc.Sinus => "Sinus", ModeleBanc.Bode => "Réponse en fréquence (Bode)", _ => "Banc vide"
    };

    /// <summary>
    /// Crée un banc : source sur le nœud d'entrée (référencée à la masse), placée à gauche du point d'entrée et reliée par un fil,
    /// à un endroit qui ne touche aucun autre nœud ; premier critère sur la sortie (sans borne : la mesure s'affiche).
    /// </summary>
    public static Banc Creer(Etude e, ModeleBanc m, string? entree, string? sortie, string nom)
    {
        var b = new Banc { Nom = nom, TypeParDefaut = m == ModeleBanc.Bode ? AnalyseKind.Ac : AnalyseKind.Transitoire };
        if (m == ModeleBanc.Vide || entree == null) return b;
        var valeur = m switch
        {
            ModeleBanc.Echelon => "PULSE(0 1 0.0005 1e-06 1e-06 1 2)",
            ModeleBanc.Sinus => "SIN(0 1 1000 0)",
            _ => "DC 0 AC 1",
        };
        if (m == ModeleBanc.Sinus) { b.Transitoire.Duree = 5e-3; b.Transitoire.Pas = 2e-6; }
        if (!PoserSource(e, b, entree, valeur)) return b;
        if (sortie != null)
            b.Gabarits.Add(m switch
            {
                ModeleBanc.Echelon => new CritereGabarit { Mesure = TypeMesure.TempsDeMontee, Noeud = sortie },
                ModeleBanc.Bode => new CritereGabarit { Mesure = TypeMesure.BandePassante3dB, Noeud = sortie },
                _ => new CritereGabarit { Mesure = TypeMesure.ValeurFinale, Noeud = sortie },
            });
        if (m == ModeleBanc.Sinus) b.Gabarits.Clear();   // pas de mesure scalaire évidente : les mesures par trace suffisent
        var noeuds = new[] { entree, sortie }.OfType<string>().Distinct().ToList();
        b.Graphiques.Add(noeuds);
        return b;
    }

    /// <summary>Pose « V1 » (+ sur l'entrée, − à la masse) et sa masse, à gauche du point d'entrée, sans court-circuiter d'autre nœud.</summary>
    public static bool PoserSource(Etude e, Banc b, string entree, string valeur)
    {
        var conn = Connectivity.Build(e.Schema);
        var points = conn.NodeOf.Where(kv => string.Equals(kv.Value, entree, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        int noeudsAvant = conn.AllNodes.Count;
        foreach (var p in points)
            foreach (var dx in new[] { 6, 10, 14, 18 })
                foreach (var dy in new[] { 0, -4, 4, -8, 8 })
                {
                    var q = new GridPoint(p.X - dx, p.Y + dy);
                    var essai = new Schematic();
                    essai.Components.Add(new PlacedComponent { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = q, Rotation = 1, Value = valeur });
                    essai.Components.Add(new PlacedComponent { Ref = "GNDB1", Kind = ComponentKind.Ground, Position = new GridPoint(q.X, q.Y + 4) });
                    if (dy != 0) { essai.Wires.Add(new(q, new GridPoint(q.X, p.Y))); essai.Wires.Add(new(new GridPoint(q.X, p.Y), p)); }
                    else essai.Wires.Add(new(q, p));
                    var banc = new Banc { Calque = essai };
                    var f = banc.Fusion(e.Schema); var c = Connectivity.Build(f);
                    // Valide si : + sur l'entrée, − à la masse, et aucun autre nœud fusionné ou créé.
                    if (c.NodeOf.GetValueOrDefault(q) is { } np && string.Equals(np, entree, StringComparison.OrdinalIgnoreCase)
                        && c.NodeOf.GetValueOrDefault(new GridPoint(q.X, q.Y + 4)) == "0" && c.AllNodes.Count == noeudsAvant
                        && !Erc.Check(f, c).Any(i => i.Severity == Severity.Error))
                    {
                        b.Calque.Components.AddRange(essai.Components); b.Calque.Wires.AddRange(essai.Wires);
                        return true;
                    }
                }
        return false;
    }
}
