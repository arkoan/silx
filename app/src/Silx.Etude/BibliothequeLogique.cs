namespace Silx.Etude;

/// <summary>
/// Composants logiques intégrés : portes et bascules XSPICE prêtes à poser (délais typiques d'une famille CMOS rapide,
/// tension logique 5 V). Modifiables comme tout composant : un fichier de même nom dans la bibliothèque les remplace.
/// </summary>
public static class BibliothequeLogique
{
    static readonly (string Nom, string Code, string Params)[] Composants =
    [
        ("AND2", "d_and", "rise_delay=5n fall_delay=5n"), ("NAND2", "d_nand", "rise_delay=5n fall_delay=5n"),
        ("OR2", "d_or", "rise_delay=5n fall_delay=5n"), ("NOR2", "d_nor", "rise_delay=5n fall_delay=5n"),
        ("XOR2", "d_xor", "rise_delay=5n fall_delay=5n"), ("INV", "d_inverter", "rise_delay=5n fall_delay=5n"),
        ("BUF", "d_buffer", "rise_delay=5n fall_delay=5n"),
        ("DFF", "d_dff", "clk_delay=10n set_delay=10n reset_delay=10n ic=0"),
        ("JKFF", "d_jkff", "clk_delay=10n set_delay=10n reset_delay=10n ic=0"),
        ("TFF", "d_tff", "clk_delay=10n set_delay=10n reset_delay=10n ic=0"),
        ("DLATCH", "d_dlatch", "data_delay=10n enable_delay=10n set_delay=10n reset_delay=10n ic=0"),
    ];

    public static IEnumerable<DefinitionComposant> Definitions()
    {
        foreach (var (nom, code, p) in Composants)
        {
            var modele = "silx_" + nom.ToLowerInvariant();
            var d = DefinitionComposant.Depuis(new ModeleImporte(modele, TypeModele.Logique, ModelesSpice.BrochesLogiques(code), $".model {modele} {code}({p})", Code: code));
            d.Nom = nom;
            yield return d;
        }
    }
}
