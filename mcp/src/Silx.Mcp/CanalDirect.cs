using System.Net.Sockets;
namespace Silx.Mcp;

/// <summary>
/// Canal local entre silx-mcp et l'application Silx ouverte (mode « en direct ») : un socket de domaine Unix, lisible par
/// l'utilisateur seul, dans le dossier de Silx. Rien n'écoute sur le réseau.
/// Emplacement : ~/Library/Application Support/Silx/direct.sock (macOS) ; SILX_DIRECT=chemin pour un autre, « off » désactive.
/// </summary>
public static class CanalDirect
{
    public static string? Chemin()
    {
        var env = Environment.GetEnvironmentVariable("SILX_DIRECT");
        if (string.Equals(env, "off", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env);
        var maison = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS()) return Path.Combine(maison, "Library", "Application Support", "Silx", "direct.sock");
        var dossier = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return Path.Combine(string.IsNullOrEmpty(dossier) ? Path.GetTempPath() : dossier, $"silx-{Environment.UserName}-direct.sock");
    }

    /// <summary>Se connecte à l'application ; null si elle n'est pas ouverte (pas de socket, ou personne n'écoute).</summary>
    public static async Task<Stream?> ConnecterAsync(string? chemin, TimeSpan delai, CancellationToken ct = default)
    {
        if (chemin == null || !File.Exists(chemin)) return null;
        var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(delai);
            await s.ConnectAsync(new UnixDomainSocketEndPoint(chemin), cts.Token);
            return new NetworkStream(s, ownsSocket: true);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            s.Dispose();
            return null;
        }
    }
}
