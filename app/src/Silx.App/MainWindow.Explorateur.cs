using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Silx.Etude;
namespace Silx.App;

/// <summary>Explorateur de projet : arborescence Circuit, Bancs (contraintes, graphiques), Composants importés ; clic pour y aller.</summary>
public sealed partial class MainWindow
{
    readonly StackPanel _arbre = new() { Spacing = 1, Margin = new Thickness(8) };
    Border _explorateur = null!;
    public bool ExplorateurVisible { get => _explorateur.IsVisible; set { _explorateur.IsVisible = value; RafraichirExplorateur(); } }
    public int ElementsExplorateur => _arbre.Children.Count;

    Control BuildExplorateur()
    {
        _explorateur = new Border { Width = 220, IsVisible = false, Background = Ui.Panel, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { Content = _arbre } };
        return _explorateur;
    }

    void RafraichirExplorateur()
    {
        if (_explorateur == null || !_explorateur.IsVisible) return;
        _arbre.Children.Clear();
        _arbre.Children.Add(Ui.Title(_projet?.Nom ?? "Projet"));
        void Noeud(int niveau, string texte, bool actif, Action? clic, IBrush? couleur = null, string? detail = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = texte, FontSize = niveau == 0 ? Ui.Texte : Ui.Aide, FontWeight = niveau == 0 || actif ? FontWeight.SemiBold : FontWeight.Normal, Foreground = couleur ?? Ui.Ink, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 });
            if (detail != null) row.Children.Add(new TextBlock { Text = detail, FontSize = Ui.Aide, Foreground = Ui.Muted });
            if (clic == null) { row.Margin = new Thickness(6 + 14 * niveau, 8, 6, 2); _arbre.Children.Add(row); return; }   // titre de section
            var b = new Button { Content = row, Padding = new Thickness(6 + 14 * niveau, 3, 6, 3), Background = actif ? Ui.ActiveBg : Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4), IsEnabled = clic != null };
            if (clic != null) b.Click += (_, _) => clic();
            _arbre.Children.Add(b);
        }
        Noeud(0, "Circuit sous test", _banc == null, () => SelectBanc(null), detail: $"{_etude.Schema.Components.Count(c => c.IsElectrical)} composants");
        Noeud(0, "Bancs de test", false, null);
        foreach (var b in _etude.Bancs)
        {
            var bb = b; var r = _results.GetValueOrDefault(b);
            var etat = r == null ? "" : r.Erreur != null ? " ⚠" : r.Nombre == 0 ? "" : r.Global == Silx.Mesures.Gabarits.Verdict.Pass ? " ✓" : r.Global == Silx.Mesures.Gabarits.Verdict.Fail ? " ✕" : " ?";
            Noeud(1, b.Nom + etat, _banc == b, () => SelectBanc(bb), CouleurDe(bb), b.Type == AnalyseKind.Ac ? "Bode" : "temporel");
            foreach (var c in b.CriteresActifs.ToList()) { var cc = c; Noeud(2, $"{c.Info.Nom} · {Signaux.Nom(c.Noeud)}", false, () => { if (_banc != bb) SelectBanc(bb); OuvrirLignePlan(cc); }); }
            foreach (var t in b.TracesActives.ToList()) { var tt = t; Noeud(2, $"Gabarit {(t.Limite == Silx.Mesures.Gabarits.Limite.Haute ? "haut" : "bas")} · {Signaux.Nom(t.Noeud)}", false, () => { if (_banc != bb) SelectBanc(bb); OuvrirLignePlan(tt); }); }
            for (int i = 0; i < b.Graphiques.Count; i++) Noeud(2, $"Graphique {i + 1} : {string.Join(", ", b.Graphiques[i].Select(n => $"V({n})").DefaultIfEmpty("vide"))}", false, () => { if (_banc != bb) SelectBanc(bb); });
        }
        var defs = _etude.Schema.Components.Concat(_etude.Bancs.SelectMany(b => b.Calque.Components)).Select(k => k.Definition).OfType<DefinitionComposant>().Distinct().ToList();
        if (defs.Count > 0)
        {
            Noeud(0, "Composants importés", false, null);
            foreach (var d in defs) { var dd = d; Noeud(1, d.Nom, false, () => Lancer(() => OuvrirEditeurAsync(dd), "éditeur de symbole"), detail: d.Description); }
        }
    }
}
