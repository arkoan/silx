using Silx.Etude;
namespace Silx.App.Tests;

/// <summary>
/// Schémas de test écrits au pas de 4 (dipôles du format de projet 1) : on les raccorde comme le fait l'ouverture d'un
/// ancien projet, ce qui vérifie au passage la migration.
/// </summary>
static class Ancien
{
    public static void Raccorder(Schematic s, params Banc[] bancs)
    {
        s.RaccorderDipoles(4);
        foreach (var b in bancs) b.Calque.RaccorderDipoles(4);
    }
}
