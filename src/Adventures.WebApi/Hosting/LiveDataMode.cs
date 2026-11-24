using Microsoft.Extensions.Configuration;
// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.WebApi;

/// <summary>
/// "Live data" mode for local development: the app on the developer's machine reads and writes the production store, so the
/// human's replies in the conversation under a post land where Claude reads them. It is opt-in (<c>LiveData:Enabled</c>), takes the
/// connection string from <c>ConnectionStrings:LiveData</c> (user-secrets, from the vault; never a file in the repo), refuses to run
/// outside the Development environment, and (unless LiveData:SkipSeeding) applies pending seeds like any start: the store is the shared development
/// environment, so a branch with a new schema runs against it (named seeds apply once and only add). The health response and the banner tell the person it is on.
/// </summary>
public static class LiveDataMode
{
    public const string EnabledKey = "LiveData:Enabled";
    public const string ConnectionStringKey = "ConnectionStrings:LiveData";
    public const string SkipSeedingKey = "LiveData:SkipSeeding";

    /// <summary>True when live data mode was asked to leave the store untouched (no table creation, no seeding).</summary>
    public static bool SkipSeeding(IConfiguration configuration) =>
        string.Equals(configuration[SkipSeedingKey], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when live data mode is on, after pointing <c>ConnectionStrings:Postgres</c> at the live store. Throws, with the
    /// reason, when it is switched on outside Development or without a connection string.
    /// </summary>
    public static bool Apply(IConfigurationManager configuration, bool isDevelopment)
    {
        if (!string.Equals(configuration[EnabledKey], "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!isDevelopment)
        {
            throw new InvalidOperationException("LiveData:Enabled is only allowed in the Development environment.");
        }

        var connection = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("LiveData:Enabled is on but ConnectionStrings:LiveData is not set (see docs/artifacts/Claude-2026-10-23-live-data-profile.md for the usersecret command).");
        }

        configuration["ConnectionStrings:Postgres"] = connection;
        return true;
    }

    /// <summary>Host and database of a connection string, never the credentials: safe to log so a run says which store it is on.</summary>
    public static string Describe(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection))
        {
            return "none";
        }

        try
        {
            var parts = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connection };
            string Get(string key) => parts.TryGetValue(key, out var v) ? v?.ToString() ?? "?" : "?";
            return $"{Get("Host")}/{Get("Database")}";
        }
        catch (ArgumentException)
        {
            return "unreadable";
        }
    }
}
