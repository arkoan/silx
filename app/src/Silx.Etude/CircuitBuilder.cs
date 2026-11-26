using System.Text;
using Silx.Core.Circuits;
namespace Silx.Etude;

/// <summary>Schéma → <see cref="Circuit"/> du socle ngspice.</summary>
public static class CircuitBuilder
{
    public static Analysis AnalysisFor(Banc e, AnalyseKind k) => k switch
    {
        // Pas automatique : pas maximal Durée/200, ngspice affine seul aux fronts ; sinon pas maximal imposé.
        AnalyseKind.Transitoire => e.Transitoire.Auto
            ? new TranAnalysis(e.Transitoire.Duree / 1000, e.Transitoire.Duree, 0, e.Transitoire.Duree / 200)
            : new TranAnalysis(e.Transitoire.Pas, e.Transitoire.Duree, 0, e.Transitoire.Pas),
        _ => new AcAnalysis(e.Ac.Lineaire ? AcSweep.Lin : AcSweep.Dec, PointsAc(e.Ac), Math.Min(e.Ac.FDebut, e.Ac.FFin), Math.Max(e.Ac.FDebut, e.Ac.FFin)),
    };

    /// <summary>
    /// Points de l'analyse AC : linéaire, au moins 2 ; en décade, assez de points par décade pour en avoir au moins 100
    /// sur la plage (une bande de 10 Hz à 50 points par décade n'en donnerait qu'un).
    /// </summary>
    public static int PointsAc(ReglagesAc a)
    {
        if (a.Lineaire) return Math.Max(a.PointsParDecade, 2);
        double decades = Math.Abs(Math.Log10(Math.Max(a.FFin, 1e-30) / Math.Max(a.FDebut, 1e-30)));
        int ppd = Math.Max(a.PointsParDecade, 1);
        return decades * ppd >= 100 || decades <= 0 ? ppd : (int)Math.Min(Math.Ceiling(100 / Math.Max(decades, 1e-9)), 1_000_000_000);
    }

    /// <summary>Construit le circuit. Suppose un ERC sans erreur (sinon le socle lève).</summary>
    public static Circuit Build(Schematic s, Connectivity c, Analysis analysis, string title = "Silx")
    {
        var circuit = new Circuit(title) { Analysis = analysis };
        foreach (var k in s.Components.Where(k => k.IsElectrical).OrderBy(k => k.Ref, StringComparer.Ordinal))
        {
            circuit.Add(Ligne(k, c));
            foreach (var a in Auxiliaires(k, c)) circuit.Add(a);
        }
        foreach (var d in Directives(s)) circuit.Directive(d);
        return circuit;
    }

    /// <summary>Netlist lisible pour la vue « Netlist », même si le schéma est incomplet.</summary>
    public static string Preview(Schematic s, Connectivity c, Analysis? analysis)
    {
        var sb = new StringBuilder("* Silx — netlist générée depuis le schéma\n");
        foreach (var k in s.Components.Where(k => k.IsElectrical).OrderBy(k => k.Ref, StringComparer.Ordinal))
        {
            sb.AppendLine(Ligne(k, c, apercu: true).ToSpiceLine());
            foreach (var a in Auxiliaires(k, c)) sb.AppendLine(a.ToSpiceLine());
        }
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
            // R, C, L : valeur saisie à l'ingénieur (« 1 MΩ », « 4,7k », « 100nF ») réécrite sans ambiguïté pour ngspice (« 1meg »).
            return new Component(k.Ref, k.Pins.Select(N).ToList(), k.Kind is ComponentKind.Resistor or ComponentKind.Capacitor or ComponentKind.Inductor
                ? SpiceNumber.NormaliserValeur(k.Value) : k.Value.Trim());
        if (d.CodeXspice is { } code)
            // Porte / bascule XSPICE : ports numériques internes, reliés au schéma par les ponts (voir Auxiliaires).
            return new Component(k.Ref, ModelesSpice.PortsXspice(code, d.BrochesModele).Select(p => p == "NULL" ? p
                : p.StartsWith('[') ? "[" + string.Join(' ', p.Trim('[', ']').Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(b => NoeudNumerique(k, b))) + "]"
                : NoeudNumerique(k, p)).ToList(), d.Modele);
        var pins = k.Pins;
        var ordre = d.OrdreSpice();
        var noeuds = d.BrochesModele.Select((n, j) => d.BrochesCachees.TryGetValue(n, out var cx) ? NoeudCache(k, n, cx)
            : ordre[j] >= 0 && ordre[j] < pins.Count ? N(pins[ordre[j]]) : apercu ? "?" : throw new InvalidOperationException($"{k.Ref} : broche du modèle non reliée")).ToList();
        return new Component(k.Ref, noeuds, d.Modele, string.IsNullOrWhiteSpace(k.Value) ? null : k.Value.Trim());
    }

    static string Propre(string s) => string.Concat(s.Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '_'));
    static string NoeudCache(PlacedComponent k, string broche, string cx) => cx.Trim() == "0" ? "0" : $"{Propre(k.Ref)}_{Propre(broche)}";

    /// <summary>
    /// Tension d'alimentation d'un composant logique : celle de l'instance (sa valeur, ex. « 3,3V » ou « VCC=3.3 »),
    /// sinon celle de sa définition (5 V par défaut).
    /// </summary>
    public static double TensionLogique(PlacedComponent k)
    {
        var v = System.Text.RegularExpressions.Regex.Replace(k.Value ?? "", @"(?i)^\s*vcc\s*=\s*", "").Trim().TrimEnd('V', 'v').Trim();
        return SpiceNumber.ParseOrNull(v) is > 0 and <= 50 and var x ? x : k.Definition?.TensionLogique ?? 5;
    }

    static string NoeudNumerique(PlacedComponent k, string broche) => $"{Propre(k.Ref)}_{Propre(broche)}_d";
    static string Famille(double vcc) => vcc.ToString("G4", System.Globalization.CultureInfo.InvariantCulture).Replace('.', 'p');

    /// <summary>
    /// Éléments ajoutés autour d'une instance : sources des broches cachées en tension fixe (« V&lt;réf&gt;_&lt;broche&gt; nœud 0 DC v ») ;
    /// pour un composant logique, un pont A→N par entrée (avec 1 GΩ vers la masse si l'entrée est en l'air) et un pont N→A
    /// suivi de 50 Ω par sortie.
    /// </summary>
    public static IEnumerable<Component> Auxiliaires(PlacedComponent k, Connectivity? c = null)
    {
        if (k.Kind != ComponentKind.Spice || k.Definition is not { } d) yield break;
        if (d.CodeXspice != null)
        {
            var pins = k.Pins; var ordre = d.OrdreSpice(); var fam = Famille(TensionLogique(k));
            for (int j = 0; j < d.BrochesModele.Count; j++)
            {
                var b = d.BrochesModele[j]; var r = Propre(k.Ref); var pb = Propre(b);
                string analog = ordre[j] >= 0 && ordre[j] < pins.Count && c != null && c.NodeOf.TryGetValue(pins[ordre[j]], out var n) ? n : $"{r}_{pb}_nc";
                if (ModelesSpice.EstSortieLogique(b))
                {
                    yield return new Component($"A{r}_{pb}_na", [$"[{NoeudNumerique(k, b)}]", $"[{r}_{pb}_o]"], $"silx_dac_{fam}");
                    yield return new Component($"R{r}_{pb}_o", [$"{r}_{pb}_o", analog], "50");
                }
                else yield return new Component($"A{r}_{pb}_an", [$"[{analog}]", $"[{NoeudNumerique(k, b)}]"], $"silx_adc_{fam}");
                // Broche en l'air (entrée ou sortie inutilisée) : 1 GΩ vers la masse, pour que le nœud ne flotte pas.
                if (c == null || !c.PinCount.TryGetValue(analog, out var nb) || nb <= 1) yield return new Component($"R{r}_{pb}_pd", [analog, "0"], "1G");
            }
            yield break;
        }
        foreach (var (b, cx) in d.BrochesCachees.Where(x => x.Value.Trim() != "0"))
            yield return new Component($"V{Propre(k.Ref)}_{Propre(b)}", [NoeudCache(k, b, cx), "0"], cx.Trim());
    }

    /// <summary>Modèles de diodes intégrés utilisés, puis texte SPICE de chaque définition importée (une fois par modèle).</summary>
    public static IEnumerable<string> Directives(Schematic s)
    {
        yield return ".options savecurrents";   // courants des composants, pour les sondes de courant
        foreach (var m in s.Components.Where(k => k.Kind == ComponentKind.Diode).Select(k => k.Value.Trim().ToUpperInvariant()).Distinct())
            if (DiodeModels.All.TryGetValue(m, out var line)) yield return line;
        foreach (var v in s.Components.Where(k => k.Kind == ComponentKind.Spice && k.Definition?.CodeXspice != null).Select(TensionLogique).Distinct())
        {
            // Ponts de la famille logique : seuils à 30 % / 70 % de Vcc, sorties 0 / Vcc, transitions de 5 ns.
            string G4(double x) => x.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
            yield return $".model silx_adc_{Famille(v)} adc_bridge(in_low={G4(0.3 * v)} in_high={G4(0.7 * v)})";
            yield return $".model silx_dac_{Famille(v)} dac_bridge(out_low=0 out_high={G4(v)} out_undef={G4(v / 2)} t_rise=5n t_fall=5n)";
        }
        foreach (var t in s.Components.Where(k => k.Kind == ComponentKind.Spice && k.Definition != null)
                     .Select(k => k.Definition!).GroupBy(d => d.Modele, StringComparer.OrdinalIgnoreCase).Select(g => g.First().TexteSpice))
            yield return t;
    }

    static string G(double d) => d.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
}
