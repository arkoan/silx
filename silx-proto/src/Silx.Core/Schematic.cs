namespace Silx.Core;

/// <summary>Point sur la grille (coordonnées entières, 1 unité = 1 pas de grille).</summary>
public readonly record struct GridPoint(int X, int Y);

public enum ComponentKind { Resistor, Capacitor, Inductor, VoltageSource, Ground }

/// <summary>Composant placé. Les broches sont définies par le type, la rotation (0..3 quarts de tour) et la position.</summary>
public sealed class PlacedComponent
{
    public required string Ref { get; set; }
    public required ComponentKind Kind { get; init; }
    public GridPoint Position { get; set; }
    public int Rotation { get; set; }
    /// <summary>Valeur SPICE brute (ex. "10k", "100n", "SIN(0 1 1k)"). Vide pour GND.</summary>
    public string Value { get; set; } = "";

    // HYPOTHÈSE : composants 2 broches, broche 1 en (0,0), broche 2 à 4 pas (axe X avant rotation). GND : 1 broche en (0,0).
    public IReadOnlyList<GridPoint> Pins
    {
        get
        {
            var local = Kind == ComponentKind.Ground
                ? new[] { new GridPoint(0, 0) }
                : new[] { new GridPoint(0, 0), new GridPoint(4, 0) };
            return local.Select(p => Rotate(p, Rotation)).Select(p => new GridPoint(p.X + Position.X, p.Y + Position.Y)).ToArray();
        }
    }

    static GridPoint Rotate(GridPoint p, int q)
    {
        for (int i = 0; i < ((q % 4) + 4) % 4; i++) p = new GridPoint(-p.Y, p.X);
        return p;
    }

    public PlacedComponent Clone() => new() { Ref = Ref, Kind = Kind, Position = Position, Rotation = Rotation, Value = Value };
}

/// <summary>Fil : segment orthogonal ou quelconque entre deux points de grille.</summary>
public sealed record Wire(GridPoint A, GridPoint B);

public sealed class Schematic
{
    public List<PlacedComponent> Components { get; init; } = new();
    public List<Wire> Wires { get; init; } = new();

    public Schematic Clone() => new()
    {
        Components = Components.Select(c => c.Clone()).ToList(),
        Wires = Wires.ToList()
    };

    public static string Prefix(ComponentKind k) => k switch
    {
        ComponentKind.Resistor => "R", ComponentKind.Capacitor => "C", ComponentKind.Inductor => "L",
        ComponentKind.VoltageSource => "V", _ => "GND"
    };

    /// <summary>Numérotation automatique R1, R2... (premier numéro libre).</summary>
    public string NextRef(ComponentKind k)
    {
        var p = Prefix(k);
        var used = Components.Select(c => c.Ref).ToHashSet();
        for (int i = 1; ; i++) if (!used.Contains(p + i)) return p + i;
    }

    public static string DefaultValue(ComponentKind k) => k switch
    {
        ComponentKind.Resistor => "1k", ComponentKind.Capacitor => "100n", ComponentKind.Inductor => "1m",
        ComponentKind.VoltageSource => "PULSE(0 1 0 1n 1n 5m 10m)", _ => ""
    };
}
