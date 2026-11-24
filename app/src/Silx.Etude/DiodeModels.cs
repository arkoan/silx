namespace Silx.Etude;

/// <summary>
/// Petite bibliothèque intégrée de modèles de diodes.
/// HYPOTHÈSE : paramètres génériques couramment publiés, indicatifs ; à remplacer par les modèles fabricant
/// quand la gestion de bibliothèques (.lib/.mod) sera en place.
/// </summary>
public static class DiodeModels
{
    public static IReadOnlyDictionary<string, string> All { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["1N4148"] = ".model 1N4148 D(Is=2.52n Rs=0.568 N=1.752 Cjo=4p M=0.4 Tt=20n Bv=100 Ibv=100u)",
        ["1N4007"] = ".model 1N4007 D(Is=7.03n Rs=0.034 N=1.81 Cjo=10p M=0.3 Tt=1u Bv=1000 Ibv=5u)",
        ["BAT54"] = ".model BAT54 D(Is=0.22u Rs=1.1 N=1.08 Cjo=10p M=0.33 Bv=30 Ibv=1u)",
        ["DIDEALE"] = ".model DIDEALE D(Is=1e-14 N=1)",
    };
}
