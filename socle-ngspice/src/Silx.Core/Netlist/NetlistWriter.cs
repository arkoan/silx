using System.Globalization;
using System.Text;
using Silx.Core.Circuits;

namespace Silx.Core.Netlist;

public static class NetlistWriter
{
    private static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    public static string Write(Circuit c)
    {
        Validate(c);
        var sb = new StringBuilder();
        sb.Append("* ").AppendLine(c.Title.ReplaceLineEndings(" "));
        foreach (var comp in c.Components) sb.AppendLine(comp.ToSpiceLine());
        foreach (var d in c.Directives) sb.AppendLine(d);
        if (c.Saves.Count > 0) sb.Append(".save ").AppendLine(string.Join(' ', c.Saves));
        sb.AppendLine(c.Analysis switch
        {
            OpAnalysis => ".op",
            DcAnalysis a => $".dc {a.Source} {F(a.Start)} {F(a.Stop)} {F(a.Step)}",
            AcAnalysis a => $".ac {a.Sweep.ToString().ToLowerInvariant()} {a.PointsPerSweep} {F(a.FStart)} {F(a.FStop)}",
            NoiseAnalysis a => $".noise v({a.Output}{(a.Reference is { } r ? "," + r : "")}) {a.Source} {a.Sweep.ToString().ToLowerInvariant()} {a.PointsPerSweep} {F(a.FStart)} {F(a.FStop)}",
            TranAnalysis a => $".tran {F(a.Step)} {F(a.Stop)} {F(a.Start)}" + (a.MaxStep is { } m ? " " + F(m) : ""),
            null => throw new InvalidOperationException("Aucune analyse définie."),
            _ => throw new NotSupportedException()
        });
        sb.AppendLine(".end");
        return sb.ToString();
    }

    /// <summary>
    /// Nœuds d'un port : un port XSPICE peut grouper plusieurs nœuds entre crochets (« [a b] ») ; « NULL » (port inutilisé)
    /// n'est pas un nœud.
    /// </summary>
    static IEnumerable<string> Noeuds(string port) => port.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(n => n.Trim('[', ']', '~')).Where(n => n.Length > 0 && !n.Equals("NULL", StringComparison.OrdinalIgnoreCase));

    /// <summary>Vérifications basiques (cf. spec §3.1) : masse présente, nœuds à une seule connexion.</summary>
    public static void Validate(Circuit c)
    {
        if (c.Components.Count == 0) throw new InvalidOperationException("Circuit vide.");
        var counts = c.Components.SelectMany(x => x.Nodes).SelectMany(Noeuds).GroupBy(n => n.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count());
        if (!counts.ContainsKey("0") && !counts.ContainsKey("gnd"))
            throw new InvalidOperationException("Pas de nœud de masse (0).");
        var floating = counts.Where(kv => kv.Value == 1 && kv.Key is not "0").Select(kv => kv.Key).ToList();
        if (floating.Count > 0)
            throw new InvalidOperationException("Nœud(s) flottant(s) : " + string.Join(", ", floating));
    }
}
