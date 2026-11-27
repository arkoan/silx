using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
namespace Silx.Mcp;

/// <summary>
/// Aiguillage de silx-mcp entre le client MCP (entrée et sortie standard) et l'endroit où les outils s'exécutent :
/// <list type="bullet">
/// <item>l'application Silx ouverte (mode « en direct ») : les actions de l'IA s'y voient au fil de l'eau ;</item>
/// <item>sinon un serveur interne, en arrière-plan sur les fichiers, comme avant.</item>
/// </list>
/// Le choix se refait en cours de session : l'application fermée, on repasse en arrière-plan ; l'application ouverte
/// après coup, on la rejoint tant que l'arrière-plan n'a aucun projet ouvert (rien n'est perdu). Pour rejoindre un
/// serveur, la poignée de main MCP du client (initialize) lui est rejouée.
/// Le protocole stdio de MCP est un message JSON-RPC par ligne : l'aiguillage lit chaque ligne, sans la modifier.
/// </summary>
public sealed class Relais(Stream entreeClient, Stream sortieClient, string? cheminDirect, Action<string>? journal = null)
{
    /// <summary>Un serveur derrière l'aiguillage : l'application (socket) ou le serveur interne (tubes en mémoire).</summary>
    sealed class Liaison(string nom, Stream versServeur, Stream depuisServeur, bool application)
    {
        public string Nom { get; } = nom;
        public bool Application { get; } = application;
        public Stream VersServeur { get; } = versServeur;
        public Stream DepuisServeur { get; } = depuisServeur;
        public bool Initialise { get; set; }
        int _perdue;
        public bool Perdue { get => Volatile.Read(ref _perdue) == 1; set => Volatile.Write(ref _perdue, value ? 1 : 0); }
        /// <summary>Vrai pour le premier appelant seulement.</summary>
        public bool MarquerPerdue() => Interlocked.Exchange(ref _perdue, 1) == 0;
        public SemaphoreSlim Ecriture { get; } = new(1, 1);
        /// <summary>Réponse attendue à la poignée de main rejouée.</summary>
        public TaskCompletionSource? Poignee { get; set; }
        public string? IdPoignee { get; set; }
    }

    readonly SemaphoreSlim _ecritureClient = new(1, 1);
    readonly SemaphoreSlim _bascule = new(1, 1);
    /// <summary>Requêtes du client en cours : identifiant JSON → (méthode, serveur qui la traite).</summary>
    readonly ConcurrentDictionary<string, (string Methode, Liaison Serveur)> _enCours = new();
    Liaison? _courant, _interne;
    SessionSilx? _sessionInterne;
    string? _initialize, _initialized;
    int _numero;

    /// <summary>Serveur qui traite les appels : « application » ou « arrière-plan » (tests, journal).</summary>
    public string Mode => _courant?.Application == true ? "application" : "arrière-plan";

    void Journal(string m) => journal?.Invoke(m);

    public async Task RunAsync(CancellationToken ct = default)
    {
        _courant = await ApplicationAsync() ?? Interne();
        Journal(_courant.Application ? "Application Silx trouvée : mode direct." : "Application Silx absente : mode arrière-plan.");
        using var lecteur = new StreamReader(entreeClient, new UTF8Encoding(false));
        while (!ct.IsCancellationRequested)
        {
            var ligne = await lecteur.ReadLineAsync(ct);
            if (ligne == null) break;
            if (string.IsNullOrWhiteSpace(ligne)) continue;
            await DuClientAsync(ligne);
        }
    }

    // ── du client vers le serveur ──

    async Task DuClientAsync(string ligne)
    {
        JsonObject? m = null;
        try { m = JsonNode.Parse(ligne) as JsonObject; } catch (System.Text.Json.JsonException) { }
        var methode = m?["method"]?.GetValue<string>();
        var id = m?["id"]?.ToJsonString();
        if (methode == "initialize") _initialize = ligne;
        else if (methode == "notifications/initialized") _initialized = ligne;
        // L'application ouverte après coup : on la rejoint au premier appel d'outil, si l'arrière-plan n'a rien en cours.
        if (methode == "tools/call" && _courant is { Application: false } && cheminDirect != null && File.Exists(cheminDirect)
            && (_sessionInterne == null || _sessionInterne.Projets.Count == 0) && _enCours.IsEmpty)
            await RejoindreApplicationAsync();
        var cible = _courant!;
        if (methode == "notifications/cancelled" && m?["params"]?["requestId"]?.ToJsonString() is { } annule && _enCours.TryGetValue(annule, out var e))
            cible = e.Serveur;
        if (methode != null && id != null) _enCours[id] = (methode, cible);
        if (methode == "notifications/initialized") cible.Initialise = true;
        if (!await VersServeurAsync(cible, ligne) && methode != null && id != null)
        {
            // Serveur perdu pendant l'envoi : la requête repart vers le serveur de repli.
            _enCours.TryRemove(id, out _);
            await DuClientAsync(ligne);
        }
    }

    async Task<bool> VersServeurAsync(Liaison s, string ligne)
    {
        if (s.Perdue) return false;
        await s.Ecriture.WaitAsync();
        try
        {
            var octets = Encoding.UTF8.GetBytes(ligne + "\n");
            await s.VersServeur.WriteAsync(octets);
            await s.VersServeur.FlushAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Net.Sockets.SocketException)
        {
            await PerteAsync(s);
            return false;
        }
        finally { s.Ecriture.Release(); }
    }

    // ── du serveur vers le client ──

    async Task LireServeurAsync(Liaison s)
    {
        try
        {
            using var lecteur = new StreamReader(s.DepuisServeur, new UTF8Encoding(false));
            while (await lecteur.ReadLineAsync() is { } ligne)
            {
                if (string.IsNullOrWhiteSpace(ligne)) continue;
                JsonObject? m = null;
                try { m = JsonNode.Parse(ligne) as JsonObject; } catch (System.Text.Json.JsonException) { }
                var id = m?["id"]?.ToJsonString();
                bool reponse = m != null && m["method"] == null && id != null;
                if (reponse && id == s.IdPoignee) { s.Poignee?.TrySetResult(); continue; }   // réponse à la poignée rejouée : pour nous seuls
                if (reponse) _enCours.TryRemove(id!, out _);
                await VersClientAsync(ligne);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Net.Sockets.SocketException or OperationCanceledException) { }
        await PerteAsync(s);
    }

    async Task VersClientAsync(string ligne)
    {
        await _ecritureClient.WaitAsync();
        try
        {
            await sortieClient.WriteAsync(Encoding.UTF8.GetBytes(ligne + "\n"));
            await sortieClient.FlushAsync();
        }
        finally { _ecritureClient.Release(); }
    }

    // ── serveurs ──

    async Task<Liaison?> ApplicationAsync()
    {
        var flux = await CanalDirect.ConnecterAsync(cheminDirect, TimeSpan.FromMilliseconds(500));
        if (flux == null) return null;
        var s = new Liaison("application", flux, flux, application: true);
        _ = Task.Run(() => LireServeurAsync(s));
        return s;
    }

    /// <summary>Serveur interne (arrière-plan), créé à la première demande et gardé ensuite avec ses projets.</summary>
    Liaison Interne()
    {
        if (_interne != null) return _interne;
        var versServeur = new Pipe(); var depuisServeur = new Pipe();
        _sessionInterne = new SessionSilx();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        Serveur.Configurer(services, _sessionInterne).WithStreamServerTransport(versServeur.Reader.AsStream(), depuisServeur.Writer.AsStream());
        var sp = services.BuildServiceProvider();
        var serveur = sp.GetRequiredService<McpServer>();
        _ = Task.Run(async () =>
        {
            try { await serveur.RunAsync(); }
            catch (Exception ex) { Journal($"Serveur interne arrêté : {ex.Message}"); }
        });
        _interne = new Liaison("arrière-plan", versServeur.Writer.AsStream(), depuisServeur.Reader.AsStream(), application: false);
        _ = Task.Run(() => LireServeurAsync(_interne));
        return _interne;
    }

    /// <summary>Rejoue au serveur la poignée de main du client, pour qu'il accepte les appels qui suivent.</summary>
    async Task<bool> InitialiserAsync(Liaison s)
    {
        if (s.Initialise || _initialize == null) return true;
        var m = (JsonObject)JsonNode.Parse(_initialize)!;
        s.IdPoignee = $"\"silx-relais-{Interlocked.Increment(ref _numero)}\"";
        m["id"] = JsonNode.Parse(s.IdPoignee);
        s.Poignee = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!await VersServeurAsync(s, m.ToJsonString())) return false;
        if (await Task.WhenAny(s.Poignee.Task, Task.Delay(TimeSpan.FromSeconds(5))) != s.Poignee.Task) return false;
        if (_initialized != null && !await VersServeurAsync(s, _initialized)) return false;
        s.Initialise = true;
        return true;
    }

    async Task RejoindreApplicationAsync()
    {
        await _bascule.WaitAsync();
        try
        {
            if (_courant is { Application: true }) return;
            var app = await ApplicationAsync();
            if (app == null) return;
            if (!await InitialiserAsync(app)) { app.Perdue = true; app.VersServeur.Dispose(); return; }
            _courant = app;
            Journal("Application Silx ouverte : passage en mode direct.");
            await VersClientAsync("""{"jsonrpc":"2.0","method":"notifications/tools/list_changed"}""");
        }
        finally { _bascule.Release(); }
    }

    /// <summary>Serveur perdu (application fermée) : ses requêtes en cours échouent proprement, la suite part en arrière-plan.</summary>
    async Task PerteAsync(Liaison s)
    {
        if (!s.MarquerPerdue()) return;
        s.Poignee?.TrySetResult();
        try { s.VersServeur.Dispose(); } catch (Exception) { }
        if (!s.Application) return;
        foreach (var (id, (methode, serveur)) in _enCours.ToArray())
        {
            if (serveur != s || !_enCours.TryRemove(id, out _)) continue;
            var texte = "L'application Silx a été fermée pendant cette opération. Les outils continuent en arrière-plan, sur les fichiers : "
                + "rouvrez le projet (open_project) pour poursuivre, ou demandez à l'utilisateur de rouvrir Silx.";
            var reponse = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id) };
            if (methode == "tools/call")
                reponse["result"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = texte }), ["isError"] = true };
            else reponse["error"] = new JsonObject { ["code"] = -32603, ["message"] = texte };
            await VersClientAsync(reponse.ToJsonString());
        }
        if (_courant != s) return;   // jamais devenu serveur courant (poignée de main ratée) : rien à basculer
        await _bascule.WaitAsync();
        try
        {
            if (_courant != s) return;
            var repli = Interne();
            await InitialiserAsync(repli);
            _courant = repli;
            Journal("Application Silx fermée : retour au mode arrière-plan.");
            await VersClientAsync("""{"jsonrpc":"2.0","method":"notifications/tools/list_changed"}""");
        }
        finally { _bascule.Release(); }
    }
}
