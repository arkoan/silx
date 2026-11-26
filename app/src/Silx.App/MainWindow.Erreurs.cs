using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ScottPlot.Avalonia;
using Silx.Etude;
using Silx.Mesures.Gabarits;
using EtudeModel = Silx.Etude.Etude;
namespace Silx.App;

/// <summary>
/// Gestion des erreurs de l'interface : aucune erreur ne ferme l'application ; elle est signalée dans la barre d'état et
/// détaillée dans le journal ~/Library/Logs/Silx/silx.log (SILX_JOURNAL pour un autre dossier).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Lance une action asynchrone depuis un bouton ou une commande : une erreur est signalée (barre d'état, journal) au
    /// lieu d'être perdue dans une tâche que personne n'attend ; une annulation est silencieuse.
    /// </summary>
    public async void Lancer(Func<Task> action, string ou)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SignalerErreur(ex, ou); }
    }

    /// <summary>Erreur interne : message dans la barre d'état, détail dans ~/Library/Logs/Silx/silx.log.</summary>
    public void SignalerErreur(Exception ex, string ou)
    {
        var fichier = Journal.Ecrire(ou, ex);
        _status.Text = $"⛔ Erreur ({ou}) : {ex.Message}" + (fichier != null ? $" — détail dans {fichier.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~")}" : "");
    }
}
