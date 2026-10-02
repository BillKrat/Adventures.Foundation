using Microsoft.Extensions.Configuration;

namespace Adventures.Data.NQuad.Tests;

/// <summary>
/// Resolves "ConnectionStrings:Postgres" the same way the production app does (user-secrets,
/// then environment variables) so this tool/test project points at the same local Docker
/// Postgres instance without a checked-in connection string. Tests using this fixture skip
/// (rather than fail) when no connection string is configured, since this is an opt-in
/// integration tool, not something CI should require.
/// </summary>
public sealed class PostgresConnectionFixture
{
    public string? ConnectionString { get; }

    public PostgresConnectionFixture()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<PostgresConnectionFixture>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        ConnectionString = configuration.GetConnectionString("Postgres");
    }
}

