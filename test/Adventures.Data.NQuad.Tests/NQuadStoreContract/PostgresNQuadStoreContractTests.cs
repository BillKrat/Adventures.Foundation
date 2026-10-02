using Npgsql;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// Runs the shared store contract against <see cref="NpgsqlNQuadStore"/> when "ConnectionStrings:Postgres" is configured.
/// Each fact gets its own scratch table (dropped afterwards), so the dev "n_quads" table is never truncated.
/// </summary>
public sealed class PostgresNQuadStoreContractTests : NQuadStoreContractTests
{
    private static readonly PostgresConnectionFixture Connection = new();
    private string? _table;

    protected override async Task<INQuadStore?> CreateStoreAsync()
    {
        if (Connection.ConnectionString is null) return null;

        _table = "nqc_" + Guid.NewGuid().ToString("N");
        var store = new NpgsqlNQuadStore(Connection.ConnectionString, _table);
        await store.InitializeAsync();
        return store;
    }

    protected override async Task DisposeStoreAsync()
    {
        if (_table is null || Connection.ConnectionString is null) return;

        await using var connection = new NpgsqlConnection(Connection.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP TABLE IF EXISTS {_table};", connection);
        await command.ExecuteNonQueryAsync();
    }
}
