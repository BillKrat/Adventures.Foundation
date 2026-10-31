// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.WebApi;
using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Adventures.Blog;

namespace Adventures.Blog.WebApi;

/// <summary>One stored image as the admin sees it: where it is served from, and the Markdown that shows it.</summary>
public sealed record MediaView(Guid Id, string Name, string ContentType, int Length, string UploadedBy, DateTimeOffset UploadedAt, string Url, string Markdown);

/// <summary>
/// Images for posts and pages. Upload and listing are for administrators; serving is anonymous (a post is public). An upload is trusted
/// for nothing it claims: <see cref="MediaRules"/> accepts only PNG, JPEG, GIF and WebP whose first bytes match, up to 5 MB, under a
/// rebuilt file name. An image is served inline with <c>nosniff</c> and a locked-down content policy, and cached for a year because its
/// address holds its id (the same bytes are never stored twice, and an id never changes its bytes).
/// </summary>
[ApiController]
[Route("api/media")]
public sealed class MediaController(IMediaStore store) : ControllerBase
{
    /// <summary>Uploads one image (multipart form field <c>file</c>); answers 201 with its address and a ready-made Markdown image.</summary>
    [HttpPost]
    [RequireRight(Rights.MediaUpload)]
    [RequestSizeLimit(MediaRules.MaxBytes + 64 * 1024)]
    public async Task<ActionResult<MediaView>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > MediaRules.MaxBytes)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: $"The image is larger than {MediaRules.MaxBytes / (1024 * 1024)} MB.");
        }

        using var memory = new MemoryStream((int)Math.Min(file.Length, MediaRules.MaxBytes));
        await file.CopyToAsync(memory, cancellationToken);
        var data = memory.ToArray();

        var check = MediaRules.Check(file.FileName, file.ContentType, data);
        if (!check.IsAccepted)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: check.Error);
        }

        var saved = await store.SaveAsync(check.FileName, check.ContentType, data, User.Identity?.Name ?? "unknown", cancellationToken);
        return StatusCode(StatusCodes.Status201Created, View(saved));
    }

    /// <summary>The stored images, newest first.</summary>
    [HttpGet]
    [RequireRight(Rights.MediaUpload)]
    public async Task<ActionResult<IReadOnlyList<MediaView>>> List(CancellationToken cancellationToken) =>
        Ok((await store.ListAsync(cancellationToken)).Select(View).ToList());

    /// <summary>Serves an image. The name in the address is for readers of the Markdown; only the id selects the file.</summary>
    [HttpGet("{id:guid}/{name?}")]
    [AllowAnonymous]
    public async Task<IActionResult> Serve(Guid id, string? name, CancellationToken cancellationToken)
    {
        var file = await store.GetAsync(id, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        Response.Headers.ETag = $"\"{file.Info.Sha256}\"";
        return File(file.Data, file.Info.ContentType);
    }

    private MediaView View(MediaInfo info)
    {
        var url = $"{Request.Scheme}://{Request.Host}/api/media/{info.Id}/{info.FileName}";
        return new MediaView(info.Id, info.FileName, info.ContentType, info.Length, info.UploadedBy, info.UploadedAt, url, $"![{Path.GetFileNameWithoutExtension(info.FileName)}]({url})");
    }
}
