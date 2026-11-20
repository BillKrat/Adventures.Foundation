using Adventures.Diagnostics.WebApi;

using Microsoft.Extensions.Configuration;
using Xunit;

namespace Adventures.Diagnostics.WebApi.Tests;

/// <summary>Backlog 3a: the anonymous health check must not fan out to the database and mcp on every request.</summary>
public class HealthSnapshotCacheTests
{
    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static HealthResponse Snapshot(string status) => new(status, DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_second_request_inside_the_window_reuses_the_snapshot_without_probing()
    {
        var cache = new HealthSnapshotCache(TimeSpan.FromSeconds(15), new MovableClock(DateTimeOffset.UnixEpoch));
        var probes = 0;
        Task<HealthResponse> Probe(CancellationToken _) { probes++; return Task.FromResult(Snapshot("Healthy")); }

        await cache.GetAsync(Probe, CancellationToken.None);
        await cache.GetAsync(Probe, CancellationToken.None);

        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task A_request_after_the_window_probes_again()
    {
        var clock = new MovableClock(DateTimeOffset.UnixEpoch);
        var cache = new HealthSnapshotCache(TimeSpan.FromSeconds(15), clock);
        var probes = 0;
        Task<HealthResponse> Probe(CancellationToken _) { probes++; return Task.FromResult(Snapshot("Healthy")); }

        await cache.GetAsync(Probe, CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(16);
        await cache.GetAsync(Probe, CancellationToken.None);

        Assert.Equal(2, probes);
    }

    [Fact]
    public async Task Simultaneous_requests_share_one_probe()
    {
        var cache = new HealthSnapshotCache(TimeSpan.FromSeconds(15), new MovableClock(DateTimeOffset.UnixEpoch));
        var probes = 0;
        async Task<HealthResponse> Probe(CancellationToken _)
        {
            Interlocked.Increment(ref probes);
            await Task.Delay(50);
            return Snapshot("Healthy");
        }

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetAsync(Probe, CancellationToken.None)));

        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task A_probe_that_throws_is_not_cached()
    {
        var cache = new HealthSnapshotCache(TimeSpan.FromSeconds(15), new MovableClock(DateTimeOffset.UnixEpoch));
        var probes = 0;
        Task<HealthResponse> Probe(CancellationToken _)
        {
            probes++;
            return probes == 1 ? throw new InvalidOperationException("boom") : Task.FromResult(Snapshot("Healthy"));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(Probe, CancellationToken.None));
        var second = await cache.GetAsync(Probe, CancellationToken.None);

        Assert.Equal("Healthy", second.Status);
        Assert.Equal(2, probes);
    }

    [Theory]
    [InlineData(null, 15)]
    [InlineData("30", 30)]
    [InlineData("0", 15)]
    [InlineData("abc", 15)]
    public void The_window_comes_from_Health_CacheSeconds_and_falls_back_to_fifteen(string? configured, int expectedSeconds)
    {
        var settings = new Dictionary<string, string?>();
        if (configured is not null)
        {
            settings["Health:CacheSeconds"] = configured;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), HealthSnapshotCache.WindowFrom(configuration));
    }
}
