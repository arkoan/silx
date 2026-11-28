namespace Silx.Mcp;

/// <summary>Instructions données au client MCP à la connexion : comment Silx est organisé et dans quel ordre appeler les outils.</summary>
static class Instructions
{
    public const string Texte = """
        Silx est un simulateur électronique (ngspice) pour ingénieurs de bureau d'étude. Un projet (.silx) contient un
        circuit sous test, sans source, et des bancs de test : chaque banc pose sur le circuit ses sources, charges et
        sondes, une analyse (transitoire, AC ou balayage DC), des critères mesurés avec bornes (PASS/FAIL) et des gabarits dessinés.
        Un banc peut porter une campagne : balayage d'un paramètre, Monte-Carlo dans les tolérances, ou coins.

        Démarche habituelle : open_project (ou open_demo) → describe_project et list_testbenches → run_simulation →
        get_test_summary, get_raw_curve → run_campaign → get_worst_case. get_operating_point donne
        la polarisation, get_noise le bruit d'un nœud sur un banc AC. set_component_value modifie le projet en
        mémoire seulement ; save_project l'écrit, et ne remplace un fichier qu'avec ecraser = true.

        Unités SI partout (s, Hz, V, A, dB, %). Les valeurs s'écrivent aussi en notation ingénieur : 4.7k, 100n, 2.2m.
        Ne présentez jamais une valeur comme simulée si elle ne vient pas d'un résultat de ces outils.

        Mode direct : si l'application Silx est ouverte, les outils agissent sur ses onglets et l'utilisateur voit chaque
        action à l'écran (list_projects l'indique, et liste les projets déjà ouverts dans l'application : commencez par
        là). Un projet modifié reste à enregistrer par save_project ou par l'utilisateur. Si un outil répond que
        l'utilisateur a interrompu ou mis en pause l'IA, arrêtez-vous et demandez-lui comment poursuivre.
        """;
}
