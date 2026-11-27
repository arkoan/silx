using Silx.Etude;
namespace Silx.App;

/// <summary>Un projet ouvert dans un onglet : son étude, son fichier, ses résultats et le banc affiché.</summary>
public sealed class ProjetOuvert
{
    public required Silx.Etude.Etude Etude { get; init; }
    public string? Chemin { get; set; }
    public string Nom { get; set; } = "Sans titre";
    /// <summary>Résultats par banc ; partagés avec la session de l'IA en mode direct (ce qu'elle simule s'affiche ici).</summary>
    public Dictionary<Banc, ResultatEtude> Resultats { get; init; } = new();
    public Banc? BancActif { get; set; }
    /// <summary>Nom de la démonstration intégrée dont vient ce projet (recréée à jour au prochain démarrage s'il n'est pas modifié).</summary>
    public string? Demo { get; set; }
    /// <summary>Contenu au dernier enregistrement (ou à l'ouverture) : sert à savoir s'il y a des modifications.</summary>
    public string Reference { get; set; } = "";

    public bool Modifie => Projet.VersJson(Etude) != Reference;
    public void MarquerEnregistre() => Reference = Projet.VersJson(Etude);
}
