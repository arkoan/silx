using Silx.Etude;
namespace Silx.App;

/// <summary>Un projet ouvert dans un onglet : son étude, son fichier, ses résultats et le banc affiché.</summary>
public sealed class ProjetOuvert
{
    public required Silx.Etude.Etude Etude { get; init; }
    public string? Chemin { get; set; }
    public string Nom { get; set; } = "Sans titre";
    public Dictionary<Banc, ResultatEtude> Resultats { get; } = new();
    public Banc? BancActif { get; set; }
    /// <summary>Contenu au dernier enregistrement (ou à l'ouverture) : sert à savoir s'il y a des modifications.</summary>
    public string Reference { get; set; } = "";

    public bool Modifie => Projet.VersJson(Etude) != Reference;
    public void MarquerEnregistre() => Reference = Projet.VersJson(Etude);
}
