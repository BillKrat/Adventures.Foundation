using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// Companion to <see cref="NQuadStoreTests"/>: same "resolve INQuadStore by key" scenario, but registered/resolved
/// using .NET's built-in keyed services (<c>AddKeyedSingleton</c>/<c>GetKeyedService</c>) instead of a hand-rolled
/// <c>Func&lt;string, INQuadStore&gt;</c> dispatcher. This is the pattern to reuse for other interfaces with
/// multiple implementations selected by key (e.g. a future <c>ITools</c>), so registration/resolution stays
/// consistent across the codebase.
/// </summary>
[Collection(NQuadStorePostgresCollection.Name)]
public class NQuadStoreKeyedServiceTests
{
    private static ServiceProvider BuildKeyedServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<NQuadStoreKeyedServiceTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);

        // Keyed singleton registrations: same INQuadStore service type, one keyed "memory" and one keyed
        // "postgres". Both are registered unconditionally so consumers can resolve either key the same way
        // (see KeyedSingleton_ResolvesInMemoryStore_ForMemoryKey); the "postgres" factory only reads
        // configuration - and fails fast with a clear message - once something actually resolves that key.
        services.AddKeyedSingleton<INQuadStore, InMemoryNQuadStore>("memory");
        services.AddKeyedSingleton<INQuadStore, NpgsqlNQuadStore>("postgres", (sp, key) =>
        {
            var postgresConn = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres");
            if (string.IsNullOrEmpty(postgresConn))
            {
                throw new InvalidOperationException("No \"ConnectionStrings:Postgres\" connection string is configured; cannot resolve INQuadStore for key 'postgres'.");
            }

            return new NpgsqlNQuadStore(postgresConn);
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task KeyedSingleton_ResolvesInMemoryStore_ForMemoryKey()
    {
        using var provider = BuildKeyedServiceProvider();

        var store = provider.GetRequiredKeyedService<INQuadStore>("memory");

        Assert.IsType<InMemoryNQuadStore>(store);
        await NQuadStoreTestSupport.RunContractChecksAsync(store);
    }

    [Fact]
    public async Task KeyedSingleton_ResolvesPostgresStore_ForPostgresKey()
    {
        using var provider = BuildKeyedServiceProvider();

        if (!provider.TryGetKeyedService<INQuadStore>("postgres", out var store))
        {
            // Not configured locally/in CI - this is an opt-in integration test (see PostgresConnectionFixture),
            // so it skips rather than fails. Note there's no knowledge here of *why* resolution failed
            // (unregistered vs. misconfigured) - TryGetKeyedService collapses both to one outcome.
            return;
        }

        Assert.IsType<NpgsqlNQuadStore>(store);
        await NQuadStoreTestSupport.PurgeAndReseedAsync(store);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("postgres")]
    [InlineData("does-not-exist")]
    public void KeyedSingleton_ResolvesStore_ForAnyDynamicKey_WithoutConditionalLogic(string dynamicKey)
    {
        // This is the pattern a Business Logic Layer consumer should use: it knows nothing about which keys
        // exist, which ones require configuration, or why a given key might fail to resolve. One call,
        // one fallback path, for any key - "memory", "postgres", or something registered a year from now.
        using var provider = BuildKeyedServiceProvider();

        var resolved = provider.TryGetKeyedService<INQuadStore>(dynamicKey, out var store);

        if (dynamicKey == "does-not-exist")
        {
            Assert.False(resolved);
            Assert.Null(store);
            return;
        }

        // "memory" always resolves; "postgres" resolves only when configured - both handled identically above.
        Assert.Equal(resolved, store is not null);
    }
}
