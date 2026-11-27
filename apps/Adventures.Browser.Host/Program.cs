using Adventures.Data.NQuad;
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Adventures.Browser.Host;
using Adventures.Browser.Host.Controllers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Signs in with a token from the main API: the same shared Jwt settings (issuer, audience, signing key), so no login or user store lives here.
builder.Services.AddSharedJwtAuthentication(builder.Configuration);
builder.Services.AddRoleAuthorization(BrowserAuth.AdminRole);
builder.Services.AddRequestHardening(builder.Configuration);

// The store: the same PostgreSQL N-Quad table as the main app when ConnectionStrings:Postgres is set, otherwise an in-memory store seeded with the
// generic model (schemas, users, roles, tenants, exception log) so the browser can be tried on its own.
var postgres = builder.Configuration.GetConnectionString("Postgres");
INQuadStore store = string.IsNullOrWhiteSpace(postgres) || string.Equals(builder.Configuration["NQuadStore:Provider"], "Memory", StringComparison.OrdinalIgnoreCase)
    ? new InMemoryNQuadStore()
    : NQuadStoreFactory.CreatePostgres(postgres, builder.Configuration["NQuadStore:Layout"], builder.Configuration["NQuadStore:Table"], builder.Configuration["NQuadStore:Prefix"]);
if (store is InMemoryNQuadStore)
{
    var seeds = Path.Combine(AppContext.BaseDirectory, "Sql", "seed");
    foreach (var seed in new[] { "seed.nq", "seed-002-exception-log.nq", "seed-003-exception-signature.nq", "seed-004-identity.nq", "seed-005-identity-required.nq" })
    {
        await store.SeedFromFileAsync(Path.Combine(seeds, seed));
    }
}

var types = BrowserCatalog.Build(await store.QueryAsync());
builder.Services.AddSingleton(store);
builder.Services.AddSingleton<IReadOnlyList<BrowserType>>(types);
foreach (var type in types)
{
    builder.Services.AddSingleton(type.Registration);
}

builder.Services.AddNQuadRelations();

var app = builder.Build();

// first of all: behind a CDN or proxy the visitor address and scheme come from the listed proxies only (Proxy:Trusted; off by default)
app.UseTrustedProxies(app.Configuration);

app.UseBrowserSecurityHeaders(app.Environment.IsDevelopment());
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

/// <summary>Public so the test host can start the app.</summary>
public partial class Program;
