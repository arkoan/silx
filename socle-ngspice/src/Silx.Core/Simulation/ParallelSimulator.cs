using System.Threading.Channels;
using Silx.Core.Circuits;

namespace Silx.Core.Simulation;

public sealed record RunProgress(int Index, int Total, bool Success, TimeSpan Elapsed, string? Error);

/// <summary>Exécute N circuits indépendants avec un degré de parallélisme borné.</summary>
public sealed class ParallelSimulator(NgspiceRunner runner, int? maxParallelism = null)
{
    // HYPOTHÈSE : Environment.ProcessorCount (cœurs logiques) comme borne par défaut ;
    // la spec demande les cœurs physiques, non exposés par .NET de façon portable.
    public int MaxParallelism { get; } = maxParallelism ?? Environment.ProcessorCount;

    /// <returns>Résultats dans l'ordre des entrées ; null si le run a échoué (voir progression).</returns>
    public async Task<SimulationResult?[]> RunAllAsync(
        IReadOnlyList<Circuit> circuits, ChannelWriter<RunProgress>? progress = null, CancellationToken ct = default)
    {
        var results = new SimulationResult?[circuits.Count];
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, circuits.Count),
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = ct },
                async (i, token) =>
                {
                    try
                    {
                        var r = await runner.RunAsync(circuits[i], token);
                        results[i] = r;
                        progress?.TryWrite(new RunProgress(i, circuits.Count, true, r.Elapsed, null));
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        progress?.TryWrite(new RunProgress(i, circuits.Count, false, TimeSpan.Zero, e.Message));
                    }
                });
        }
        finally { progress?.TryComplete(); }
        return results;
    }
}
