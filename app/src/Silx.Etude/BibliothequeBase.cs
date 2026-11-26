namespace Silx.Etude;

/// <summary>
/// Composants de base intégrés, toujours dans la palette : bipolaires, JFET, MOSFET discrets et quartz courants.
/// Modèles SPICE indicatifs (paramètres publics usuels), à remplacer par ceux du fabricant pour une étude fine ;
/// un composant importé ou modifié de même nom les remplace.
/// </summary>
public static class BibliothequeBase
{
    const string Texte = """
        * Bipolaires
        .model 2N3904 NPN(IS=6.734f XTI=3 EG=1.11 VAF=74.03 BF=416.4 NE=1.259 ISE=6.734f IKF=66.78m XTB=1.5 BR=.7371 NC=2 RC=1 CJC=3.638p MJC=.3085 VJC=.75 FC=.5 CJE=4.493p MJE=.2593 VJE=.75 TR=239.5n TF=301.2p ITF=.4 VTF=4 XTF=2 RB=10)
        .model 2N3906 PNP(IS=1.41f XTI=3 EG=1.11 VAF=18.7 BF=180.7 NE=1.5 IKF=80m XTB=1.5 BR=4.977 NC=2 RC=2.5 CJC=9.728p MJC=.5776 VJC=.75 FC=.5 CJE=8.063p MJE=.3677 VJE=.75 TR=33.42n TF=179.3p ITF=.4 VTF=4 XTF=6 RB=10)
        .model 2N2222A NPN(IS=14.34f XTI=3 EG=1.11 VAF=74.03 BF=255.9 NE=1.307 ISE=14.34f IKF=.2847 XTB=1.5 BR=6.092 NC=2 RC=1 CJC=7.306p MJC=.3416 VJC=.75 FC=.5 CJE=22.01p MJE=.377 VJE=.75 TR=46.91n TF=411.1p ITF=.6 VTF=1.7 XTF=3 RB=10)
        .model BC547B NPN(IS=2.39E-14 NF=1.008 ISE=3.545E-15 NE=1.541 BF=294.3 IKF=0.1357 VAF=63.2 NR=1.004 ISC=6.272E-14 NC=1.243 BR=7.946 IKR=0.1144 VAR=25.9 RB=1 RE=0.4683 RC=0.85 CJE=1.358E-11 VJE=0.65 MJE=0.3279 TF=4.391E-10 XTF=120 VTF=2.643 ITF=0.7495 CJC=3.728E-12 VJC=0.3997 MJC=0.2955 FC=0.9579)
        .model BC557B PNP(IS=3.83E-14 NF=1.008 ISE=1.22E-14 NE=1.528 BF=344.4 IKF=0.08039 VAF=21.11 NR=1.005 ISC=2.85E-13 NC=1.28 BR=14.84 IKR=0.047 VAR=32.02 RB=1 RE=0.6202 RC=0.5713 CJE=1.23E-11 VJE=0.6106 MJE=0.378 TF=5.60E-10 XTF=3.414 VTF=5.23 ITF=0.1483 CJC=1.08E-11 VJC=0.1022 MJC=0.3563 FC=0.8027)
        * JFET
        .model 2N5457 NJF(VTO=-1.37 BETA=1.125m LAMBDA=2.3m RD=1 RS=1 CGS=2.25p CGD=2.25p IS=33.6f)
        .model J201 NJF(VTO=-0.8 BETA=1.3m LAMBDA=4m RD=1 RS=1 CGS=3p CGD=3p IS=50f)
        .model 2N5460 PJF(VTO=2.5 BETA=0.6m LAMBDA=4m RD=1 RS=1 CGS=5p CGD=5p IS=50f)

        * MOSFET canal N petit signal 60 V, 200 mA (modèle niveau 1 indicatif, diode de corps)
        .subckt 2N7000 D G S
        M1 D G S S MN
        CGS G S 20p
        CGD G D 5p
        DBODY S D DB
        .model MN NMOS(LEVEL=1 VTO=2.1 KP=0.3 LAMBDA=0.01)
        .model DB D(IS=1e-12 RS=0.5 CJO=20p)
        .ends 2N7000

        * MOSFET canal P petit signal 45 V, 250 mA (modèle niveau 1 indicatif, diode de corps)
        .subckt BS250 D G S
        M1 D G S S MP
        CGS G S 25p
        CGD G D 5p
        DBODY D S DB
        .model MP PMOS(LEVEL=1 VTO=-2.0 KP=0.15 LAMBDA=0.01)
        .model DB D(IS=1e-12 RS=0.5 CJO=25p)
        .ends BS250

        * MOSFET canal N de puissance 100 V, 33 A (modèle niveau 1 indicatif, diode de corps)
        .subckt IRF540N D G S
        M1 D1 G S S MN
        RD D D1 20m
        CGS G S 1.7n
        CGD G D 150p
        DBODY S D DB
        .model MN NMOS(LEVEL=1 VTO=3.5 KP=20 LAMBDA=0.002)
        .model DB D(IS=1e-10 RS=10m CJO=1n)
        .ends IRF540N

        * MOSFET canal P de puissance 100 V, 23 A (modèle niveau 1 indicatif, diode de corps)
        .subckt IRF9540 D G S
        M1 D1 G S S MP
        RD D D1 60m
        CGS G S 1.4n
        CGD G D 200p
        DBODY D S DB
        .model MP PMOS(LEVEL=1 VTO=-3.5 KP=10 LAMBDA=0.002)
        .model DB D(IS=1e-10 RS=10m CJO=1n)
        .ends IRF9540

        * Quartz 32,768 kHz (horloger) : branche motionnelle L1 C1 R1 en parallèle sur C0
        .subckt XTAL32K768 1 2
        L1 1 3 11.4k
        C1 3 4 2.07f
        R1 4 2 35k
        C0 1 2 1.3p
        .ends XTAL32K768

        * Quartz 4 MHz : branche motionnelle L1 C1 R1 en parallèle sur C0
        .subckt XTAL4M 1 2
        L1 1 3 132m
        C1 3 4 12f
        R1 4 2 80
        C0 1 2 3.5p
        .ends XTAL4M

        * Quartz 8 MHz : branche motionnelle L1 C1 R1 en parallèle sur C0
        .subckt XTAL8M 1 2
        L1 1 3 26.4m
        C1 3 4 15f
        R1 4 2 50
        C0 1 2 4p
        .ends XTAL8M

        * Quartz 16 MHz : branche motionnelle L1 C1 R1 en parallèle sur C0
        .subckt XTAL16M 1 2
        L1 1 3 4.95m
        C1 3 4 20f
        R1 4 2 40
        C0 1 2 5p
        .ends XTAL16M
        """;

    /// <summary>Quartz : sous-circuit à deux broches « 1 » et « 2 » dont le nom commence par XTAL.</summary>
    public static bool EstQuartz(DefinitionComposant d) => d.Symbole.Gabarit == GabaritSymbole.Quartz;

    public static IEnumerable<DefinitionComposant> Definitions()
    {
        foreach (var m in ModelesSpice.Analyser(Texte).Modeles)
        {
            var d = DefinitionComposant.Depuis(m);
            if (m.Nom.StartsWith("XTAL", StringComparison.OrdinalIgnoreCase))
            {
                d.AppliquerGabarit(GabaritSymbole.Quartz);
                d.Description = "Quartz";
                d.Nom = m.Nom switch { "XTAL32K768" => "Quartz 32,768 kHz", "XTAL4M" => "Quartz 4 MHz", "XTAL8M" => "Quartz 8 MHz", _ => "Quartz 16 MHz" };
            }
            d.Notes = "Modèle intégré, indicatif. " + d.Notes;
            yield return d;
        }
    }
}
