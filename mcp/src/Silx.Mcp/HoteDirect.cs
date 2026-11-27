using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
namespace Silx.Mcp;

/// <summary>
/// Côté application (mode direct) : écoute sur <see cref="CanalDirect"/> et sert, à chaque silx-mcp qui se connecte, les
/// mêmes outils MCP sur la session de l'application. Un filtre donné par l'application entoure chaque appel d'outil
/// (affichage de l'action en cours, interruption, mise à jour de l'écran).
/// </summary>
public sealed class HoteDirect(SessionSilx session, string chemin, McpRequestFilter<CallToolRequestParams, CallToolResult>? filtre = null) : IAsyncDisposable
{
    readonly CancellationTokenSource _arret = new();
    Socket? _ecoute;
    Task? _boucle;
    int _connexions;

    public string Chemin { get; } = chemin;
    /// <summary>Clients IA connectés (un par silx-mcp lancé par un client MCP).</summary>
    public int Connexions => Volatile.Read(ref _connexions);
    /// <summary>Le nombre de clients connectés a changé (appelé hors du fil de l'interface).</summary>
    public event Action<int>? ConnexionsChangees;

    /// <summary>
    /// Ouvre le canal. Faux si une autre application Silx l'occupe déjà (elle garde le mode direct). Un socket laissé par
    /// une application qui s'est mal fermée est remplacé.
    /// </summary>
    public async Task<bool> DemarrerAsync()
    {
        if (File.Exists(Chemin))
        {
            await using var autre = await CanalDirect.ConnecterAsync(Chemin, TimeSpan.FromMilliseconds(300));
            if (autre != null) return false;
            File.Delete(Chemin);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Chemin)!);
        var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        s.Bind(new UnixDomainSocketEndPoint(Chemin));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Chemin, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        s.Listen(8);
        _ecoute = s;
        _boucle = Task.Run(BoucleAsync);
        return true;
    }

    async Task BoucleAsync()
    {
        while (!_arret.IsCancellationRequested)
        {
            Socket client;
            try { client = await _ecoute!.AcceptAsync(_arret.Token); }
            catch (Exception) when (_arret.IsCancellationRequested) { return; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => ServirAsync(client));
        }
    }

    async Task ServirAsync(Socket client)
    {
        // (évalué hors de ?.Invoke : sans abonné, l'argument ne serait pas calculé)
        var n = Interlocked.Increment(ref _connexions); ConnexionsChangees?.Invoke(n);
        try
        {
            await using var flux = new NetworkStream(client, ownsSocket: true);
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            var b = Serveur.Configurer(services, session).WithStreamServerTransport(flux, flux);
            if (filtre != null) b.WithRequestFilters(f => f.AddCallToolFilter(filtre));
            await using var sp = services.BuildServiceProvider();
            var serveur = sp.GetRequiredService<McpServer>();
            await serveur.RunAsync(_arret.Token);
        }
        catch (Exception) { /* client parti ou application qui se ferme : la connexion s'arrête, l'application continue */ }
        finally { var m = Interlocked.Decrement(ref _connexions); ConnexionsChangees?.Invoke(m); }
    }

    /// <summary>Ferme le canal tout de suite, sans attendre (fermeture de l'application, depuis le fil de l'interface).</summary>
    public void Fermer()
    {
        if (_arret.IsCancellationRequested) return;
        _arret.Cancel();
        _ecoute?.Dispose();
        // Seulement le socket ouvert par cet hôte (pas celui d'une autre application qui garde le canal).
        try { if (_ecoute != null && File.Exists(Chemin)) File.Delete(Chemin); } catch (IOException) { }
    }

    public async ValueTask DisposeAsync()
    {
        Fermer();
        if (_boucle != null) try { await _boucle.ConfigureAwait(false); } catch (Exception) { }
    }
}
