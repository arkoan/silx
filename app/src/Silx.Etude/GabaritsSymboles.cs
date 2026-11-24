namespace Silx.Etude;

/// <summary>Symboles par défaut (pas de grille ; broches sur des points entiers). Chaque broche est pré-reliée par nom.</summary>
public static class GabaritsSymboles
{
    public static GabaritSymbole PourType(TypeModele t) => t switch
    {
        TypeModele.Diode => GabaritSymbole.Diode, TypeModele.Npn => GabaritSymbole.Npn, TypeModele.Pnp => GabaritSymbole.Pnp,
        TypeModele.Nmos => GabaritSymbole.Nmos, TypeModele.Pmos => GabaritSymbole.Pmos, TypeModele.Njf => GabaritSymbole.Njf,
        TypeModele.Pjf => GabaritSymbole.Pjf, _ => GabaritSymbole.Boite
    };

    public static Symbole PourType(TypeModele t, IReadOnlyList<string> broches) => Creer(PourType(t), broches);

    public static string Libelle(GabaritSymbole g) => g switch
    {
        GabaritSymbole.Boite => "Boîte", GabaritSymbole.Diode => "Diode", GabaritSymbole.Npn => "NPN", GabaritSymbole.Pnp => "PNP",
        GabaritSymbole.Nmos => "MOS canal N", GabaritSymbole.Pmos => "MOS canal P", GabaritSymbole.Njf => "JFET canal N", _ => "JFET canal P"
    };

    /// <summary>Crée le symbole d'un gabarit ; les broches du gabarit sont reliées aux broches du modèle de même nom (sinon laissées libres).</summary>
    public static Symbole Creer(GabaritSymbole g, IReadOnlyList<string> brochesModele)
    {
        var s = g == GabaritSymbole.Boite ? Boite(brochesModele) : Dessin(g);
        if (g != GabaritSymbole.Boite)
            foreach (var b in s.Broches)
                b.BrocheModele = brochesModele.FirstOrDefault(n => string.Equals(n, b.BrocheModele, StringComparison.OrdinalIgnoreCase));
        return s;
    }

    /// <summary>
    /// Boîte rectangulaire : première moitié des broches à gauche, le reste à droite, au pas de 2.
    /// HYPOTHÈSE : ordre du fichier (souvent entrées puis alimentations puis sortie) ; l'utilisateur réarrange ensuite.
    /// </summary>
    public static Symbole Boite(IReadOnlyList<string> broches)
    {
        int n = Math.Max(broches.Count, 1), gauche = (n + 1) / 2, droite = n - gauche;
        int lignes = Math.Max(gauche, Math.Max(droite, 1));
        var s = new Symbole { Gabarit = GabaritSymbole.Boite, Corps = [1, -1, 7, 2 * lignes - 1] };
        for (int i = 0; i < broches.Count; i++)
        {
            bool g = i < gauche; int r = g ? i : i - gauche;
            s.Broches.Add(new() { X = g ? 0 : 8, Y = 2 * r, BrocheModele = broches[i] });
        }
        return s;
    }

    static BrocheSymbole B(int x, int y, string nom) => new() { X = x, Y = y, BrocheModele = nom };

    static Symbole Dessin(GabaritSymbole g)
    {
        var s = new Symbole { Gabarit = g };
        var t = s.Traits;
        switch (g)
        {
            case GabaritSymbole.Diode:
                s.Broches.AddRange([B(0, 0, "A"), B(4, 0, "K")]);
                t.Add(Primitive.Trait(0, 0, 1.5, 0)); t.Add(Primitive.Trait(2.5, 0, 4, 0));
                t.Add(Primitive.Poly(true, 1.5, -0.8, 2.5, 0, 1.5, 0.8));
                t.Add(Primitive.Trait(2.5, -0.8, 2.5, 0.8));
                break;
            case GabaritSymbole.Npn:
            case GabaritSymbole.Pnp:
                // B à gauche, C en haut, E en bas.
                s.Broches.AddRange([B(0, 0, "B"), B(3, -3, "C"), B(3, 3, "E")]);
                t.Add(Primitive.Trait(0, 0, 1.4, 0));
                t.Add(Primitive.Trait(1.4, -1, 1.4, 1));
                t.Add(Primitive.Trait(1.4, -0.45, 3, -1.4)); t.Add(Primitive.Trait(3, -1.4, 3, -3));
                t.Add(g == GabaritSymbole.Npn ? Primitive.Fleche(1.4, 0.45, 3, 1.4) : Primitive.Fleche(3, 1.4, 1.4, 0.45));
                t.Add(Primitive.Trait(3, 1.4, 3, 3));
                t.Add(Primitive.Cercle(2.1, 0, 1.75));
                break;
            case GabaritSymbole.Nmos:
            case GabaritSymbole.Pmos:
                // G à gauche, D en haut, S en bas, substrat B à droite.
                s.Broches.AddRange([B(0, 0, "G"), B(3, -3, "D"), B(3, 3, "S"), B(3, 0, "B")]);
                t.Add(Primitive.Trait(0, 0, 1, 0)); t.Add(Primitive.Trait(1, -1.2, 1, 1.2));
                foreach (var y in new[] { -1.0, 0.0, 1.0 }) t.Add(Primitive.Trait(1.5, y - 0.35, 1.5, y + 0.35));
                t.Add(Primitive.Trait(1.5, -1, 3, -1)); t.Add(Primitive.Trait(3, -1, 3, -3));
                t.Add(Primitive.Trait(1.5, 1, 3, 1)); t.Add(Primitive.Trait(3, 1, 3, 3));
                t.Add(g == GabaritSymbole.Nmos ? Primitive.Fleche(3, 0, 1.5, 0) : Primitive.Fleche(1.5, 0, 3, 0));
                break;
            case GabaritSymbole.Njf:
            case GabaritSymbole.Pjf:
                s.Broches.AddRange([B(0, 0, "G"), B(3, -3, "D"), B(3, 3, "S")]);
                t.Add(g == GabaritSymbole.Njf ? Primitive.Fleche(0, 0, 1.5, 0) : Primitive.Fleche(1.5, 0, 0, 0));
                t.Add(Primitive.Trait(1.5, -1.2, 1.5, 1.2));
                t.Add(Primitive.Trait(1.5, -0.8, 3, -0.8)); t.Add(Primitive.Trait(3, -0.8, 3, -3));
                t.Add(Primitive.Trait(1.5, 0.8, 3, 0.8)); t.Add(Primitive.Trait(3, 0.8, 3, 3));
                break;
        }
        return s;
    }

    /// <summary>Ajoute une broche libre sur une boîte : premier emplacement libre à droite, sinon à gauche ; agrandit le corps.</summary>
    public static BrocheSymbole AjouterBroche(Symbole s)
    {
        var c = s.Corps ?? [1, -1, 7, 1];
        int xd = (int)Math.Round(c[2]) + 1, xg = (int)Math.Round(c[0]) - 1;
        for (int y = 0; ; y += 2)
            foreach (var x in new[] { xd, xg })
                if (!s.Broches.Any(b => b.X == x && b.Y == y))
                {
                    var b = new BrocheSymbole { X = x, Y = y };
                    s.Broches.Add(b);
                    s.Corps = [c[0], Math.Min(c[1], y - 1), c[2], Math.Max(c[3], y + 1)];
                    return b;
                }
    }
}
