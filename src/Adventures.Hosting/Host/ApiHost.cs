using System.Reflection;
using Adventures.Blog;
using Adventures.Blog.Legacy;
using Adventures.Blog.Postgres;
using Adventures.Data;
using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.Entities.NQuad;
using Adventures.Ioc.Extensions;
using Adventures.MEF;
using Adventures.MEF.WebApi;
using Adventures.Security;
using Adventures.WebApi;
using Serilog;

namespace Adventures.Hosting;

/// <summary>What differs between API hosts built on <see cref="ApiHost"/>: its name, its assembly, the seeds and schemas of the features it uses.</summary>
public sealed class ApiHostOptions
{
    /// <summary>The name the root banner reports.</summary>
    public required string ServiceName { get; init; }

    /// <summary>The host's own assembly: the DI scan loads what it references, and plug-in discovery searches it.</summary>
    public required Assembly HostAssembly { get; init; }

    /// <summary>Besides <c>Adventures.</c>: the name prefix of the host's own libraries for plug-in discovery.</summary>
    public required string AssemblyPrefix { get; init; }

    /// <summary>The seeds of the features the host uses, in application order (each feature library exposes its own list).</summary>
    public required IReadOnlyList<SeedEntry> Seeds { get; init; }

    /// <summary>Schemas the features need; live data mode skips seeding, so a missing one is reported instead of failing in the schema loader.</summary>
    public required IReadOnlyList<string> RequiredSchemas { get; init; }

    /// <summary>The Aspire service defaults of the host's own defaults project (the Hosting library cannot reference an app project).</summary>
    public Action<WebApplicationBuilder>? AddServiceDefaults { get; init; }

    public Action<WebApplication>? MapDefaultEndpoints { get; init; }
}

/// <summary>
/// The whole start-up of a full API host (the last step of the host reuse review): bootstrap logger, configuration checks and the 503 reporting app, live data mode,
/// data mode, logging, platform, identity and plug-in services, the N-Quad store and its seeds, the feature entity registrations, the start-up tasks and the pipeline.
/// A host's <c>Program.cs</c> is this call with its options.
/// </summary>
public static class ApiHost
{
    public static async Task RunAsync(string[] args, ApiHostOptions options)
    {
        // Bootstrap logger: catches and logs anything that goes wrong before the real Serilog pipeline (which needs configuration/DI to be built) is up - including a crash
        // inside WebApplication.CreateBuilder itself. Without it a startup-time exception (e.g. missing required config) produces nothing but IIS's own bare 500 page in
        // production, with zero trace of what actually failed.
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "startup-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateBootstrapLogger();

        try
        {
            var app = await BuildAsync(args, options);
            if (app is null)
            {
                return; // the reporting app ran instead
            }

            // Start-up tasks the features registered (identity schema, role-rights upgrade, first platform administrator); none when live data mode skips seeding.
            if (!app.Configuration.GetValue<bool>("LiveData:SkipSeedingActive"))
            {
                await StartupTaskRunner.RunAsync(app.Services, app.Configuration.GetValue<bool>("Data:LegacyActive"));
            }

            app.UseAppPipeline(options);
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "{Service} terminated unexpectedly during startup", options.ServiceName);
            FailureJournal.Write(FailureJournal.DefaultDirectory, "startup", ex);
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task<WebApplication?> BuildAsync(string[] args, ApiHostOptions options)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Missing configuration is reported, not fatal: a small app answers 503 with what is missing and how to fix it (see StartupChecks),
        // so the page in the browser says so instead of waiting on an API that never started.
        var startupProblems = StartupChecks.Find(builder.Configuration, builder.Environment.IsDevelopment());
        if (startupProblems.Count > 0)
        {
            await StartupChecks.RunReportingAppAsync(args, startupProblems, builder.Environment.IsDevelopment(),
                string.Equals(builder.Configuration[LiveDataMode.EnabledKey], "true", StringComparison.OrdinalIgnoreCase));
            return null;
        }

        // Local development against the live store (opt-in, Development only; see LiveDataMode): the connection is switched here,
        // before anything reads ConnectionStrings:Postgres, and the startup seeding below is skipped.
        var liveData = LiveDataMode.Apply(builder.Configuration, builder.Environment.IsDevelopment());
        if (liveData)
        {
            builder.Configuration["LiveData:Active"] = "true";
        }

        // Which data layer this host runs on (Data:Mode = New | Legacy). Only registrations differ between the two; no Bll, presenter or controller knows the mode.
        // Legacy serves posts, pages, categories and tags from a BlogEngine.NET SQL Server database (ConnectionStrings:LegacySql) and everything else
        // from the in-memory store seeded at start; collaboration is switched off; sign-in is the developer-testing account (see DevLoginOptions).
        var legacy = DataModes.Parse(builder.Configuration[DataModes.SettingName]) == DataMode.Legacy;
        var legacySql = builder.Configuration.GetConnectionString("LegacySql");
        if (legacy && (liveData || string.IsNullOrWhiteSpace(legacySql)))
        {
            throw new InvalidOperationException("Data:Mode=Legacy needs ConnectionStrings:LegacySql and cannot be combined with live data mode.");
        }

        // Every line carries the request's CorrelationId; the text log is for people, the compact JSON sibling for tools.
        builder.Host.UseAdventuresSerilog();
        options.AddServiceDefaults?.Invoke(builder);

        builder.Services.AddAppPlatform(builder.Configuration, builder.Environment.IsDevelopment());

        // Adventures.Data/Adventures.Identity: IUserAccountService for the real login. Lazy: the executor does not open a connection until something queries through it.
        builder.Services.AddAppIdentity(legacy, builder.Configuration, builder.Environment.IsDevelopment());

        // The scan only sees loaded assemblies, so load every referenced library first (see HostAssemblies).
        HostAssemblies.EnsureReferencedLoaded(options.HostAssembly);
        // Plug-ins: exported IPluginModule from the host libraries and, when Plugins:Directory is set, from a folder of approved DLLs (see Adventures.MEF).
        builder.Services.AddPluginDiscovery(builder.Configuration, HostAssemblies.Libraries("Adventures.", options.AssemblyPrefix), [PluginWebRules.Validator]);
        // A plug-in may bring API controllers: only under api/plugins/{key}/, never anonymous, switched off with the plug-in.
        builder.Services.AddControllers().AddPluginControllers();
        // The in-process event bus for plug-ins: notifications run off the request for the plug-ins that are on for the tenant; interceptors may refuse.
        builder.Services.AddPluginEvents();
        // Encrypted secrets (plug-in settings of kind Secret): keys come from Encryption:CurrentKeyId and Encryption:Keys:<id> (host secrets, never the store).
        builder.Services.AddFieldEncryption(builder.Configuration);
        builder.Services.AddSingleton<IPluginEventContext, RequestTenantEventContext>();
        builder.Services.AddLifetimeServices();

        // The N-Quad store is PostgreSQL-backed whenever ConnectionStrings:Postgres is configured, in a table of its own (never "n_quads", which the Foundation test suite
        // purges and reseeds). NQuadStore:Provider = "Memory" opts out (offline development). The baseline seed is applied once and never merged again.
        var postgresConnection = builder.Configuration.GetConnectionString("Postgres");
        var useMemoryStore = string.Equals(builder.Configuration["NQuadStore:Provider"], "Memory", StringComparison.OrdinalIgnoreCase)
            || legacy
            || string.IsNullOrWhiteSpace(postgresConnection);
        INQuadStore nquadStore = useMemoryStore
            ? new InMemoryNQuadStore()
            : NQuadStoreFactory.CreatePostgres(postgresConnection!, builder.Configuration["NQuadStore:Layout"], builder.Configuration["NQuadStore:Table"], builder.Configuration["NQuadStore:Prefix"]);
        var seeded = false;
        // Live data mode runs on the shared development store, so it applies pending seeds like any start (named seeds apply once and only add schema and defaults).
        // Set LiveData:SkipSeeding=true to leave the store untouched instead.
        var skipSeeding = liveData && LiveDataMode.SkipSeeding(builder.Configuration);
        if (liveData)
        {
            Log.Warning(skipSeeding
                ? "LIVE DATA mode: this local run uses the shared store; startup seeding is skipped (LiveData:SkipSeeding)"
                : "LIVE DATA mode: this local run uses the shared store and applies pending seeds to it");
        }

        if (!skipSeeding)
        {
            seeded = await SeedRunner.ApplyAsync(nquadStore, SeedProfile.BaselineFile(builder.Configuration, builder.Environment.IsDevelopment()), options.Seeds);
        }

        Log.Information("N-Quad store: {Provider} at {Store}{Live}; baseline seed {SeedState}", useMemoryStore ? "memory" : "postgres", LiveDataMode.Describe(postgresConnection), liveData ? " (LIVE DATA)" : "", seeded ? "applied" : "already applied");
        var userQuads = await nquadStore.QueryAsync();
        if (skipSeeding && StartupChecks.FindMissingSchemas(userQuads.Select(q => (q.Subject, q.Predicate, q.Object)), options.RequiredSchemas) is { Count: > 0 } missingSchemas)
        {
            await StartupChecks.RunReportingAppAsync(args, missingSchemas, builder.Environment.IsDevelopment(), liveData);
            return null;
        }

        builder.Services.AddSingleton<INQuadStore>(nquadStore);
        if (legacy)
        {
            // The whole Legacy switch: the SQL Dals are registered first, so the repository registrations below add no Dal for these types.
            builder.Services.AddBlogLegacy(legacySql!, Guid.TryParse(builder.Configuration["Legacy:BlogId"], out var legacyBlogId) ? legacyBlogId : null);
            // No stored plug-in state in this mode: the collaboration plug-ins are fixed off (see LegacyModePlugins).
            builder.Services.AddScoped<IPlugins, LegacyModePlugins>();
            Log.Information("Data mode: Legacy (BlogEngine SQL Server for posts, pages, categories and tags; collaboration off)");
        }

        // Uploaded images: in the same PostgreSQL database as the content (survives a redeploy, travels with the backup); memory for the in-memory development store.
        builder.Services.AddSingleton<IMediaStore>(useMemoryStore ? new InMemoryMediaStore() : new NpgsqlMediaStore(postgresConnection!, builder.Configuration["Media:Table"] ?? "app_media"));
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddAppEntities(userQuads, builder.Configuration, builder.Environment.IsDevelopment());

        // what the start-up that follows needs to know without re-deriving it
        builder.Configuration["Data:LegacyActive"] = legacy ? "true" : "false";
        builder.Configuration["LiveData:SkipSeedingActive"] = skipSeeding ? "true" : "false";

        var app = builder.Build();

        // A plug-in or DLL that did not load is never silent: one warning per refusal here, and the whole report on the Plugins page.
        foreach (var refused in app.Services.GetRequiredService<PluginLoadReport>().Failures)
        {
            app.Logger.LogWarning("Plug-in not loaded ({Source}): {Reason}", refused.Source, refused.Reason);
        }

        return app;
    }
}
