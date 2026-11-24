// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Adventures.WebApi;

/// <summary>One thing the configuration lacks, and what to do about it. Never carries a secret value.</summary>
public sealed record StartupProblem(string Key, string Message, string Fix);

/// <summary>
/// Checks the configuration the API cannot start without, before anything reads it, and - when something is missing -
/// keeps a small "reporting" app running in place of the API. That app answers every request with a 503 that says what
/// is missing and how to fix it (the health check included, so the badge in the page header says it too), instead of the
/// process dying and the page waiting on a proxy that has nothing to proxy to. The failure is also logged and written to the
/// failure journal, so it appears in the exception log at the next healthy start. Detail is only given in Development;
/// anywhere else the answer is a generic 503.
/// </summary>
public static class StartupChecks
{
    public const string ProblemTitle = "The API could not start: its configuration is incomplete.";

    /// <summary>The missing pieces, empty when the API can start. Reads only; applies nothing.</summary>
    public static IReadOnlyList<StartupProblem> Find(IConfiguration configuration, bool isDevelopment)
    {
        var problems = new List<StartupProblem>();

        try
        {
            _ = SiteDomain.Domain(configuration);
        }
        catch (InvalidOperationException ex)
        {
            problems.Add(new StartupProblem(SiteDomain.Key, ex.Message, "Set Site__Domain to your bare domain name, for example example.com, or remove it."));
        }

        if (string.Equals(configuration[LiveDataMode.EnabledKey], "true", StringComparison.OrdinalIgnoreCase))
        {
            if (!isDevelopment)
            {
                problems.Add(new StartupProblem(LiveDataMode.EnabledKey, "Live data mode is only allowed in the Development environment.",
                    "Remove LiveData:Enabled, or run in Development."));
            }
            else if (string.IsNullOrWhiteSpace(configuration[LiveDataMode.ConnectionStringKey]))
            {
                problems.Add(new StartupProblem(LiveDataMode.ConnectionStringKey, "Live data mode is on but its connection string is not set.",
                    "Store it with the usersecret command in docs/artifacts/Claude-2026-10-23-live-data-profile.md, or start the normal 'https' profile."));
            }
        }
        else if (!isDevelopment && string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Postgres"]))
        {
            problems.Add(new StartupProblem("ConnectionStrings:Postgres", "The database connection string is not set.",
                "Set ConnectionStrings__Postgres as a site secret on the host (and restart the site)."));
        }

        if (string.IsNullOrWhiteSpace(configuration["Jwt:SigningKey"]))
        {
            problems.Add(new StartupProblem("Jwt:SigningKey", "The token signing key is not set.",
                "Development: dotnet user-secrets (docs/Claude-secret.ps1 gen). Production: the Jwt__SigningKey site secret."));
        }

        return problems;
    }

    /// <summary>
    /// Schemas the app needs that the store does not hold. Live data mode skips the startup seeding on purpose, so a branch that ships a new
    /// seed cannot run against the production store until that seed has been deployed; this says so instead of failing inside the schema loader.
    /// </summary>
    public static IReadOnlyList<StartupProblem> FindMissingSchemas(IEnumerable<(string Subject, string Predicate, string Object)> quads, IEnumerable<string> requiredSchemaIris)
    {
        var present = quads
            .Where(q => q.Predicate == Adventures.Entities.EntityConstants.Schema.TypePredicate && q.Object == Adventures.Entities.EntityConstants.Schema.TypeIri)
            .Select(q => q.Subject)
            .ToHashSet(StringComparer.Ordinal);
        return requiredSchemaIris
            .Where(iri => !present.Contains(iri))
            .Select(iri => new StartupProblem(iri, "The store has no such schema: live data mode skips the seeding, and this code needs a seed the production store does not have yet.",
                "Run the normal https profile (local database), or deploy this branch first so the seed is applied."))
            .ToList();
    }

    /// <summary>The same message as one exception, for the log and the failure journal.</summary>
    public static InvalidOperationException ToException(IReadOnlyList<StartupProblem> problems) =>
        new($"{ProblemTitle} " + string.Join(" ", problems.Select(p => $"[{p.Key}] {p.Message} {p.Fix}")));

    /// <summary>Logs the problems, journals them, and serves the 503 "reporting" app until the process is stopped.</summary>
    public static async Task RunReportingAppAsync(string[] args, IReadOnlyList<StartupProblem> problems, bool isDevelopment, bool liveData)
    {
        var failure = ToException(problems);
        Log.Fatal(failure, "The API cannot start: {Problems}", problems.Select(p => p.Key));
        FailureJournal.Write(FailureJournal.DefaultDirectory, "startup", failure);

        var builder = WebApplication.CreateBuilder(args);
        var allowedOrigins = SiteDomain.AllowedOrigins(builder.Configuration, builder.Environment.IsDevelopment()).ToArray();
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();
        app.UseCors();

        var detail = isDevelopment ? problems : [];
        app.MapGet("/api/health", () => Results.Json(
            new
            {
                status = "Unhealthy",
                timestampUtc = DateTimeOffset.UtcNow,
                dependencies = new[] { new { name = "configuration", status = "failed", durationMs = 0, detail = isDevelopment ? string.Join(", ", problems.Select(p => p.Key)) + " missing" : "incomplete" } },
                liveData,
            },
            statusCode: StatusCodes.Status503ServiceUnavailable));
        app.MapFallback(() => Results.Json(new { title = ProblemTitle, problems = detail }, statusCode: StatusCodes.Status503ServiceUnavailable));

        await app.RunAsync();
    }
}
