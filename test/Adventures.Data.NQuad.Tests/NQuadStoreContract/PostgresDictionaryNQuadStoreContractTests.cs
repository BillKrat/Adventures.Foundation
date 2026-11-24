using Npgsql;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// Runs the shared store contract against <see cref="NpgsqlDictionaryNQuadStore"/> (the hybrid-dictionary layout) when "ConnectionStrings:Postgres" is configured. Each fact gets its own scratch
/// pair of tables (dropped afterwards), so no real table is ever touched.
/// </summary>
public sealed class PostgresDictionaryNQuadStoreContractTests : NQuadStoreContractTests
{
    private static readonly PostgresConnectionFixture Connection = new();
    private string? _prefix;

    protected override async Task<INQuadStore?> CreateStoreAsync()
    {
        if (Connection.ConnectionString is null) return null;

        _prefix = "dqc_" + Guid.NewGuid().ToString("N");
        var store = new NpgsqlDictionaryNQuadStore(Connection.ConnectionString, _prefix);
        await store.InitializeAsync();
        return store;
    }

    protected override async Task DisposeStoreAsync()
    {
        if (_prefix is null || Connection.ConnectionString is null) return;

        await using var connection = new NpgsqlConnection(Connection.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP TABLE IF EXISTS {_prefix}_quads; DROP TABLE IF EXISTS {_prefix}_terms;", connection);
        await command.ExecuteNonQueryAsync();
    }
}
