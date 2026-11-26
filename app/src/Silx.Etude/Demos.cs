using Silx.Mesures.Gabarits;
namespace Silx.Etude;

/// <summary>
/// Projets de démonstration ouverts au démarrage (en plus de l'exemple RC) : filtre à quartz en échelle, oscillateur
/// Colpitts, amplificateur à émetteur commun avec campagne Monte-Carlo. Valeurs vérifiées sous ngspice (tests).
/// </summary>
public static class Demos
{
    public static IReadOnlyList<(string Nom, Func<Etude> Creer)> Toutes { get; } =
    [
        ("Filtre à quartz 4 MHz", FiltreQuartz),
        ("Oscillateur Colpitts", Colpitts),
        ("Ampli émetteur commun", AmpliEmetteurCommun),
    ];

    sealed class Plan(Schematic s)
    {
        int _gnd;
        public PlacedComponent Dipole(string r, ComponentKind k, int x, int y, int rot, string v)
        { var c = new PlacedComponent { Ref = r, Kind = k, Position = new(x, y), Rotation = rot, Value = v }; s.Components.Add(c); return c; }
        public PlacedComponent Spice(string r, DefinitionComposant d, int x, int y, string v = "")
        { var c = new PlacedComponent { Ref = r, Kind = ComponentKind.Spice, Definition = d, Position = new(x, y), Value = v }; s.Components.Add(c); return c; }
        public void Net(string nom, int x, int y) => s.Components.Add(new() { Ref = "NET" + (s.Components.Count + 1), Kind = ComponentKind.Label, Position = new(x, y), Value = nom });
        public void Masse(int x, int y, string prefixe = "GND") => s.Components.Add(new() { Ref = $"{prefixe}{++_gnd}", Kind = ComponentKind.Ground, Position = new(x, y) });
        public void Fil(int x1, int y1, int x2, int y2) => s.Wires.Add(new(new(x1, y1), new(x2, y2)));
    }

    static DefinitionComposant Base(string nom) => BibliothequeBase.Definitions().Single(d => d.Nom == nom);

    /// <summary>
    /// Filtre à quartz en échelle, 2 quartz 4 MHz, terminé sur 470 Ω : bande passante ≈ 1,2 kHz vers 3,999 MHz,
    /// gain maximal ≈ −7,4 dB (dont −6 dB du pont de terminaison).
    /// </summary>
    public static Etude FiltreQuartz()
    {
        var e = new Etude(); var p = new Plan(e.Schema); var q = Base("Quartz 4 MHz");
        p.Dipole("RS", ComponentKind.Resistor, 5, 8, 0, "470"); p.Net("IN", 5, 8);
        p.Fil(8, 8, 10, 8);
        p.Spice("X1", q, 10, 8);
        p.Fil(13, 8, 16, 8);
        p.Dipole("CS", ComponentKind.Capacitor, 14, 8, 1, "47p"); p.Masse(14, 11);
        p.Spice("X2", q, 16, 8);
        p.Dipole("RL", ComponentKind.Resistor, 19, 8, 1, "470"); p.Net("OUT", 19, 8); p.Masse(19, 11);
        var b = new Banc { Nom = "Réponse du filtre", Type = AnalyseKind.Ac };
        b.Calque.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(5, 8), Rotation = 1, Value = "AC 1" });
        b.Calque.Components.Add(new() { Ref = "GNDB1", Kind = ComponentKind.Ground, Position = new(5, 11) });
        b.Ac.FDebut = 3.99e6; b.Ac.FFin = 4.01e6;
        b.Gabarits.Add(new() { Mesure = TypeMesure.GainMax, Noeud = "OUT", Min = -10 });
        b.Gabarits.Add(new() { Mesure = TypeMesure.LargeurBande3dB, Noeud = "OUT", Min = 800, Max = 1600 });
        // Bande passante (limite basse) et réjection de part et d'autre (limites hautes).
        b.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Ac, Noeud = "OUT", Limite = Limite.Basse, Points = [(3.99885e6, -14), (3.99980e6, -14)] });
        b.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Ac, Noeud = "OUT", Limite = Limite.Haute, Points = [(3.990e6, -25), (3.997e6, -25)] });
        b.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Ac, Noeud = "OUT", Limite = Limite.Haute, Points = [(4.001e6, -25), (4.010e6, -25)] });
        b.Graphiques.Add(["OUT"]);
        e.Bancs.Add(b);
        return e;
    }

    /// <summary>
    /// Oscillateur Colpitts à émetteur commun (2N3904, L = 10 µH, C1 = C2 = 1 nF) : ≈ 2,2 MHz, ≈ 7 V crête à crête au
    /// collecteur. Schéma dessiné dans Silx par Régis (projet embarqué Demos/OscillateurColpitts.silx) ; alimentation
    /// 9 V dans le circuit, impulsion de courant de démarrage dans le banc.
    /// </summary>
    public static Etude Colpitts() => Embarque("OscillateurColpitts.silx");

    /// <summary>Projet de démonstration embarqué dans l'assemblage (fichier .silx enregistré depuis Silx).</summary>
    public static Etude Embarque(string fichier)
    {
        using var s = typeof(Demos).Assembly.GetManifestResourceStream("Silx.Etude.Demos." + fichier)
            ?? throw new InvalidOperationException($"Démo « {fichier} » absente de l'assemblage.");
        using var r = new StreamReader(s);
        return Projet.DepuisJson(r.ReadToEnd());
    }

    /// <summary>
    /// Amplificateur à émetteur commun (BC547B, 12 V) attaqué par un générateur 600 Ω : gain ≈ 42 dB, bande ≈ 100 Hz à
    /// 800 kHz. Campagne Monte-Carlo réglée (100 tirages, résistances ± 5 %).
    /// </summary>
    public static Etude AmpliEmetteurCommun()
    {
        var e = new Etude(); var p = new Plan(e.Schema);
        p.Dipole("VCC", ComponentKind.VoltageSource, 0, 2, 1, "DC 12"); p.Net("VCC", 0, 2); p.Masse(0, 5);
        p.Dipole("CIN", ComponentKind.Capacitor, 1, 8, 0, "1u"); p.Net("IN", 1, 8); p.Net("B", 4, 8);
        p.Dipole("R1", ComponentKind.Resistor, 6, 2, 1, "47k").Tolerance = 5; p.Net("VCC", 6, 2); p.Net("B", 6, 5);
        p.Dipole("R2", ComponentKind.Resistor, 6, 8, 1, "10k").Tolerance = 5; p.Net("B", 6, 8); p.Masse(6, 11);
        p.Spice("Q1", Base("BC547B"), 10, 8); p.Net("B", 10, 8); p.Net("C", 12, 6); p.Net("E", 12, 10);
        p.Dipole("RC", ComponentKind.Resistor, 16, 2, 1, "4.7k").Tolerance = 5; p.Net("VCC", 16, 2); p.Net("C", 16, 5);
        p.Dipole("RE", ComponentKind.Resistor, 16, 10, 1, "1k").Tolerance = 5; p.Net("E", 16, 10); p.Masse(16, 13);
        p.Dipole("CE", ComponentKind.Capacitor, 19, 10, 1, "100u"); p.Net("E", 19, 10); p.Masse(19, 13);
        p.Dipole("COUT", ComponentKind.Capacitor, 22, 5, 0, "1u"); p.Net("C", 22, 5); p.Net("OUT", 25, 5);
        p.Dipole("RL", ComponentKind.Resistor, 28, 5, 1, "10k"); p.Net("OUT", 28, 5); p.Masse(28, 8);
        var b = new Banc { Nom = "Réponse en fréquence", Type = AnalyseKind.Ac };
        b.Calque.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(-8, 8), Rotation = 1, Value = "AC 1" });
        b.Calque.Components.Add(new() { Ref = "GNDB1", Kind = ComponentKind.Ground, Position = new(-8, 11) });
        b.Calque.Components.Add(new() { Ref = "RG", Kind = ComponentKind.Resistor, Position = new(-8, 8), Value = "600" });
        b.Calque.Components.Add(new() { Ref = "NETB1", Kind = ComponentKind.Label, Position = new(-5, 8), Value = "IN" });
        b.Ac.FDebut = 1; b.Ac.FFin = 100e6;
        b.Gabarits.Add(new() { Mesure = TypeMesure.GainMax, Noeud = "OUT", Min = 40 });
        b.Gabarits.Add(new() { Mesure = TypeMesure.LargeurBande3dB, Noeud = "OUT", Min = 500e3 });
        b.Campagne = new() { Mode = ModeCampagne.MonteCarlo, Tirages = 100, Graine = 1 };
        b.Graphiques.Add(["OUT"]);
        e.Bancs.Add(b);
        return e;
    }
}
