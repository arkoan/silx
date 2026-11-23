using Silx.Mesures.Courbes;
using Silx.Mesures.Mesures;

namespace Silx.Mesures.Gabarits;

/// <summary>Mesures prêtes à l'emploi pour les gabarits.</summary>
public static class Catalogue
{
    public static IMesure<ReponseFrequentielle> BandePassante3dB { get; } =
        new Mesure<ReponseFrequentielle>("Bande passante -3 dB", "Hz", Frequentiel.BandePassante3dB);
    public static IMesure<ReponseFrequentielle> MargeDePhase { get; } =
        new Mesure<ReponseFrequentielle>("Marge de phase", "°", r => Frequentiel.MargeDePhase(r));
    public static IMesure<ReponseFrequentielle> MargeDeGain { get; } =
        new Mesure<ReponseFrequentielle>("Marge de gain", "dB", r => Frequentiel.MargeDeGain(r));
    public static IMesure<ReponseFrequentielle> GainDc { get; } =
        new Mesure<ReponseFrequentielle>("Gain DC", "V/V", Frequentiel.GainDc);

    public static IMesure<CourbeTemporelle> Depassement { get; } =
        new Mesure<CourbeTemporelle>("Dépassement", "%", c => Transitoire.Depassement(c));
    public static IMesure<CourbeTemporelle> TempsDeMontee { get; } =
        new Mesure<CourbeTemporelle>("Temps de montée 10-90 %", "s", c => Transitoire.TempsDeMontee(c));
    public static IMesure<CourbeTemporelle> TempsEtablissement2Pct { get; } =
        new Mesure<CourbeTemporelle>("Temps d'établissement 2 %", "s", c => Transitoire.TempsEtablissement(c));

    public static IMesure<CourbeTemporelle> Thd(double f0Hz) =>
        new Mesure<CourbeTemporelle>("THD", "ratio", c => Spectral.Thd(c, f0Hz));
}
