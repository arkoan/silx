using Silx.Etude;
using Xunit;
namespace Silx.App.Tests;

/// <summary>Fils élastiques : orthogonalité et conservation des connexions quand on déplace ou pivote un composant.</summary>
public class ElastiqueTests
{
    static GridPoint P(int x, int y) => new(x, y);
    static void Orthogonaux(Schematic s) => Assert.All(s.Wires, w => Assert.True(w.A.X == w.B.X || w.A.Y == w.B.Y, $"fil oblique {w}"));
    static bool Relie(Schematic s, GridPoint a, GridPoint b) { var c = Connectivity.Build(s); return c.NodeOf[a] == c.NodeOf[b]; }

    /// <summary>R1 (0,0)-(3,0) — fil — R2 (8,0)-(11,0).</summary>
    static Schematic Serie()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = P(0, 0), Value = "1k" });
        s.Components.Add(new() { Ref = "R2", Kind = ComponentKind.Resistor, Position = P(8, 0), Value = "1k" });
        s.Wires.Add(new(P(3, 0), P(8, 0)));
        return s;
    }

    [Fact]
    public void Deplacement_dans_l_axe_etire_le_fil()
    {
        var s = Serie();
        Elastique.Deplacer(s, [s.Components[1]], P(3, 0));
        Assert.Equal([new Wire(P(3, 0), P(11, 0))], s.Wires);
    }

    [Fact]
    public void Deplacement_perpendiculaire_ajoute_un_coude()
    {
        var s = Serie(); var avant = s.Clone();
        Elastique.Deplacer(s, [s.Components[1]], P(0, 4));
        Orthogonaux(s);
        Assert.True(Elastique.MemesConnexions(avant, s));
        // Le fil horizontal descend avec R2 ; un segment vertical le relie à la broche fixe de R1.
        Assert.Contains(new Wire(P(3, 4), P(8, 4)), s.Wires);
        Assert.Contains(s.Wires, w => w.A.X == 3 && w.B.X == 3 && Math.Abs(w.A.Y - w.B.Y) == 4);
    }

    [Fact]
    public void Deplacement_oblique_garde_des_fils_orthogonaux_et_le_retour_est_exact()
    {
        var s = Serie(); var origine = s.Clone();
        var d = s.Clone(); Elastique.Deplacer(d, [d.Components[1]], P(5, -3));
        Orthogonaux(d);
        Assert.True(Elastique.MemesConnexions(origine, d));
        // Pendant un glisser, on repart toujours de l'origine : revenir à (0,0) redonne le schéma initial.
        var r = s.Clone(); Elastique.Deplacer(r, [r.Components[1]], P(0, 0));
        Assert.Equal(origine.Wires, r.Wires);
    }

    [Fact]
    public void Le_coin_glisse_le_long_du_fil_voisin()
    {
        // R1 horizontale, fil qui part de sa broche droite (3,0) vers (3,6) puis vers (10,6) où se trouve R2 verticale.
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = P(0, 0), Value = "1k" });
        s.Components.Add(new() { Ref = "R2", Kind = ComponentKind.Resistor, Position = P(10, 6), Rotation = 1, Value = "1k" });
        s.Wires.Add(new(P(3, 0), P(3, 6))); s.Wires.Add(new(P(3, 6), P(10, 6)));
        Elastique.Deplacer(s, [s.Components[0]], P(2, 0));
        // Le segment vertical se décale à x = 5, l'horizontal raccourcit : pas de coude supplémentaire.
        Assert.Equal(2, s.Wires.Count);
        Assert.Contains(new Wire(P(5, 0), P(5, 6)), s.Wires);
        Assert.Contains(new Wire(P(5, 6), P(10, 6)), s.Wires);
    }

    [Fact]
    public void Une_jonction_ne_glisse_pas()
    {
        // Le fil de R1 arrive sur une jonction en T : on insère un coude plutôt que de déplacer la jonction.
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = P(0, 0), Value = "1k" });
        s.Wires.Add(new(P(3, 0), P(3, 6)));
        s.Wires.Add(new(P(0, 6), P(3, 6))); s.Wires.Add(new(P(3, 6), P(10, 6)));
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = P(0, 6) });
        s.Components.Add(new() { Ref = "R2", Kind = ComponentKind.Resistor, Position = P(10, 6), Rotation = 1, Value = "1k" });
        var avant = s.Clone();
        Elastique.Deplacer(s, [s.Components[0]], P(2, 0));
        Orthogonaux(s);
        Assert.Contains(new Wire(P(0, 6), P(3, 6)), s.Wires); // la jonction (3,6) reste en place
        Assert.True(Relie(s, P(5, 0), P(0, 6)) && Relie(s, P(5, 0), P(10, 6)));
        Assert.True(Elastique.MemesConnexions(avant, s));
    }

    [Fact]
    public void Une_broche_posee_sur_un_fil_l_emporte()
    {
        // R3 verticale dont la broche haute (6,0) touche l'intérieur du fil (3,0)-(8,0) : jonction en T.
        var s = Serie();
        s.Components.Add(new() { Ref = "R3", Kind = ComponentKind.Resistor, Position = P(6, 3), Rotation = 3, Value = "1k" });
        var avant = s.Clone();
        Elastique.Deplacer(s, [s.Components[2]], P(1, 2));
        Orthogonaux(s);
        Assert.True(Elastique.MemesConnexions(avant, s));
    }

    [Fact]
    public void Pivoter_garde_les_fils_attaches()
    {
        var s = Serie(); var avant = s.Clone();
        Elastique.Pivoter(s, s.Components[1]);
        Orthogonaux(s);
        Assert.True(Elastique.MemesConnexions(avant, s));
    }

    [Fact]
    public void Un_fil_entre_deux_broches_d_un_meme_composant_est_translate()
    {
        // Court-circuit en U sous R1 : (0,0)-(0,3)-(3,3)-(3,0).
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = P(0, 0), Value = "1k" });
        s.Wires.Add(new(P(0, 0), P(0, 3))); s.Wires.Add(new(P(0, 3), P(3, 3))); s.Wires.Add(new(P(3, 3), P(3, 0)));
        Elastique.Deplacer(s, [s.Components[0]], P(5, 2));
        Assert.Equal(3, s.Wires.Count);
        Assert.Contains(new Wire(P(5, 5), P(8, 5)), s.Wires);
    }

    [Fact]
    public void Exemple_RC_une_serie_de_deplacements_conserve_le_circuit()
    {
        var e = Etude.Etude.Demo();
        var origine = e.Schema;
        var casses = new List<string>();
        foreach (var refe in new[] { "R1", "C1", "GND1" })
            foreach (var d in new[] { P(3, 0), P(0, -5), P(-2, 7), P(6, 6), P(-1, -1), P(0, 3), P(-4, 0) })
            {
                var s = origine.Clone();
                bool ok = Elastique.Deplacer(s, [s.Components.Single(c => c.Ref == refe)], d);
                Orthogonaux(s);
                Assert.Equal(Elastique.MemesConnexions(origine, s), ok); // le canevas peut prévenir de façon fiable
                if (!ok) casses.Add($"{refe} {d.X},{d.Y}");
            }
        // Cas connus : R1 descendue sur le fil de masse (ses deux broches s'y posent : court-circuit), et la masse remontée
        // juste sous la broche de R1 (un coude droit passe forcément sur cette broche). Le canevas prévient dans les deux cas.
        Assert.Equal(["R1 0,3", "GND1 0,-5"], casses);
    }

    [Fact]
    public void Les_fils_du_banc_suivent_le_circuit()
    {
        var e = Etude.Etude.Demo();
        var bode = e.Bancs[1];
        var c1 = e.Schema.Components.Single(c => c.Ref == "C1");
        var moves = c1.Pins.ToDictionary(p => p, p => new GridPoint(p.X + 3, p.Y));
        Elastique.Deplacer(e.Schema, [c1], P(3, 0));
        Elastique.SuivreCalque(bode.Calque, moves);
        var f = bode.Fusion(e.Schema);
        var conn = Connectivity.Build(f);
        var rl = f.Components.Single(c => c.Ref == "RL");
        var c1b = f.Components.Single(c => c.Ref == "C1");
        Assert.Equal(conn.NodeOf[c1b.Pins[0]], conn.NodeOf[rl.Pins[0]]);
        Assert.Equal(conn.NodeOf[c1b.Pins[1]], conn.NodeOf[rl.Pins[1]]);
        // V1 posée directement sur la broche de R1 (non déplacée) reste reliée.
        Assert.Equal(conn.NodeOf[f.Components.Single(c => c.Ref == "V1").Pins[0]], conn.NodeOf[f.Components.Single(c => c.Ref == "R1").Pins[0]]);
    }
}
