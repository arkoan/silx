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
        GabaritSymbole.Nmos => "MOS canal N, 4 broches", GabaritSymbole.Pmos => "MOS canal P, 4 broches",
        GabaritSymbole.Nmos3 => "MOS canal N, 3 broches", GabaritSymbole.Pmos3 => "MOS canal P, 3 broches",
        GabaritSymbole.Njf => "JFET canal N", GabaritSymbole.Pjf => "JFET canal P", GabaritSymbole.Quartz => "Quartz", GabaritSymbole.Logique => "Logique (porte, bascule)", _ => "Personnalisé (dessin libre)"
    };

    /// <summary>Crée le symbole d'un gabarit ; les broches du gabarit sont reliées aux broches du modèle de même nom (sinon laissées libres).</summary>
    public static Symbole Creer(GabaritSymbole g, IReadOnlyList<string> brochesModele)
    {
        var s = g == GabaritSymbole.Boite ? Boite(brochesModele) : Dessin(g, false);
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
        var s = new Symbole { Gabarit = GabaritSymbole.Boite, Version = Symbole.VersionCourante, Corps = [1, -1, 7, 2 * lignes - 1] };
        for (int i = 0; i < broches.Count; i++)
        {
            bool g = i < gauche; int r = g ? i : i - gauche;
            s.Broches.Add(new() { X = g ? 0 : 8, Y = 2 * r, BrocheModele = broches[i] });
        }
        return s;
    }

    static BrocheSymbole B(int x, int y, string nom) => new() { X = x, Y = y, BrocheModele = nom, Role = nom };

    /// <summary>
    /// Gabarit dessiné ; broches nommées par rôle (D, G, S…). Transistors sur 4 pas de haut (broches à ± 2), dessin fin et
    /// compact au quart de pas ; diode et quartz sur 3 pas comme les dipôles. <paramref name="diodeCorps"/> : MOS 3 broches seulement.
    /// </summary>
    public static Symbole Dessin(GabaritSymbole g, bool diodeCorps)
    {
        var s = new Symbole { Gabarit = g, DiodeCorps = diodeCorps && g is GabaritSymbole.Nmos3 or GabaritSymbole.Pmos3, Version = Symbole.VersionCourante };
        var t = s.Traits;
        switch (g)
        {
            case GabaritSymbole.Diode:
                s.Broches.AddRange([B(0, 0, "A"), B(3, 0, "K")]);
                t.Add(Primitive.Trait(0, 0, 1.1, 0)); t.Add(Primitive.Trait(1.9, 0, 3, 0));
                t.Add(Primitive.Poly(true, 1.1, -0.45, 1.9, 0, 1.1, 0.45));
                t.Add(Primitive.Trait(1.9, -0.45, 1.9, 0.45));
                break;
            case GabaritSymbole.Quartz:
                s.Broches.AddRange([B(0, 0, "1"), B(3, 0, "2")]);
                t.Add(Primitive.Trait(0, 0, 1.05, 0)); t.Add(Primitive.Trait(1.95, 0, 3, 0));
                t.Add(Primitive.Trait(1.05, -0.6, 1.05, 0.6)); t.Add(Primitive.Trait(1.95, -0.6, 1.95, 0.6));
                t.Add(Primitive.Rect(1.25, -0.45, 1.75, 0.45));
                break;
            case GabaritSymbole.Npn:
            case GabaritSymbole.Pnp:
                // B à gauche, C en haut, E en bas.
                s.Broches.AddRange([B(0, 0, "B"), B(2, -2, "C"), B(2, 2, "E")]);
                t.Add(Primitive.Trait(0, 0, 0.9, 0));
                t.Add(Primitive.Trait(0.9, -0.6, 0.9, 0.6));
                t.Add(Primitive.Trait(0.9, -0.3, 2, -0.9)); t.Add(Primitive.Trait(2, -0.9, 2, -2));
                t.Add(g == GabaritSymbole.Npn ? Primitive.Fleche(0.9, 0.3, 2, 0.9) : Primitive.Fleche(2, 0.9, 0.9, 0.3));
                t.Add(Primitive.Trait(2, 0.9, 2, 2));
                t.Add(Primitive.Cercle(1.4, 0, 1.05));
                break;
            case GabaritSymbole.Nmos:
            case GabaritSymbole.Pmos:
            case GabaritSymbole.Nmos3:
            case GabaritSymbole.Pmos3:
            {
                // Symbole compact (1 case de large, 4 de haut, sans cercle) : G à gauche, D en haut, S en bas ;
                // substrat B à droite (4 broches) ou relié à la source (3 broches).
                bool canalN = g is GabaritSymbole.Nmos or GabaritSymbole.Nmos3, trois = g is GabaritSymbole.Nmos3 or GabaritSymbole.Pmos3;
                s.Broches.AddRange([B(0, 0, "G"), B(1, -2, "D"), B(1, 2, "S")]);
                if (!trois) s.Broches.Add(B(1, 0, "B"));
                else t.Add(Primitive.Trait(1, 0, 1, 0.4));
                t.Add(Primitive.Trait(0, 0, 0.3, 0)); t.Add(Primitive.Trait(0.3, -0.5, 0.3, 0.5));
                foreach (var y in new[] { -0.4, 0.0, 0.4 }) t.Add(Primitive.Trait(0.5, y - 0.14, 0.5, y + 0.14));
                t.Add(Primitive.Trait(0.5, -0.4, 1, -0.4)); t.Add(Primitive.Trait(1, -0.4, 1, -2));
                t.Add(Primitive.Trait(0.5, 0.4, 1, 0.4)); t.Add(Primitive.Trait(1, 0.4, 1, 2));
                t.Add(canalN ? Primitive.Fleche(1, 0, 0.5, 0) : Primitive.Fleche(0.5, 0, 1, 0));
                if (s.DiodeCorps)
                {
                    // Diode de corps à droite, piquée sur le drain et la source ; anode côté source pour un canal N.
                    const double xd = 1.4, yb = 1.0, h = 0.12;
                    t.Add(Primitive.Trait(1, -yb, xd, -yb)); t.Add(Primitive.Trait(xd, -yb, xd, -h));
                    t.Add(Primitive.Trait(1, yb, xd, yb)); t.Add(Primitive.Trait(xd, yb, xd, h));
                    if (canalN) { t.Add(Primitive.Poly(true, xd - h, h, xd + h, h, xd, -h)); t.Add(Primitive.Trait(xd - h, -h, xd + h, -h)); }
                    else { t.Add(Primitive.Poly(true, xd - h, -h, xd + h, -h, xd, h)); t.Add(Primitive.Trait(xd - h, h, xd + h, h)); }
                }
                break;
            }
            case GabaritSymbole.Njf:
            case GabaritSymbole.Pjf:
                s.Broches.AddRange([B(0, 0, "G"), B(2, -2, "D"), B(2, 2, "S")]);
                t.Add(g == GabaritSymbole.Njf ? Primitive.Fleche(0, 0, 0.95, 0) : Primitive.Fleche(0.95, 0, 0, 0));
                t.Add(Primitive.Trait(0.95, -0.65, 0.95, 0.65));
                t.Add(Primitive.Trait(0.95, -0.45, 2, -0.45)); t.Add(Primitive.Trait(2, -0.45, 2, -2));
                t.Add(Primitive.Trait(0.95, 0.45, 2, 0.45)); t.Add(Primitive.Trait(2, 0.45, 2, 2));
                t.Add(Primitive.Cercle(1.4, 0, 1.05));
                break;
        }
        return s;
    }

    /// <summary>
    /// Symbole logique (norme CEI) : porte = rectangle avec « & », « ≥1 », « =1 » ou « 1 », rond de négation en sortie ;
    /// bascule = boîte avec le nom des broches, entrées à gauche, Q et /Q à droite. Broches reliées par nom.
    /// </summary>
    public static Symbole Logique(string code, IReadOnlyList<string> broches)
    {
        code = code.ToLowerInvariant();
        var s = new Symbole { Gabarit = GabaritSymbole.Logique, Version = Symbole.VersionCourante };
        var entrees = broches.Where(b => !ModelesSpice.EstSortieLogique(b)).ToList();
        var sorties = broches.Where(ModelesSpice.EstSortieLogique).ToList();
        static List<int> Rangs(int n) => n switch { 1 => [0], 2 => [-1, 1], 3 => [-1, 0, 1], 4 => [-2, -1, 1, 2], _ => Enumerable.Range(0, n).Select(i => i - n / 2).ToList() };
        if (ModelesSpice.EstPorte(code) || code is "d_inverter" or "d_buffer")
        {
            var ys = Rangs(entrees.Count);
            double h = Math.Max(1, Math.Max(-ys.Min(), ys.Max()) + 0.75);
            s.Traits.Add(Primitive.Rect(1, -h, 3, h));
            for (int i = 0; i < entrees.Count; i++) { s.Broches.Add(new() { X = 0, Y = ys[i], BrocheModele = entrees[i], Role = entrees[i] }); s.Traits.Add(Primitive.Trait(0, ys[i], 1, ys[i])); }
            bool non = code is "d_nand" or "d_nor" or "d_xnor" or "d_inverter";
            s.Broches.Add(new() { X = 4, Y = 0, BrocheModele = "Y", Role = "Y" });
            if (non) { s.Traits.Add(Primitive.Cercle(3.25, 0, 0.25)); s.Traits.Add(Primitive.Trait(3.5, 0, 4, 0)); }
            else s.Traits.Add(Primitive.Trait(3, 0, 4, 0));
            var signe = code switch { "d_and" or "d_nand" => "&", "d_or" or "d_nor" => "≥1", "d_xor" or "d_xnor" => "=1", _ => "1" };
            s.Traits.Add(Primitive.Txt(signe.Length > 1 ? 1.45 : 1.75, -h + 0.25, signe));
            return s;
        }
        // Bascules et verrous : boîte, noms des broches à l'intérieur.
        var ye = Rangs(entrees.Count); var yq = Rangs(sorties.Count);
        double hb = Math.Max(Math.Max(-ye.Min(), ye.Max()), Math.Max(-yq.Min(), yq.Max())) + 1;
        s.Corps = [1, -hb, 4, hb];
        for (int i = 0; i < entrees.Count; i++) s.Broches.Add(new() { X = 0, Y = ye[i], BrocheModele = entrees[i], Role = entrees[i] });
        for (int i = 0; i < sorties.Count; i++) s.Broches.Add(new() { X = 5, Y = yq[i], BrocheModele = sorties[i], Role = sorties[i], Nom = sorties[i] == "QB" ? "/Q" : null });
        return s;
    }

    /// <summary>Met le dessin à l'échelle <paramref name="k"/> autour de l'origine ; broches arrondies à la grille du schéma.</summary>
    public static void Reduire(Symbole s, double k)
    {
        foreach (var b in s.Broches) { b.X = (int)Math.Round(b.X * k); b.Y = (int)Math.Round(b.Y * k); }
        foreach (var t in s.Traits)
        {
            t.X1 *= k; t.Y1 *= k; t.Y2 *= k; t.X2 *= k;   // pour un cercle, X2 est le rayon : même échelle
            if (t.Points != null) foreach (var p in t.Points) { p[0] *= k; p[1] *= k; }
        }
        if (s.Corps is { } c) for (int i = 0; i < c.Length; i++) c[i] *= k;
    }

    /// <summary>
    /// Symbole d'une bibliothèque antérieure à la grille fine : un gabarit non retouché est redessiné à la nouvelle taille,
    /// en gardant la liaison et le nom de chaque broche (repérées par leur rôle). Renvoie vrai s'il a changé.
    /// </summary>
    public static bool MettreAJour(Symbole s)
    {
        if (s.Version >= Symbole.VersionCourante) return false;
        s.Version = Symbole.VersionCourante;
        if (s.Gabarit is GabaritSymbole.Boite or GabaritSymbole.Personnalise or GabaritSymbole.Logique) return false;
        var neuf = Dessin(s.Gabarit, s.DiodeCorps);
        foreach (var b in neuf.Broches)
            if (s.Broches.FirstOrDefault(a => a.Role == b.Role) is { } ancien) { b.BrocheModele = ancien.BrocheModele; b.Nom = ancien.Nom; }
            else b.BrocheModele = null;
        s.Traits = neuf.Traits; s.Broches = neuf.Broches;
        return true;
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
