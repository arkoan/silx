namespace Silx.Core.Circuits;

/// <summary>Un composant SPICE générique : lettre-préfixe dans le nom (R1, C2, V1, X3...).</summary>
public sealed record Component(string Name, IReadOnlyList<string> Nodes, string Value, string? Extra = null)
{
    public string ToSpiceLine() =>
        $"{Name} {string.Join(' ', Nodes)} {Value}" + (string.IsNullOrWhiteSpace(Extra) ? "" : " " + Extra);
}

public abstract record Analysis;
public sealed record OpAnalysis : Analysis;
public sealed record DcAnalysis(string Source, double Start, double Stop, double Step) : Analysis;
public sealed record AcAnalysis(AcSweep Sweep, int PointsPerSweep, double FStart, double FStop) : Analysis;
public sealed record TranAnalysis(double Step, double Stop, double Start = 0, double? MaxStep = null) : Analysis;
public enum AcSweep { Dec, Oct, Lin }

public sealed class Circuit
{
    private readonly List<Component> _components = new();
    private readonly List<string> _directives = new();

    public Circuit(string title) => Title = title;

    public string Title { get; }
    public IReadOnlyList<Component> Components => _components;
    /// <summary>Directives brutes : .model, .include, .param, .options...</summary>
    public IReadOnlyList<string> Directives => _directives;
    public Analysis? Analysis { get; set; }
    /// <summary>Vecteurs à sauvegarder (ex. "v(out)"). Vide = tout.</summary>
    public List<string> Saves { get; } = new();

    public Circuit Add(Component c)
    {
        if (_components.Any(x => string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Référence dupliquée : {c.Name}");
        _components.Add(c);
        return this;
    }

    public Circuit Directive(string d) { _directives.Add(d); return this; }

    public Circuit R(string name, string a, string b, string value) => Add(new(name, [a, b], value));
    public Circuit C(string name, string a, string b, string value) => Add(new(name, [a, b], value));
    public Circuit L(string name, string a, string b, string value) => Add(new(name, [a, b], value));
    /// <param name="spec">ex. "DC 5", "AC 1", "PULSE(0 1 0 1n 1n 1m 2m)", "SIN(0 1 1k)"</param>
    public Circuit V(string name, string plus, string minus, string spec) => Add(new(name, [plus, minus], spec));
    public Circuit I(string name, string plus, string minus, string spec) => Add(new(name, [plus, minus], spec));
    public Circuit D(string name, string anode, string cathode, string model) => Add(new(name, [anode, cathode], model));
}
