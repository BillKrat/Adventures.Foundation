using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Data.NQuad.Tests;

[Collection(NQuadStorePostgresCollection.Name)]
public class NQuadStoreTests
{
    /// <summary>
    /// Builds the minimal DI container used by these tests: an <see cref="IConfiguration"/> (user secrets +
    /// environment variables, same resolution order as <see cref="NQuadStoreContract.PostgresConnectionFixture"/>)
    /// plus a <see cref="ServiceCollection"/> that interface implementations get registered against.
    /// </summary>
    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<NQuadStoreTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);

        // Concrete registrations for the two supported stores. The Postgres registration is
        // conditional based on whether a connection string was supplied so tests that don't
        // opt into Postgres won't throw during DI setup.
        services.AddSingleton<InMemoryNQuadStore>();

        var postgresConn = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrEmpty(postgresConn))
        {
            // Use the AddSingleton<TService, TImplementation>(Func<IServiceProvider, TImplementation>)
            // overload since NpgsqlNQuadStore requires a connection string that only the
            // configuration (resolved from the container) can supply.
            services.AddSingleton<INQuadStore, NpgsqlNQuadStore>(sp => new NpgsqlNQuadStore(postgresConn));
        }

        // Register a simple keyed factory: Func<string, INQuadStore> where the key is
        // "memory" or "postgres". Callers can resolve this factory and request the
        // desired implementation by name.
        services.AddSingleton<Func<string, INQuadStore>>(sp => key =>
        {
            return key switch
            {
                "memory" => sp.GetRequiredService<InMemoryNQuadStore>(),
                "postgres" => sp.GetService<INQuadStore>() ?? throw new KeyNotFoundException($"No INQuadStore registered for key '{key}'."),
                _ => throw new KeyNotFoundException($"No INQuadStore registered for key '{key}'.")
            };
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public void TestMethod1()
    {
        using var provider = BuildServiceProvider();

        var configuration = provider.GetRequiredService<IConfiguration>();

        Assert.NotNull(configuration);
    }

    [Fact]
    public async Task KeyedFactory_ResolvesInMemoryStore_ForMemoryKey()
    {
        using var provider = BuildServiceProvider();
        var factory = provider.GetRequiredService<Func<string, INQuadStore>>();

        var store = factory("memory");

        Assert.IsType<InMemoryNQuadStore>(store);
        await NQuadStoreTestSupport.RunContractChecksAsync(store);
    }

    [Fact]
    public async Task KeyedFactory_ResolvesPostgresStore_ForPostgresKey()
    {
        using var provider = BuildServiceProvider();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var postgresConn = configuration.GetConnectionString("Postgres");
        var factory = provider.GetRequiredService<Func<string, INQuadStore>>();

        if (string.IsNullOrEmpty(postgresConn))
        {
            Assert.Throws<KeyNotFoundException>(() => factory("postgres"));
            return;
        }

        var store = factory("postgres");

        Assert.IsType<NpgsqlNQuadStore>(store);
        await NQuadStoreTestSupport.PurgeAndReseedAsync(store);
    }
}
