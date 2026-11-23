using System.Numerics;

namespace Silx.Mesures.Courbes;

/// <summary>
/// Réponse complexe H(jf) : fonction de transfert ou gain de boucle, fréquences en Hz
/// strictement croissantes et strictement positives. Immuable.
/// </summary>
public sealed class ReponseFrequentielle
{
    private readonly double[] _f;
    private readonly Complex[] _h;

    public ReponseFrequentielle(IEnumerable<double> frequencesHz, IEnumerable<Complex> valeurs)
    {
        _f = frequencesHz.ToArray();
        _h = valeurs.ToArray();
        if (_f.Length != _h.Length) throw new ArgumentException("Fréquences et valeurs de longueurs différentes.");
        if (_f.Length < 2) throw new ArgumentException("Au moins 2 points requis.");
        for (int i = 0; i < _f.Length; i++)
        {
            if (!double.IsFinite(_f[i]) || _f[i] <= 0) throw new ArgumentException($"Fréquence {i} invalide (doit être > 0).");
            if (!double.IsFinite(_h[i].Real) || !double.IsFinite(_h[i].Imaginary)) throw new ArgumentException($"Valeur {i} non finie.");
            if (i > 0 && _f[i] <= _f[i - 1]) throw new ArgumentException($"Fréquences non strictement croissantes à l'indice {i}.");
        }
    }

    public int Count => _f.Length;
    public ReadOnlySpan<double> Frequences => _f;
    public ReadOnlySpan<Complex> Valeurs => _h;
}
