namespace Silx.Mesures;

/// <summary>
/// La mesure n'a pas de sens pour cette courbe (ex. pas de passage à 0 dB, signal plat).
/// Distincte d'une <see cref="ArgumentException"/> (entrée invalide) : un gabarit la traduit
/// en verdict « non évaluable », jamais en pass silencieux.
/// </summary>
public sealed class MesureImpossibleException(string message) : Exception(message);
