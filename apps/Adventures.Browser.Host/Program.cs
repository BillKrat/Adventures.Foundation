using Adventures.Browser.Gateway.WebApi;
using Adventures.WebApi;

// The schema browser applications' one API address. It has no database, no users and no secrets: it forwards sign-in and read-only schema requests to
// the sites listed in the BrowserGateway settings of this installation.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddTrustedProxies(builder.Configuration);
builder.Services.AddBrowserGateway(builder.Configuration);

var app = builder.Build();

// first of all: behind a CDN or proxy the visitor address and scheme come from the listed proxies only (Proxy:Trusted; off by default)
app.UseTrustedProxies(app.Configuration);
app.UseSecurityHeaders(app.Environment.IsDevelopment());
app.UseBrowserGateway();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy" }));
app.Run();

/// <summary>Public so the test host can start the app.</summary>
public partial class Program;
