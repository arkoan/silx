using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Silx.Etude;
using Silx.Mcp;
using Silx.Mesures.Gabarits;
namespace Silx.App;

/// <summary>
/// Mode « en direct » : l'IA externe (Claude Desktop, Claude Code… via silx-mcp) travaille dans cette fenêtre.
/// L'application écoute sur un socket local (<see cref="CanalDirect"/>) et sert elle-même les outils MCP de Silx, sur
/// les projets de ses onglets : ce que l'IA ouvre, modifie et simule s'affiche au fil de l'eau (onglet et banc suivis,
/// composant modifié sélectionné, verdicts et courbes, progression des campagnes). Un badge dit ce que fait l'IA ;
/// « Interrompre » arrête l'opération en cours et met l'IA en pause jusqu'à « Reprendre ».
/// Les outils s'exécutent sur le fil de l'interface (les simulations ngspice, elles, tournent à côté) : l'IA et
/// l'utilisateur ne modifient jamais le projet en même temps.
/// </summary>
public sealed partial class MainWindow
{
    readonly SessionSilx _sessionIA = new() { EnDirect = true };
    HoteDirect? _hoteIA;
    readonly Dictionary<ProjetOuvert, ProjetCharge> _projetsIA = new();
    CancellationTokenSource _interruptionIA = new();
    bool _iaEnPause;
    int _appelsIA, _clientsIA;
    string _actionIA = "";
    readonly List<string> _activiteIA = new();
    readonly TextBlock _texteIA = new() { FontSize = Ui.Aide, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 230, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Border _badgeIA = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly Button _pauseIA = new() { FontSize = Ui.Aide, Padding = new Thickness(9, 2), VerticalAlignment = VerticalAlignment.Center, IsVisible = false, CornerRadius = new CornerRadius(6) };

    /// <summary>L'écran suit l'IA : onglet et banc sur lesquels elle travaille.</summary>
    public bool SuivreIA { get; set; } = true;
    public HoteDirect? HoteIA => _hoteIA;
    public bool IAEnPause => _iaEnPause;
    public bool IAOccupee => _appelsIA > 0;
    public string BadgeIA => _badgeIA.IsVisible ? _texteIA.Text ?? "" : "";
    public string BoutonIA => _pauseIA.IsVisible ? _pauseIA.Content as string ?? "" : "";
    /// <summary>Dernières actions de l'IA, la plus récente en dernier.</summary>
    public IReadOnlyList<string> ActiviteIA => _activiteIA;

    /// <summary>Ouvre le canal local ; si une autre fenêtre Silx l'a déjà, elle garde l'IA.</summary>
    public async Task DemarrerLiaisonIAAsync(string canal)
    {
        _badgeIA.Child = _texteIA;
        _pauseIA.Click += (_, _) => { if (_iaEnPause) ReprendreIA(); else InterrompreIA(); };
        _sessionIA.ProjetAjoute += p =>
        {
            // open_project, open_demo : un nouvel onglet, qui partage ses résultats avec la session de l'IA.
            var o = OuvrirDansOnglet(p.Etude, p.Nom, p.Chemin, p.Simulations);
            _projetsIA[o] = p;
            if (p.Chemin != null) RetenirRecent(p.Chemin);
        };
        _sessionIA.ProjetRetire += p =>
        {
            if (_projetsIA.FirstOrDefault(kv => kv.Value == p).Key is { } o) { _projetsIA.Remove(o); FermerSansDemander(o); }
        };
        var h = new HoteDirect(_sessionIA, canal, FiltreIA);
        h.ConnexionsChangees += n => Dispatcher.UIThread.Post(() => { _clientsIA = n; MajBadgeIA(); });
        if (!await h.DemarrerAsync()) { await h.DisposeAsync(); return; }
        _hoteIA = h;
    }

    /// <summary>Arrête l'opération de l'IA en cours et refuse ses appels suivants jusqu'à <see cref="ReprendreIA"/>.</summary>
    public void InterrompreIA()
    {
        _iaEnPause = true;
        var c = _interruptionIA; _interruptionIA = new(); c.Cancel();
        _status.Text = "IA interrompue : ses prochains appels sont refusés jusqu'à « Reprendre ».";
        MajBadgeIA();
    }

    public void ReprendreIA()
    {
        _iaEnPause = false;
        _status.Text = "L'IA peut de nouveau travailler dans Silx.";
        MajBadgeIA();
    }

    void MajBadgeIA()
    {
        bool actif = _appelsIA > 0;
        _badgeIA.IsVisible = _pauseIA.IsVisible = _clientsIA > 0 || actif || _iaEnPause;
        _texteIA.Text = _iaEnPause ? "IA en pause" : actif ? $"IA : {_actionIA}…" : "IA connectée";
        _badgeIA.Background = _iaEnPause ? Ui.Teinte(Ui.WarnBrush, 0.16) : actif ? Ui.AccentBrush : Ui.Teinte(Ui.AccentBrush, 0.12);
        _texteIA.Foreground = _iaEnPause ? Ui.WarnBrush : actif ? Brushes.White : Ui.AccentBrush;
        _pauseIA.Content = _iaEnPause ? "Reprendre" : actif ? "Interrompre" : "Pause";
        ToolTip.SetTip(_pauseIA, _iaEnPause ? "Laisser de nouveau l'IA travailler dans Silx"
            : actif ? "Arrêter l'opération de l'IA et la mettre en pause" : "Mettre l'IA en pause : ses appels sont refusés jusqu'à « Reprendre »");
        ToolTip.SetTip(_badgeIA, _activiteIA.Count == 0 ? "Une IA est connectée à Silx (serveur MCP) : ses actions s'afficheront ici."
            : "Actions de l'IA :\n" + string.Join("\n", _activiteIA.TakeLast(15)));
    }

    // ───────────────────────── appels d'outils ─────────────────────────

    McpRequestHandler<CallToolRequestParams, CallToolResult> FiltreIA(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (ctx, ct) => await Dispatcher.UIThread.InvokeAsync(() => AppelIAAsync(ctx, next, ct));

    static CallToolResult ErreurIA(string texte) => new() { IsError = true, Content = [new TextContentBlock { Text = texte }] };

    async Task<CallToolResult> AppelIAAsync(RequestContext<CallToolRequestParams> ctx, McpRequestHandler<CallToolRequestParams, CallToolResult> next, CancellationToken ct)
    {
        var outil = ctx.Params?.Name ?? "";
        var args = ctx.Params?.Arguments;
        string? Arg(string k) => args != null && args.TryGetValue(k, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.GetRawText() : null
            : null;
        if (_iaEnPause)
            return ErreurIA("L'utilisateur a mis l'IA en pause dans Silx. Arrêtez-vous et demandez-lui comment poursuivre (il reprendra avec le bouton « Reprendre »).");

        SynchroniserProjetsIA();
        var (onglet, projet, banc) = CibleIA(outil, Arg("projet"), Arg("banc"), Arg("chemin"));
        var libelle = LibelleIA(outil, Arg, banc);
        if (outil == "close_project" && onglet is { Modifie: true })
            return ErreurIA($"« {onglet.Nom} » a des modifications non enregistrées dans Silx : enregistrez-le avec save_project, ou laissez l'utilisateur le fermer.");

        if (SuivreIA && onglet != null) MontrerIA(onglet, banc, outil, Arg("banc") != null);
        bool modification = outil == "set_component_value";
        bool surEcran = onglet != null && onglet == _projet;
        if (modification && surEcran) _canvas.AvantModificationExterne();   // ⌘Z annule le changement de l'IA

        _appelsIA++; _actionIA = libelle; MajBadgeIA();
        _status.Text = $"IA : {libelle}…";
        if (projet != null) projet.SuiviCampagne = SuiviCampagneIA;
        var interruption = _interruptionIA.Token;
        using var lien = CancellationTokenSource.CreateLinkedTokenSource(ct, interruption);
        CallToolResult r;
        try { r = await next(ctx, lien.Token); }
        catch (OperationCanceledException) when (interruption.IsCancellationRequested) { r = ErreurIA(""); }
        finally
        {
            _appelsIA--;
            if (projet != null) projet.SuiviCampagne = null;
            _progCampagne.IsVisible = _annulerCampagne.IsVisible = false;
        }
        if (interruption.IsCancellationRequested && r.IsError == true)
            r = ErreurIA("Interrompu par l'utilisateur dans Silx. Arrêtez-vous et demandez-lui comment poursuivre.");

        try { ApresIA(outil, onglet, projet, banc, surEcran, Arg); }
        catch (Exception ex) { SignalerErreur(ex, "affichage d'une action de l'IA"); }

        var erreur = r.IsError == true ? string.Concat(r.Content.OfType<TextContentBlock>().Select(t => t.Text)).Split('\n')[0] : null;
        _activiteIA.Add($"{DateTime.Now:HH:mm:ss}  {libelle}{(erreur != null ? " — ⚠ " + erreur : "")}");
        if (_activiteIA.Count > 200) _activiteIA.RemoveAt(0);
        if (!_status.Text!.StartsWith("⛔")) _status.Text = $"IA : {libelle}" + (erreur != null ? $" — ⚠ {erreur}" : " ✓");
        MajBadgeIA();
        return r;
    }

    /// <summary>Onglets ↔ projets de la session de l'IA : un onglet ouvert par l'utilisateur est visible de l'IA, un onglet fermé ne l'est plus.</summary>
    void SynchroniserProjetsIA()
    {
        foreach (var o in _projets)
            if (!_projetsIA.ContainsKey(o)) _projetsIA[o] = _sessionIA.Adopter(o.Nom, o.Chemin, o.Etude, o.Resultats);
        foreach (var (o, p) in _projetsIA.ToList())
        {
            if (!_projets.Contains(o)) { _sessionIA.Retirer(p); _projetsIA.Remove(o); continue; }
            p.Nom = o.Nom; p.Chemin = o.Chemin;
        }
    }

    /// <summary>Onglet, projet et banc visés par un appel (au mieux : une désignation ambiguë est laissée à l'outil, qui l'expliquera).</summary>
    (ProjetOuvert? Onglet, ProjetCharge? Projet, Banc? Banc) CibleIA(string outil, string? projet, string? banc, string? chemin)
    {
        ProjetCharge? p = null;
        if (outil == "open_project" && chemin != null)
        {
            // Déjà dans un onglet : l'outil le rendra tel qu'il est à l'écran.
            try
            {
                var plein = Path.GetFullPath(chemin.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), chemin.TrimStart('~', '/', '\\')) : chemin);
                p = _sessionIA.Projets.FirstOrDefault(x => x.Chemin != null && Path.GetFullPath(x.Chemin) == plein);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        else if (outil is not ("open_demo" or "list_projects" or "list_measurements"))
            try { p = _sessionIA.Projet_(projet); } catch (ErreurSilx) { }
        if (p == null) return (null, null, null);
        var o = _projetsIA.FirstOrDefault(kv => kv.Value == p).Key;
        Banc? b = null;
        // Outils d'un seul banc : sans précision, le seul banc du projet ; run_simulation sans banc les simule tous.
        bool unBanc = outil is "run_campaign" or "get_raw_curve" or "list_signals" or "get_netlist" or "get_worst_case" or "get_noise";
        if (banc != null || unBanc) try { b = SessionSilx.Banc_(p, banc); } catch (ErreurSilx) { }
        return (o, p, b);
    }

    /// <summary>L'écran suit l'IA : son onglet, et le banc (ou le circuit sous test) qu'elle touche.</summary>
    void MontrerIA(ProjetOuvert onglet, Banc? banc, string outil, bool bancDonne)
    {
        if (onglet != _projet) ActiverProjet(onglet);
        Banc? vue = _banc; bool changer = false;
        if (banc != null) { vue = banc; changer = true; }
        // Circuit sous test : valeur changée dans le circuit, ou tous les bancs simulés (verdict global).
        else if (!bancDonne && outil is "set_component_value" or "run_simulation") { vue = null; changer = true; }
        if (changer && vue != _banc) SelectBanc(vue);
    }

    void ApresIA(string outil, ProjetOuvert? onglet, ProjetCharge? projet, Banc? banc, bool surEcran, Func<string, string?> arg)
    {
        switch (outil)
        {
            case "set_component_value" when onglet != null:
                if (surEcran && onglet == _projet)
                {
                    _canvas.ApresModificationExterne();   // netlist, ERC, résultats périmés (et relance automatique si elle est rapide)
                    if (arg("reference") is { } reference)
                        _canvas.Selectionner(_canvas.Schematic.Components.FirstOrDefault(k => string.Equals(k.Ref, reference.Trim(), StringComparison.OrdinalIgnoreCase)));
                }
                break;
            case "run_simulation" or "get_operating_point" when onglet != null && onglet == _projet:
                if (_banc != null && _last is { } res) _journal.Text = res.Journal;
                SetVerdict(_banc == null ? GlobalVerdict() : _last is { Erreur: null } r && r.Nombre > 0 ? r.Global : null);
                RefreshGabarits(); RefreshPlot(); RefreshBenchBar(); RefreshSide();
                break;
            case "run_campaign" when projet != null && banc != null && projet.Campagnes.TryGetValue(banc, out var rc):
                _campagnes[banc] = rc;
                if (onglet == _projet && banc == _banc)
                {
                    _tirageChoisi = null;
                    SetVerdict(rc.Conformes == rc.Tirages.Count ? Verdict.Pass : Verdict.Fail);
                    RefreshPlotToolbar(); RefreshGabarits(); RefreshPlot(keepLimits: true);
                    if (SuivreIA || _detailOuvert == Detail.Campagne) OuvrirDetail(Detail.Campagne);
                }
                break;
            case "save_project" when onglet != null && projet != null:
                onglet.Chemin = projet.Chemin;
                if (projet.Chemin != null) { onglet.Nom = Path.GetFileNameWithoutExtension(projet.Chemin); RetenirRecent(projet.Chemin); }
                onglet.Demo = null; onglet.MarquerEnregistre();
                break;
        }
        if (onglet != null && onglet == _projet) RefreshBenchBar();   // pastilles des bancs : résultats à jour ou périmés
        MajEtatResultats();
        RafraichirOnglets();
    }

    /// <summary>Progression d'une campagne lancée par l'IA (appelée hors du fil de l'interface).</summary>
    void SuiviCampagneIA(Banc b, int n, int total) => Dispatcher.UIThread.Post(() =>
    {
        if (_appelsIA == 0) return;
        _progCampagne.Maximum = Math.Max(1, total); _progCampagne.Value = n; _progCampagne.IsVisible = true;
        _status.Text = $"IA : campagne sur « {b.Nom} » : {n}/{total} simulation(s)…";
    });

    /// <summary>Ce que fait l'IA, en clair, pour le badge et la barre d'état.</summary>
    static string LibelleIA(string outil, Func<string, string?> arg, Banc? banc)
    {
        string NomBanc() => banc != null ? $"« {banc.Nom} »" : arg("banc") is { } b ? $"« {b} »" : "le banc";
        return outil switch
        {
            "list_projects" => "consulte les projets ouverts",
            "open_project" => $"ouvre {Path.GetFileName(arg("chemin") ?? "un projet")}",
            "open_demo" => $"ouvre la démo « {arg("nom")} »",
            "describe_project" => "lit le circuit",
            "list_testbenches" => "lit les bancs de test",
            "list_measurements" => "consulte le catalogue des mesures",
            "list_signals" => $"liste les signaux de {NomBanc()}",
            "get_netlist" => $"lit la netlist de {NomBanc()}",
            "run_simulation" => arg("banc") == null ? "simule tous les bancs" : $"simule {NomBanc()}",
            "run_campaign" => $"lance une campagne sur {NomBanc()}",
            "get_test_summary" => "lit le bilan des tests",
            "get_worst_case" => $"lit le pire cas de {NomBanc()}",
            "get_raw_curve" => $"lit la courbe {arg("signal")} de {NomBanc()}",
            "get_operating_point" => "calcule le point de fonctionnement",
            "get_noise" => $"lit le bruit de {NomBanc()}",
            "set_component_value" => $"change {arg("reference")}" + (arg("valeur") is { } v ? $" en {v}" : "") + (arg("tolerance") is { } t ? $" (tolérance {t} %)" : ""),
            "save_project" => "enregistre le projet",
            "close_project" => "ferme le projet",
            _ => $"utilise {outil}",
        };
    }
}
