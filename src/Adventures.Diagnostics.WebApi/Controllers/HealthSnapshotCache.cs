using Microsoft.Extensions.Configuration;

namespace Adventures.Diagnostics.WebApi;

/// <summary>
/// Backlog 3a: keeps the last health answer for a short window (<c>Health:CacheSeconds</c>, default 15) so an anonymous caller,
/// a monitor or a flood cannot make every request probe the database and mcp. Simultaneous callers share one probe run, and a
/// probe that throws is not remembered.
/// </summary>
public sealed class HealthSnapshotCache(TimeSpan window, TimeProvider clock)
{
    public const string WindowKey = "Health:CacheSeconds";

    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim gate = new(1, 1);
    private HealthResponse? snapshot;
    private DateTimeOffset takenAt;

    /// <summary>A positive number of seconds from configuration, else fifteen (zero or garbled never switches the protection off).</summary>
    public static TimeSpan WindowFrom(IConfiguration configuration) =>
        int.TryParse(configuration[WindowKey], out var seconds) && seconds >= 1 ? TimeSpan.FromSeconds(seconds) : DefaultWindow;

    public async Task<HealthResponse> GetAsync(Func<CancellationToken, Task<HealthResponse>> probe, CancellationToken cancellationToken)
    {
        if (TryFresh(out var fresh))
        {
            return fresh;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryFresh(out fresh))
            {
                return fresh;
            }

            var taken = await probe(cancellationToken);
            takenAt = clock.GetUtcNow();
            Volatile.Write(ref snapshot, taken);
            return taken;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool TryFresh(out HealthResponse response)
    {
        var current = Volatile.Read(ref snapshot);
        if (current is not null && clock.GetUtcNow() - takenAt < window)
        {
            response = current;
            return true;
        }

        response = null!;
        return false;
    }
}
