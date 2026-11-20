// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Security.Claims;
using Adventures.Entities;
using Adventures.Security;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Adventures.Blog;

using Adventures.Blog.WebApi;

namespace Adventures.Blog.WebApi.Tests;

/// <summary>Uploads are for administrators and trusted for nothing they claim; images are served to anyone, with hardening headers.</summary>
public sealed class MediaControllerTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    private static MediaController Build(IMediaStore store, string user = "Admin")
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], "test")) };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.example.test");
        return new MediaController(store) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private static IFormFile File(string name, string contentType, byte[] data) =>
        new FormFile(new MemoryStream(data), 0, data.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };

    [Fact]
    public async Task AnUpload_ReturnsTheAddressAndReadyMadeMarkdown_UnderTheRebuiltName()
    {
        var controller = Build(new InMemoryMediaStore());

        var result = await controller.Upload(File("My Photo (1).PNG", "image/png", Png), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var view = Assert.IsType<MediaView>(created.Value);
        Assert.Equal("my-photo-1.png", view.Name);
        Assert.Equal($"https://api.example.test/api/media/{view.Id}/my-photo-1.png", view.Url);
        Assert.Equal($"![my-photo-1]({view.Url})", view.Markdown);
        Assert.Equal("Admin", view.UploadedBy);
    }

    [Fact]
    public async Task AFileThatIsNotTheImageItClaims_IsRefusedWithAReason_AndNothingIsStored()
    {
        var store = new InMemoryMediaStore();
        var controller = Build(store);

        var html = await controller.Upload(File("evil.png", "image/png", "<html><script>alert(1)</script></html>"u8.ToArray()), CancellationToken.None);
        var svg = await controller.Upload(File("x.svg", "image/svg+xml", "<svg onload='alert(1)'/>"u8.ToArray()), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, ((ObjectResult)html.Result!).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, ((ObjectResult)svg.Result!).StatusCode);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task AnImage_IsServedInlineWithHardeningHeaders_AndAnUnknownIdIs404()
    {
        var store = new InMemoryMediaStore();
        var saved = await store.SaveAsync("a.png", "image/png", Png, "Admin");
        var controller = Build(store);

        var served = await controller.Serve(saved.Id, "a.png", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(served);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(Png, file.FileContents);
        var headers = controller.Response.Headers;
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Contains("immutable", headers.CacheControl.ToString());
        Assert.Contains("sandbox", headers["Content-Security-Policy"].ToString());
        Assert.IsType<NotFoundResult>(await controller.Serve(Guid.NewGuid(), "x.png", CancellationToken.None));
    }

    [Fact]
    public void OnlyAdministratorsUploadOrList_AndAnyoneMayReadAnImage()
    {
        string? RightOf(string method) =>
            typeof(MediaController).GetMethod(method)!.GetCustomAttributes(typeof(Adventures.WebApi.RequireRightAttribute), true).Cast<Adventures.WebApi.RequireRightAttribute>().SingleOrDefault()?.Right;

        Assert.Equal(Adventures.Entities.Rights.MediaUpload, RightOf(nameof(MediaController.Upload)));
        Assert.Equal(Adventures.Entities.Rights.MediaUpload, RightOf(nameof(MediaController.List)));
        Assert.NotEmpty(typeof(MediaController).GetMethod(nameof(MediaController.Serve))!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
