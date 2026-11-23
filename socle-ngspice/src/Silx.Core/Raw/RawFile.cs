namespace Silx.Core.Raw;

public sealed record RawVariable(int Index, string Name, string Type);

/// <summary>Un jeu de résultats ngspice. Pour une analyse AC, <see cref="Imag"/> est renseigné.</summary>
public sealed class RawFile
{
    public required string Title { get; init; }
    public required string PlotName { get; init; }
    public required bool IsComplex { get; init; }
    public required IReadOnlyList<RawVariable> Variables { get; init; }
    public required int PointCount { get; init; }
    /// <summary>Real[variable][point]</summary>
    public required double[][] Real { get; init; }
    public double[][]? Imag { get; init; }

    public int IndexOf(string name)
    {
        for (int i = 0; i < Variables.Count; i++)
            if (string.Equals(Variables[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        throw new KeyNotFoundException($"Variable '{name}' absente. Disponibles : {string.Join(", ", Variables.Select(v => v.Name))}");
    }

    public double[] Vector(string name) => Real[IndexOf(name)];
    public double[] Imaginary(string name) =>
        Imag?[IndexOf(name)] ?? throw new InvalidOperationException("Résultat non complexe.");
    public double[] Magnitude(string name)
    {
        var re = Vector(name); var im = Imaginary(name);
        var m = new double[re.Length];
        for (int i = 0; i < m.Length; i++) m[i] = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
        return m;
    }
}
