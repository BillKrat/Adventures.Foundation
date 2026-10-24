// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
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

