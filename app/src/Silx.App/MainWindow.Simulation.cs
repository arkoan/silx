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

/// <summary>Netlist, vérifications (ERC), simulation et simulation automatique.</summary>
public sealed partial class MainWindow
{
    // ───────────────────────── schéma → netlist / ERC ─────────────────────────
    void OnSchemaChanged()
    {
        var schema = SchemaCourant;
        var conn = Connectivity.Build(schema);
        var issues = Erc.Check(schema, conn, circuitSeul: _banc == null);
        _netlist.Text = CircuitBuilder.Preview(schema, conn, _banc == null ? null : CircuitBuilder.AnalysisFor(Banc, Shown));
        _ercList.Children.Clear();
        if (issues.Count == 0) _ercList.Children.Add(new TextBlock { Text = _banc == null ? "✓ Circuit cohérent (références, valeurs). Masse et broches libres sont vérifiées avec chaque banc." : "✓ Banc cohérent : masse présente, pas de broche flottante ni de référence dupliquée.", Foreground = Ui.PassBrush });
        foreach (var i in issues)
            _ercList.Children.Add(new TextBlock { Text = (i.Severity == Severity.Error ? "⛔ " : "⚠ ") + i.Message, Foreground = i.Severity == Severity.Error ? Ui.FailBrush : Ui.WarnBrush, TextWrapping = TextWrapping.Wrap });
        int nErr = issues.Count(i => i.Severity == Severity.Error), nWarn = issues.Count - nErr;
        if (_boutonsDetails.TryGetValue(Detail.Verifications, out var bv)) { bv.Content = nErr + nWarn == 0 ? "Vérifications ✓" : $"Vérifications ({nErr + nWarn})"; bv.Foreground = nErr > 0 ? Ui.FailBrush : nWarn > 0 ? Ui.WarnBrush : Ui.Ink; }
        // Circuit modifié : tous les bancs sont à relancer. Calque modifié : seulement le banc actif.
        bool had = !_chargement && (_banc == null ? _results.Count > 0 : _last != null);
        if (!_chargement) { if (_banc == null) _results.Clear(); else _last = null; }
        if (had) { SetVerdict(null); RefreshGabarits(); RefreshPlot(keepLimits: true); RefreshBenchBar(); if (_banc == null) RefreshSide(); }
        if (!_chargement && (had || _last == null)) ResultatsPerimes();
        if (_banc != null && _banc.Type != _typeAffiche) { _typeAffiche = _banc.Type; _cursors.Clear(); RefreshSide(); RefreshBenchBar(); RefreshPlot(); }
        RefreshPlotToolbar();
        RefreshProperties();
        if (!_chargement && _opVisible) Lancer(MajPointFonctionnementAsync, "point de fonctionnement");
    }

    // ───────────────────────── point de fonctionnement (.op) ─────────────────────────
    bool _opVisible; CancellationTokenSource? _ctsOp;
    public bool PointFonctionnementVisible => _opVisible;

    /// <summary>Affiche ou masque le point de fonctionnement continu sur le schéma (circuit seul, ou avec le banc actif).</summary>
    public async Task AfficherPointFonctionnementAsync(bool visible)
    {
        _opVisible = visible; Ui.SetActive(_boutonOp, visible);
        await MajPointFonctionnementAsync();
    }

    async Task MajPointFonctionnementAsync()
    {
        _ctsOp?.Cancel();
        if (!_opVisible) { _canvas.PointFonctionnement = null; return; }
        var cts = _ctsOp = new CancellationTokenSource();
        var r = await PointFonctionnement.CalculerAsync(_etude, _banc, cts.Token);
        if (cts.IsCancellationRequested) return;
        _canvas.PointFonctionnement = r;
        _status.Text = r.Erreur != null ? "⛔ " + r.Erreur
            : $"Point de fonctionnement {(_banc == null ? "du circuit seul" : $"avec le banc « {_banc.Nom} »")} : {r.Tensions.Count - 1} nœud(s), {r.Courants.Count} courant(s).";
    }

    // ───────────────────────── simulation ─────────────────────────
    public async Task SimulateAsync() => await SimulerAsync(keepLimits: false);

    async Task SimulerAsync(bool keepLimits, bool campagne = true)
    {
        _cts?.Cancel(); _cts = new();
        _simulate.IsEnabled = false; _simEnCours = true; MajEtatResultats();
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (_banc == null)
            {
                // Depuis le circuit : tous les bancs, en parallèle (non-régression).
                if (_etude.Bancs.Count == 0) { _status.Text = "Aucun banc de test : créez-en un pour brancher des stimuli sur le circuit."; return; }
                _status.Text = $"Simulation de {_etude.Bancs.Count} banc(s)…";
                var all = await Task.WhenAll(_etude.Bancs.Select(async b => (b, r: await Verification.RunAsync(_etude, b, [], _cts.Token))));
                foreach (var (b, r) in all) _results[b] = r;
                _journal.Text = string.Join("\n", all.Select(x => $"════ {x.b.Nom} ════\n{x.r.Journal}"));
                int ok = all.Count(x => x.r.Erreur == null && (x.r.Nombre == 0 || x.r.Global == Verdict.Pass));
                _status.Text = $"{all.Length} banc(s) simulé(s) en {sw.ElapsedMilliseconds} ms · {ok}/{all.Length} conforme(s)";
                _dureeDerniere = sw.ElapsedMilliseconds;
                SetVerdict(GlobalVerdict());
                RefreshSide(); RefreshBenchBar();
                return;
            }
            _status.Text = "Simulation ngspice en cours…";
            var res = await Verification.RunAsync(_etude, Banc, [], _cts.Token);
            _last = res;
            _journal.Text = res.Journal;
            if (res.Erreur != null)
            {
                _status.Text = "⛔ " + res.Erreur;
                OuvrirDetail(res.Erc.Any(i => i.Severity == Severity.Error) ? Detail.Verifications : Detail.Journal);
                SetVerdict(null);
            }
            else
            {
                _status.Text = $"« {Banc.Nom} » simulé en {sw.ElapsedMilliseconds} ms ({string.Join(" + ", res.Resultats.Keys.Select(k => k switch { AnalyseKind.Ac => "AC", AnalyseKind.Dc => "balayage DC", _ => "transitoire" }))}) · {res.Respectes}/{res.Nombre} contrainte(s) respectée(s)";
                SetVerdict(res.Nombre == 0 ? null : res.Global);
            }
            RefreshGabarits(); RefreshPlot(keepLimits); RefreshBenchBar();
            _dureeDerniere = sw.ElapsedMilliseconds;
            // Campagne du banc (balayage, Monte-Carlo, coins) : après le nominal, sur demande explicite seulement.
            if (campagne && res.Erreur == null && Banc.Campagne.Mode != ModeCampagne.Aucune) await LancerCampagneAsync(_cts.Token);
        }
        catch (OperationCanceledException) { }
        finally { _simulate.IsEnabled = true; _simEnCours = false; MajEtatResultats(); }
    }

    // ───────────────────────── simulation automatique ─────────────────────────
    bool _simAuto = true, _simEnCours;
    long _dureeDerniere = long.MaxValue;   // durée du dernier calcul (ms) ; inconnue = pas d'automatique
    readonly CheckBox _auto = new() { Content = "Auto", IsChecked = true, FontSize = Ui.Aide, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _etat = new() { FontSize = Ui.Aide, VerticalAlignment = VerticalAlignment.Center };
    readonly Avalonia.Threading.DispatcherTimer _relance = new() { Interval = TimeSpan.FromMilliseconds(350) };
    public bool SimulationAutomatique { get => _simAuto; set => _auto.IsChecked = value; }

    /// <summary>Résultats périmés : relance automatique si le dernier calcul était court, sinon « à relancer ».</summary>
    void ResultatsPerimes()
    {
        OublierCampagne();
        MajEtatResultats();
        if (_simAuto && _dureeDerniere < 1000 && !_chargement) { _relance.Stop(); _relance.Start(); }
    }

    void MajEtatResultats()
    {
        bool aJour = _banc == null ? _etude.Bancs.Count > 0 && _etude.Bancs.All(b => _results.ContainsKey(b)) : _last != null;
        _etat.Text = _simEnCours ? "Calcul…" : aJour ? "✓ À jour" : _banc == null && _etude.Bancs.Count == 0 ? "" : "● À relancer";
        _etat.Foreground = _simEnCours ? Ui.Muted : aJour ? Ui.PassBrush : Ui.WarnBrush;
    }
    public string EtatResultats => _etat.Text ?? "";
    public void Reevaluer() => Invalidate();

    void SetVerdict(Verdict? v)
    {
        MajEtatResultats();
        _verdict.IsVisible = v != null;
        if (v == null) return;
        _verdict.Background = v == Verdict.Pass ? Ui.PassBrush : v == Verdict.Fail ? Ui.FailBrush : Ui.WarnBrush;
        _verdict.Child = new TextBlock { Text = v == Verdict.Pass ? "✓ Conforme" : v == Verdict.Fail ? "✕ Non conforme" : "Non évaluable", Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, FontSize = Ui.Texte };
    }
}
