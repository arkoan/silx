namespace Silx.Etude;

/// <summary>Point sur la grille (coordonnées entières, 1 unité = 1 pas de grille).</summary>
public readonly record struct GridPoint(int X, int Y);

public enum ComponentKind { Resistor, Capacitor, Inductor, VoltageSource, CurrentSource, Diode, Ground, Label, Spice }

/// <summary>Composant placé. Les broches sont définies par le type, la rotation (0..3 quarts de tour) et la position.</summary>
public sealed class PlacedComponent
{
    public required string Ref { get; set; }
    public required ComponentKind Kind { get; init; }
    public GridPoint Position { get; set; }
    public int Rotation { get; set; }
    /// <summary>Valeur SPICE brute (ex. "10k", "SIN(0 1 1k)"), nom de modèle pour une diode, nom de nœud pour une étiquette. Vide pour GND.</summary>
    public string Value { get; set; } = "";
    /// <summary>Composant SPICE importé (<see cref="ComponentKind.Spice"/>) : définition (modèle, symbole, correspondance des broches).
    /// Value contient alors les paramètres d'instance facultatifs (ex. « W=2u L=180n »).</summary>
    public DefinitionComposant? Definition { get; set; }

    /// <summary>Vrai si le composant produit une ligne de netlist (ni masse, ni étiquette).</summary>
    public bool IsElectrical => Kind is not (ComponentKind.Ground or ComponentKind.Label);

    // HYPOTHÈSE : composants 2 broches, broche 1 en (0,0), broche 2 à 4 pas (axe X avant rotation). GND et étiquette : 1 broche en (0,0).
    public IReadOnlyList<GridPoint> Pins
    {
        get
        {
            var local = Kind == ComponentKind.Spice
                ? (Definition?.Symbole.Broches.Select(b => new GridPoint(b.X, b.Y)).ToArray() ?? [])
                : IsElectrical
                ? new[] { new GridPoint(0, 0), new GridPoint(4, 0) }
                : new[] { new GridPoint(0, 0) };
            return local.Select(p => Miroir ? new GridPoint(-p.X, p.Y) : p).Select(p => Rotate(p, Rotation)).Select(p => new GridPoint(p.X + Position.X, p.Y + Position.Y)).ToArray();
        }
    }

    public static GridPoint Rotate(GridPoint p, int q)
    {
        for (int i = 0; i < ((q % 4) + 4) % 4; i++) p = new GridPoint(-p.Y, p.X);
        return p;
    }

    /// <summary>Symétrie gauche-droite du symbole (appliquée avant la rotation).</summary>
    public bool Miroir { get; set; }
    /// <summary>Tolérance en % (± autour de la valeur) pour les campagnes ; null = tolérance par défaut du type.</summary>
    public double? Tolerance { get; set; }
    public PlacedComponent Clone() => new() { Ref = Ref, Kind = Kind, Position = Position, Rotation = Rotation, Miroir = Miroir, Value = Value, Definition = Definition, Tolerance = Tolerance };

    /// <summary>Lettre SPICE attendue en tête de référence.</summary>
    public string Prefix => Kind == ComponentKind.Spice ? Definition?.Lettre ?? "X" : Schematic.Prefix(Kind);
}

/// <summary>Fil : segment entre deux points de grille.</summary>
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
        ComponentKind.VoltageSource => "V", ComponentKind.CurrentSource => "I", ComponentKind.Diode => "D",
        ComponentKind.Label => "NET", ComponentKind.Spice => "X", _ => "GND"
    };

    /// <summary>Numérotation automatique R1, R2... (premier numéro libre).</summary>
    public string NextRef(ComponentKind k) => NextRef(Prefix(k));

    public string NextRef(string p)
    {
        var used = Components.Select(c => c.Ref).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++) if (!used.Contains(p + i)) return p + i;
    }

    public string DefaultValue(ComponentKind k) => k switch
    {
        ComponentKind.Resistor => "1k", ComponentKind.Capacitor => "100n", ComponentKind.Inductor => "1m",
        ComponentKind.VoltageSource => "PULSE(0 1 0.0005 1e-06 1e-06 0.02 0.04)",
        ComponentKind.CurrentSource => "DC 1m",
        ComponentKind.Diode => "1N4148",
        ComponentKind.Label => NextLabel(),
        _ => ""
    };

    string NextLabel()
    {
        var used = Components.Where(c => c.Kind == ComponentKind.Label).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains("OUT")) return "OUT";
        for (int i = 1; ; i++) if (!used.Contains("N" + i)) return "N" + i;
    }
}
