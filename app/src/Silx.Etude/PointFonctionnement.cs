using Silx.Core.Circuits;
using Silx.Core.Raw;
namespace Silx.Etude;

/// <summary>Point de fonctionnement continu (.op) : tension de chaque nœud et courant de chaque composant.</summary>
public sealed record ResultatOp(IReadOnlyDictionary<string, double> Tensions, IReadOnlyDictionary<string, double> Courants, string? Erreur)
{
    /// <summary>Courant d'un composant (entrant par sa broche 1 ; collecteur / drain pour un transistor), null si inconnu.</summary>
    public double? Courant(string reference) => Courants.TryGetValue(reference, out var i) ? i : null;
}

public static class PointFonctionnement
{
    /// <summary>
    /// Calcule le point de fonctionnement du circuit seul ou avec le banc <paramref name="banc"/> branché (stimuli à leur
    /// valeur continue : début d'un balayage DC, valeur à t = 0 d'une impulsion, polarisation d'un petit signal).
    /// </summary>
    public static async Task<ResultatOp> CalculerAsync(Etude circuit, Banc? banc, CancellationToken ct = default)
    {
        var schema = banc?.Fusion(circuit.Schema) ?? circuit.Schema;
        var conn = Connectivity.Build(schema);
        if (Erc.Check(schema, conn).Any(i => i.Severity == Severity.Error))
            return new(new Dictionary<string, double>(), new Dictionary<string, double>(), "Point de fonctionnement impossible : corrigez d'abord les erreurs du schéma.");
        var exe = Verification.NgspiceExecutable();
        var runner = new Silx.Core.Simulation.NgspiceRunner { Executable = exe, Environnement = NgspiceEmbarque.Environnement(exe), Compatibilite = schema.Components.Any(k => k.Kind == ComponentKind.Spice) ? "psa" : null };
        try
        {
            var r = await runner.RunAsync(CircuitBuilder.Build(schema, conn, new OpAnalysis(), "Silx point de fonctionnement"), ct);
            return Lire(r.Raw, schema);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(new Dictionary<string, double>(), new Dictionary<string, double>(), ex.Message); }
    }

    public static ResultatOp Lire(RawFile raw, Schematic schema)
    {
        var tensions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["0"] = 0 };
        foreach (var v in raw.Variables)
            if (v.Name.StartsWith("v(", StringComparison.OrdinalIgnoreCase) && v.Name.EndsWith(')'))
                tensions[v.Name[2..^1]] = raw.Vector(v.Name)[0];
        var courants = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in schema.Components)
        {
            // Broche 1 pour un dipôle ; collecteur / drain (broche du rôle C ou D) pour un transistor.
            int broche = 0;
            if (k.Kind == ComponentKind.Spice && k.Definition is { Lettre: "Q" or "M" or "J" } d)
                broche = Math.Max(0, d.Symbole.Broches.FindIndex(b => (b.Role ?? b.BrocheModele ?? "").ToUpperInvariant() is "C" or "D"));
            if (Signaux.Vecteur(Signaux.Courant(k, broche), schema) is { } vs
                && raw.Variables.Any(x => string.Equals(x.Name, vs.Vecteur, StringComparison.OrdinalIgnoreCase)))
                courants[k.Ref] = raw.Vector(vs.Vecteur)[0] * vs.Signe;
        }
        return new(tensions, courants, null);
    }
}
