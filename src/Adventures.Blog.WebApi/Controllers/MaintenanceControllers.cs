using System.Text.Json;
using Adventures.Blog;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Blog.WebApi;

/// <summary>Downloads a JSON backup of the tenant's posts and pages (drafts included). Administrators with the site-settings right only; it is a read, never a change.</summary>
[ApiController]
[Route("api/export")]
[RequireRight(Rights.SettingsManage)]
public sealed class ExportController(IContentExporter exporter) : ApiControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [HttpGet("content")]
    public async Task<IActionResult> Content(CancellationToken cancellationToken)
    {
        var document = await exporter.ExportAsync(cancellationToken);
        return File(JsonSerializer.SerializeToUtf8Bytes(document, Json), "application/json", $"content-export-{document.ExportedUtc:yyyyMMdd-HHmm}.json");
    }
}

/// <summary>Clears what the server remembers about which site a host reaches (tenants and the sites the API answers); everything is read again on the next request.</summary>
[ApiController]
[Route("api/cache")]
[RequireRight(Rights.SettingsManage)]
public sealed class CacheController(TenantCacheInvalidator? caches = null) : ApiControllerBase
{
    [HttpPost("clear")]
    public IActionResult Clear()
    {
        caches?.Invalidate();
        return NoContent();
    }
}
