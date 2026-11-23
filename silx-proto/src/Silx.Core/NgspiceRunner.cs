using System.Diagnostics;
using System.Globalization;
namespace Silx.Core;

public sealed record Trace(string Name, double[] X, double[] Y);

/// <summary>Lance ngspice en CLI (<c>ngspice -b</c>) et relit les courbes via wrdata.</summary>
public static class NgspiceRunner
{
    public static async Task<IReadOnlyList<Trace>> RunTranAsync(string netlistWithoutEnd, IEnumerable<string> nodes,
        string tran, CancellationToken ct = default)
    {
        var dir = Directory.CreateTempSubdirectory("silx").FullName;
        try
        {
            var nodeList = nodes.ToList();
            var outFile = Path.Combine(dir, "out.txt");
            var deck = netlistWithoutEnd.Replace(".end", "").TrimEnd() + "\n"
                + $".control\nset wr_vecnames\noption numdgt=7\ntran {tran}\nwrdata {outFile} {string.Join(' ', nodeList.Select(n => $"v({n})"))}\n.endc\n.end\n";
            var cir = Path.Combine(dir, "deck.cir");
            await File.WriteAllTextAsync(cir, deck, ct);
            var psi = new ProcessStartInfo("ngspice", $"-b \"{cir}\"") { RedirectStandardOutput = true, RedirectStandardError = true };
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("ngspice introuvable");
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (!File.Exists(outFile)) throw new InvalidOperationException("ngspice n'a pas produit de résultat :\n" + await stdout + await stderr);
            return Parse(File.ReadAllLines(outFile), nodeList);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // wrdata : une paire (x, y) par vecteur sur chaque ligne ; en-tête avec noms (wr_vecnames).
    static List<Trace> Parse(string[] lines, List<string> nodes)
    {
        var rows = lines.Skip(1).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray()).Where(r => r.Length >= 2 * nodes.Count).ToList();
        return nodes.Select((n, i) => new Trace($"v({n})", rows.Select(r => r[2 * i]).ToArray(), rows.Select(r => r[2 * i + 1]).ToArray())).ToList();
    }
}
