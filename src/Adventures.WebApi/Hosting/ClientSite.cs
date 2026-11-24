using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace Adventures.WebApi;

/// <summary>
/// Single-site mode: the API serves the Angular client from its web root (the install default, so one temporary address is the whole app; the split `www` and `api.` layout is unchanged
/// when the web root holds no client). The client page has a Content-Security-Policy of its own built from the page itself: scripts only from this site plus the hash of each inline
/// script the build emitted, nothing else inline, no eval, no framing, requests only to this site. Client routes fall back to the page for reads; the API, other methods and missing files never do.
/// </summary>
public sealed partial class ClientSite
{
    public const string SettingName = "Hosting:ServeClient";

    private ClientSite(string root, string indexHtml, string policy)
    {
        Root = root;
        IndexHtml = indexHtml;
        ContentSecurityPolicy = policy;
    }

    public string Root { get; }

    public string IndexHtml { get; }

    public string ContentSecurityPolicy { get; }

    [GeneratedRegex(@"<script(?![^>]*\bsrc\s*=)[^>]*>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineScripts();

    [GeneratedRegex(@"-[A-Za-z0-9_-]{6,}\.(js|css|woff2?|mjs)$", RegexOptions.CultureInvariant)]
    private static partial Regex HashedName();

    /// <summary>The client, when the web root holds a build (an index.html) and the setting does not switch it off; otherwise null (the host is the API alone).</summary>
    public static ClientSite? TryLoad(string? webRoot, IConfiguration configuration)
    {
        if (string.Equals(configuration[SettingName]?.Trim(), "false", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(webRoot)
            || !File.Exists(Path.Combine(webRoot, "index.html")))
        {
            return null;
        }

        var html = File.ReadAllText(Path.Combine(webRoot, "index.html"));
        return new ClientSite(webRoot, html, PolicyFor(html));
    }

    /// <summary>The policy for a page: every inline script is allowed by its SHA-256 hash and nothing else inline is. Styles may be inline (the component library needs it) and come from this site and Google Fonts.</summary>
    public static string PolicyFor(string html)
    {
        var hashes = InlineScripts().Matches(html)
            .Select(m => m.Groups["body"].Value)
            .Where(body => !string.IsNullOrWhiteSpace(body))
            .Select(body => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body))) + "'")
            .Distinct(StringComparer.Ordinal);
        var scripts = string.Join(' ', new[] { "'self'" }.Concat(hashes));
        return "default-src 'self'; script-src " + scripts + "; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com data:; "
            + "img-src 'self' data: blob: https:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    }

    /// <summary>Static files (and the page at the root) ahead of tenancy: an asset needs no tenant. Replaces the API's strict policy on the page, and sets the cache rules.</summary>
    public void Use(WebApplication app)
    {
        var files = new PhysicalFileProvider(Root);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = context =>
            {
                var headers = context.Context.Response.Headers;
                var name = context.File.Name;
                if (name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                {
                    headers["Content-Security-Policy"] = ContentSecurityPolicy;
                    headers["Cache-Control"] = "no-cache";
                }
                else
                {
                    headers["Cache-Control"] = HashedName().IsMatch(name) ? "public, max-age=31536000, immutable" : "no-cache";
                }
            },
        });

        // A missing file (a path with an extension, outside /api) is a plain 404: never the page, and not the API's 401 for an unknown route.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                && !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                && Path.HasExtension(path.Value))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });
    }

    /// <summary>Client routes (no file extension, not under /api) answer the page; the API, a missing file and any method but GET or HEAD never do.</summary>
    public void MapFallback(WebApplication app) =>
        app.MapFallback("{*path:nonfile}", (HttpContext context) =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
            {
                return Results.NotFound();
            }

            context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            context.Response.Headers["Cache-Control"] = "no-cache";
            return Results.Content(IndexHtml, "text/html; charset=utf-8");
        }).AllowAnonymous().ExcludeFromDescription();
}
