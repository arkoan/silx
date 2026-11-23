namespace Silx.Mesures.Courbes;

/// <summary>Signal v(t) échantillonné, pas de temps éventuellement non uniforme. Immuable.</summary>
public sealed class CourbeTemporelle
{
    private readonly double[] _t;
    private readonly double[] _v;

    public CourbeTemporelle(IEnumerable<double> temps, IEnumerable<double> valeurs)
    {
        _t = temps.ToArray();
        _v = valeurs.ToArray();
        if (_t.Length != _v.Length) throw new ArgumentException("Temps et valeurs de longueurs différentes.");
        if (_t.Length < 2) throw new ArgumentException("Au moins 2 échantillons requis.");
        for (int i = 0; i < _t.Length; i++)
        {
            if (!double.IsFinite(_t[i]) || !double.IsFinite(_v[i]))
                throw new ArgumentException($"Échantillon {i} non fini.");
            if (i > 0 && _t[i] <= _t[i - 1])
                throw new ArgumentException($"Temps non strictement croissant à l'indice {i}.");
        }
    }

    public int Count => _t.Length;
    public ReadOnlySpan<double> Temps => _t;
    public ReadOnlySpan<double> Valeurs => _v;
    public double Duree => _t[^1] - _t[0];

    /// <summary>Interpolation linéaire ; hors bornes : ArgumentOutOfRangeException.</summary>
    public double Interpoler(double t)
    {
        if (t < _t[0] || t > _t[^1]) throw new ArgumentOutOfRangeException(nameof(t));
        int i = Array.BinarySearch(_t, t);
        if (i >= 0) return _v[i];
        i = ~i; // premier indice > t
        return Interpolation.Lineaire(_t[i - 1], _v[i - 1], _t[i], _v[i], t);
    }
}
