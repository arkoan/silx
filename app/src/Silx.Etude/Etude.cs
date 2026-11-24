namespace Silx.Etude;

public enum AnalyseKind { Transitoire, Ac }

public sealed class ReglagesTransitoire { public double Pas { get; set; } = 10e-6; public double Duree { get; set; } = 10e-3; }
public sealed class ReglagesAc { public double FDebut { get; set; } = 1; public double FFin { get; set; } = 1e6; public int PointsParDecade { get; set; } = 50; }

public enum TypeMesure { Depassement, TempsDeMontee, TempsEtablissement, ValeurFinale, BandePassante3dB, GainDc, MargeDePhase, MargeDeGain }

public static class TypesMesure
{
    public sealed record Info(TypeMesure Type, string Nom, string Unite, AnalyseKind Analyse);

    public static IReadOnlyList<Info> Tous { get; } =
    [
        new(TypeMesure.Depassement, "Dépassement", "%", AnalyseKind.Transitoire),
        new(TypeMesure.TempsDeMontee, "Temps de montée 10–90 %", "s", AnalyseKind.Transitoire),
        new(TypeMesure.TempsEtablissement, "Temps d'établissement 2 %", "s", AnalyseKind.Transitoire),
        new(TypeMesure.ValeurFinale, "Valeur finale", "V", AnalyseKind.Transitoire),
        new(TypeMesure.BandePassante3dB, "Bande passante −3 dB", "Hz", AnalyseKind.Ac),
        new(TypeMesure.GainDc, "Gain basse fréquence", "dB", AnalyseKind.Ac),
        new(TypeMesure.MargeDePhase, "Marge de phase (gain de boucle)", "°", AnalyseKind.Ac),
        new(TypeMesure.MargeDeGain, "Marge de gain (gain de boucle)", "dB", AnalyseKind.Ac),
    ];

    public static Info Get(TypeMesure t) => Tous.First(x => x.Type == t);
}

/// <summary>Un critère de gabarit tel que l'utilisateur le saisit : mesure + nœud observé + bornes.</summary>
public sealed class CritereGabarit
{
    public TypeMesure Mesure { get; set; }
    public string Noeud { get; set; } = "OUT";
    public double? Min { get; set; }
    public double? Max { get; set; }
    public TypesMesure.Info Info => TypesMesure.Get(Mesure);
}

/// <summary>Une étude = un schéma + les réglages d'analyse + les gabarits à vérifier.</summary>
public sealed class Etude
{
    public Schematic Schema { get; set; } = new();
    public ReglagesTransitoire Transitoire { get; } = new();
    public ReglagesAc Ac { get; } = new();
    public List<CritereGabarit> Gabarits { get; } = new();

    /// <summary>Filtre RC passe-bas de démonstration, avec deux critères.</summary>
    public static Etude Demo()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(4, 8), Rotation = 1, Value = "DC 0 AC 1 PULSE(0 1 0.5m 1u 1u 20m 40m)" });
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(4, 8), Value = "1k" });
        s.Components.Add(new() { Ref = "C1", Kind = ComponentKind.Capacitor, Position = new(12, 8), Rotation = 1, Value = "1u" });
        s.Wires.Add(new(new(8, 8), new(12, 8)));
        s.Wires.Add(new(new(12, 12), new(4, 12)));
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(8, 12) });
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(4, 8), Value = "IN" });
        s.Components.Add(new() { Ref = "NET2", Kind = ComponentKind.Label, Position = new(12, 8), Value = "OUT" });
        var e = new Etude { Schema = s };
        e.Gabarits.Add(new() { Mesure = TypeMesure.TempsDeMontee, Noeud = "OUT", Max = 2.5e-3 });
        e.Gabarits.Add(new() { Mesure = TypeMesure.Depassement, Noeud = "OUT", Max = 5 });
        e.Gabarits.Add(new() { Mesure = TypeMesure.BandePassante3dB, Noeud = "OUT", Min = 150 });
        return e;
    }
}
